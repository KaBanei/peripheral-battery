# 架构说明

## 1. 目标与约束

* **形态**：Windows 托盘常驻程序（WinForms），监控 2.4G / 蓝牙 / USB 外设电量。
* **技术栈**：.NET Framework 4.8 + WinForms，`csc.exe` 直接编译；**无 SDK 工程、无 NuGet、禁止第三方库**。
* **权限**：普通用户即可运行，不安装驱动、不写 `HKLM`、不需要管理员。
* **稳定性**：任何一个数据源失效/抛异常都不能影响主程序；没有设备时界面显示空列表而不是报错。

## 2. 分层与目录

```
src/
  Core/                      与 UI 无关的核心（可单独编译）
    Contracts.cs             IProvider / Peripheral / ProviderStatus / HostUpdate / HID 抽象
    AppConfig.cs             配置模型 + JSON 读写（%APPDATA%\PeripheralBattery\config.json）
    Log.cs                   文件日志（滚动，2MB 上限）
    ProviderHost.cs          后台调度、计时、异常隔离、结果合并
    ProviderRegistry.cs      反射创建所有 Provider（构造签名固定）
    Hid/                     HID 枚举与通道、HID++ 实现（HID 组负责）
    Platform/                平台互操作
      AutoStart.cs           HKCU Run 开机启动
      SetupApiInterop.cs     PnP 设备枚举 + 设备属性读取（蓝牙 Provider 用）
      XInputInterop.cs       xinput1_4/1_3 动态加载与委托（手柄 Provider 用）
    Providers/
      LogitechHidppProvider.cs
      StandardHidBatteryProvider.cs
      BluetoothBatteryProvider.cs
      XInputBatteryProvider.cs
  VgnBatteryProvider.cs             VGN 2.4G 鼠标（实验，VHUB WebHID 协议）
      SimulationProvider.cs
  App/                       WinForms 托盘 UI（UI 组负责）
  app.manifest               DPI 感知等
build.ps1                   编译脚本（csc，x64，输出 dist\app）
docs/                       协议调研、支持矩阵、本文件
installer/                  安装/卸载脚本与说明
```

## 3. 数据流

```
ProviderHost（Timer, 默认 30s）
   └─ Task.Run(DoScan)                       ← 后台线程，不阻塞 UI
        ├─ 逐个 Provider：Stopwatch 计时
        │    ├─ Enabled == false → 记 ProviderStatus(Ok=true, Count=0)
        │    ├─ Scan(ct)  → List<Peripheral>
        │    └─ 抛异常 → 捕获，记 ProviderStatus(Ok=false, Error=...)
        ├─ Merge(所有结果)                    ← 按 Peripheral.MergeKey 去重
        └─ 触发 Updated 事件（UI 线程封送由 UI 层负责）

UI：TrayAppContext / MainForm / FlyoutForm 订阅 Updated，重绘卡片
```

### 去重规则

`Peripheral.MergeKey = (Brand + "|" + (Name 或 VID:PID) + "|" + Kind).ToLowerInvariant()`。
合并时优先保留**有电量数据**的记录；都有或都没有时保留 `Updated` 更新的那条。
典型场景：同一只罗技鼠标同时被 HID++ 与蓝牙属性发现 → 只显示一条。

## 4. 关键类型

| 类型 | 职责 |
| --- | --- |
| `IProvider` | 插件契约：`Key`、`DisplayName`、`Enabled`、`Scan(CancellationToken)`、`Dispose()`；**必须线程安全**，允许抛异常（宿主会兜住） |
| `Peripheral` | 一台设备：Id/Name/Brand/Kind/Connection/Percent/MilliVolts/State/Signal/Provider/Detail/DevicePath/Vid/Pid/Updated |
| `ProviderStatus` | 单个 Provider 本轮状态：Ok/Error/Count/DurationMs/At（设置页与诊断用） |
| `ProviderHost` | 定时调度、串行扫描（`Interlocked` 防重入）、合并、事件分发 |
| `ProviderRegistry` | 反射创建，找不到类型只记日志（因此缺少某个 Provider 也不影响启动） |
| `IAppContext` | 给 Provider 的最小上下文：`Config` + `Log(message)` |
| `AppConfig` | 所有开关（`EnableLogitech` / `EnableStandardHid` / `EnableBluetooth` / `EnableXInput` / `EnableDemo` 等） |

## 5. 线程模型与性能预算

* 扫描在 `Task.Run` 的线程池线程执行，**同一时刻只允许一轮**（`Interlocked.Exchange`）；手动刷新撞上进行中的扫描会延迟 600ms 重试。
* 取消：`Dispose` 时 `CancellationTokenSource.Cancel()`，Provider 应在循环里检查 `ct.IsCancellationRequested`。
* 预算（本机实测，x64）：蓝牙 Provider 首次 ~20ms、稳态 ~9-13ms；XInput ~2-4ms；两者之和远低于 100ms。HID Provider 视设备数量可能更慢，`RefreshSeconds` 默认 30s。
* Provider 内部如需加锁（例如 XInput 缓存 DLL 句柄），自行加锁；宿主不保证重入安全之外的任何东西。

## 6. 如何新增一个 Provider（插件化扩展路径）

以新增「某品牌鼠标」为例，按顺序做：

1. **写实现**：在 `src/Core/Providers/` 新建 `XxxBatteryProvider.cs`，实现 `IProvider`：

```
namespace Pb.Core.Providers
{
    public sealed class XxxBatteryProvider : IProvider
    {
        private readonly AppConfig _config;
        private readonly IAppContext _ctx;

        public XxxBatteryProvider(AppConfig cfg, IAppContext ctx) { _config = cfg; _ctx = ctx; }

        public string Key { get { return "xxx"; } }
        public string DisplayName { get { return "某品牌设备"; } }
        public bool Enabled { get { return _config != null && _config.EnableXxx; } }

        public List<Peripheral> Scan(CancellationToken ct)
        {
            var list = new List<Peripheral>();
            if (!Enabled) return list;
            try { /* 读设备、填 Peripheral */ }
            catch (Exception ex) { Log.Warn("品牌扫描失败: " + ex.Message); } // 不抛给宿主
            return list;
        }

        public void Dispose() { }
    }
}
```

   要求：构造签名必须是 `(AppConfig, IAppContext)`；`Scan` 里检查 `ct`；所有 IO 都有超时；读不到电量就**跳过**，不要编造。

2. **注册**：在 `src/Core/ProviderRegistry.cs` 的 `CreateAll` 里加一行：

```
Add(list, "Pb.Core.Providers.XxxBatteryProvider", config, ctx);
```

3. **配置开关**：在 `src/Core/AppConfig.cs` 增加 `public bool EnableXxx { get; set; }`，构造里默认 `true`，并在设置界面加对应复选框（`src/App/SettingsForm.cs`）。
4. **文档**：更新 `docs/support-matrix.md`（品牌/系列/状态/证据）与 `docs/protocols.md`（协议细节 + 来源链接）。
5. **验证**：
   * 编译：`powershell -ExecutionPolicy Bypass -File build.ps1`（UI 未就绪时可用第 7 节的子集编译）；
   * 运行时：启动程序看卡片；或写临时控制台直接 `new XxxBatteryProvider(cfg, ctx).Scan(CancellationToken.None)`；
   * 异常路径：拔掉设备、禁用开关、传已取消的 token，都必须返回空列表且无异常。
6. **回归**：确认其他 Provider 的 `ProviderStatus` 仍为 `Ok`，且同一设备没有重复出现（`MergeKey` 是否合理）。

> 注意 `ProviderRegistry.cs`、`AppConfig.cs`、`build.ps1`、`src/App/` 属于核心/UI 边界，改动需与对应负责人协调（见任务板写作用域）。

## 7. 编译与验证方法

**完整构建**（需要 `src/App` 与 `src/Core` 都可编译）：

```
powershell -ExecutionPolicy Bypass -File build.ps1
powershell -ExecutionPolicy Bypass -File build.ps1 -Probe    # 附带诊断工具
```

**只验证 Core 与自己的 Provider**（UI 未就绪时的常用手段）：

```
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$srcs = @('src\Core\Contracts.cs','src\Core\AppConfig.cs','src\Core\Log.cs',
          'src\Core\Platform\SetupApiInterop.cs','src\Core\Platform\XInputInterop.cs',
          'src\Core\Providers\BluetoothBatteryProvider.cs','src\Core\Providers\XInputBatteryProvider.cs',
          '_scratch\PlatformTest.cs') | ForEach-Object { (Resolve-Path $_).Path }
& $csc /nologo /target:exe /platform:x64 /codepage:65001 /main:PlatformTest.Program `
       /out:_scratch\PlatformTest.exe /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll $srcs
```

注意：`csc.exe` 在 .NET Framework 4.x 下是 **C# 5**，不要用字符串插值、`?.`、`nameof`、表达式体成员等新语法；中文源码必须带 `/codepage:65001`。

## 8. 权限与安全边界

| 能力 | 需要的权限 | 说明 |
| --- | --- | --- |
| 枚举 PnP 设备 / 读设备属性 | 普通用户 | `SetupDiGetClassDevs` 只读，无需管理员 |
| 打开 HID 设备（罗技/标准 HID） | 普通用户 | 系统 HID 驱动已提供访问；不安装驱动 |
| 读取蓝牙电量属性 | 普通用户 | 设备需已配对并连接；属性由蓝牙栈写入 |
| XInput | 普通用户 | 系统自带 DLL，动态加载 |
| 开机启动 | 当前用户 | 只写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| 配置/日志 | 当前用户 | `%APPDATA%\PeripheralBattery\`，安装目录之外 |

安全约束：
* 不注入进程、不读厂商软件内存、不绕过签名；
* 不写 `HKLM`、不注册服务、不装驱动；
* 网络：无（本程序不联网）；
* 未签名 exe 会触发 SmartScreen，见 `installer/README-installer.md`。

## 9. 已知限制与降级策略

* **蓝牙**：只读系统属性，系统没值就跳过；不做 GATT 直连。
* **XInput**：只有 4 档，`Percent` 是估算值且在 `Detail` 标注；有线手柄 `Percent = null`。
* **HID++**：需要独占/共享打开 HID 通道；被厂商软件占用时可能读不到。
* **合并**：同名同类型不同设备可能被 `MergeKey` 合并（罕见）；需要时用更细的 `Id` 调整。
* **32/64 位**：统一编译为 x64，避免 AnyCPU 在 64 位系统上加载 32 位 HID 组件的问题。
* **日志**：`%APPDATA%\PeripheralBattery\app.log`，超过 2MB 轮转为 `.1`。

## 10. 故障排查顺序

1. 看 `app.log` 有没有 `Provider xxx 扫描异常`；
2. 设置页看每个 Provider 的 `ProviderStatus`（Ok/Count/DurationMs）；
3. 单 Provider 复现：临时控制台直接调 `Scan`，排除 UI 因素；
4. 蓝牙：设备管理器确认设备节点存在且已连接；
5. 手柄：`joy.cpl` 或系统设置里确认手柄在线；
6. 罗技：厂商软件（G HUB / Options+）是否占用 HID 通道。