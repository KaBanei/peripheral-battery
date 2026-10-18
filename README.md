# 2.4G 外设电量监控

常驻 Windows 通知区域的轻量电量监控工具：把 2.4G / 蓝牙无线外设（鼠标、键盘、耳机、手柄）的电量放在右下角，随时能看。

**绿色免安装**：单个 exe，只依赖 Windows 自带的 .NET Framework 4.8；无驱动、无需管理员、不联网、不上传任何数据。

![主面板](docs/images/main.png)

## 功能

- **托盘实时电量**：动态电池图标 + 圆环数字，绿（≥50%）/ 黄（20–49%）/ 红（<20%）/ 蓝（充电）/ 灰（未知）；多设备时默认显示电量最低的一台（可在设置里改为平均/最高）。
- **悬浮面板**：左键托盘图标，列出全部设备、电量条、充电状态；点“设置”可直接调整。
- **主面板**：设备详情（品牌、连接方式、VID:PID、电压、数据源、HID 路径）+ 数据源健康状态（每个 Provider 的耗时与错误）。
- **低电量提醒**：低于阈值（默认 20%）弹一次气泡，回到阈值 +5 以上后重置。
- **开机启动 / 刷新间隔 / 数据源开关 / 演示模式**：都在设置里。
- **诊断信息**：一键复制（系统版本、DPI、配置路径、Provider 状态、设备列表、日志路径），方便反馈问题。
- 单实例、全局异常兜底、日志轮转、配置自动回退（%APPDATA% 不可写时用 exe 旁的 data 目录）。

| 悬浮面板 | 设置 |
| --- | --- |
| ![悬浮面板](docs/images/flyout.png) | ![设置](docs/images/settings.png) |

![托盘图标状态](docs/images/tray-icons.png)

## 先说清楚：能支持哪些设备

2.4G 无线外设**没有统一电量标准**。每家的接收器（dongle）多数跑私有协议，Windows 本身也不提供通用 API。所以本工具采用“数据源插件”结构，当前实现并经过验证/测试的是：

| 数据源 | 覆盖设备 | 精度 | 状态 |
| --- | --- | --- | --- |
| 罗技 HID++ 2.0 | 罗技办公系列（MX 等）、G 系列 LIGHTSPEED，Unifying / Bolt / Lightspeed 接收器 | 百分比，或电压换算 | ✅ 已实现（本机无罗技硬件，靠假接收器 53 项端到端自测） |
| 罗技 HID++ 1.0 寄存器 | 老款 Unifying 设备 | 百分比 / 电压（看固件） | ⚠️ 兜底尝试 |
| 标准 HID 电量（Usage 0x06/0x20） | 少数按规范上报的外设 | 百分比 | ⚠️ 2.4G dongle 极少暴露 |
| Windows 蓝牙电量属性 | 已连接、系统能拿到电量的蓝牙设备（以 BLE 为主） | 百分比 | ✅ 本机实测可读到 BLE 设备 |
| Xbox / XInput 手柄 | Xbox One / Series、兼容手柄 | 4 档 → 估 5/25/50/100 | ✅ 已实现（本机无手柄，负向路径已验证） |
| 模拟数据 | 演示与自检 | — | ✅ |

**明确不支持**（已调研，不是“还没做”）：雷蛇、赛睿、海盗船、HyperX、VGN、达尔优等品牌 2.4G 设备的私有协议。它们只有厂商软件（Synapse / GG / iCUE / NGENUITY / VGN HUB）能读，没有公开文档；逐型号逆向会随固件更新失效。

> 换句话说：如果你用的是罗技（MX / G 系列）或蓝牙设备，大概率能直接看到电量；如果是雷蛇、VGN 这类，当前版本会显示“未发现支持的外设”。这是现实约束，不是程序坏了。
>
> 详细的支持矩阵与证据链接见 [docs/support-matrix.md](docs/support-matrix.md)，协议调研见 [docs/protocols.md](docs/protocols.md)。

## 快速开始

**免安装（推荐先试）**

1. 打开 `dist\app\`，双击 `PeripheralBattery.exe`；
2. 首次启动会显示引导，点“开始使用”；
3. 托盘出现电池图标后，左键打开悬浮面板，右键打开菜单。

**安装到本机**

```powershell
powershell -ExecutionPolicy Bypass -File installer/install.ps1
# 可选参数：-DesktopShortcut  -NoAutoStart  -InstallDir "D:\Tools\PeripheralBattery"
```

安装脚本会把程序复制到 `%LOCALAPPDATA%\Programs\PeripheralBattery`，创建开始菜单快捷方式，并写入当前用户的启动项（`HKCU\...\Run`，参数 `--tray`，静默驻留托盘）。
卸载：`powershell -ExecutionPolicy Bypass -File installer/uninstall.ps1`（加 `-Purge` 连配置一起删）。

> exe 未签名，首次运行 SmartScreen 可能提示“Windows 已保护你的电脑”，点“更多信息 → 仍要运行”即可；也可以先 `Unblock-File .\PeripheralBattery.exe`。

## 使用说明

- **左键 / 双击托盘图标**：打开面板；**右键**：打开面板、立即刷新、设置、开机启动（勾选状态读注册表真实值）、诊断信息、关于、退出。
- **设置项**：刷新间隔（5–300 秒）、托盘显示策略、低电量阈值、气泡提醒、开机启动、各数据源开关（改动需重启程序生效）、详细日志。
- **配置与日志**：`%APPDATA%\PeripheralBattery\config.json` / `app.log`；如果该目录不可写（受限环境或绿色运行），自动改用 exe 同级的 `data\` 目录。
- **命令行开关**（便于排障）：
  - `--tray` 静默启动到托盘
  - `--show` 启动并显示主面板
  - `--flyout` 启动并直接弹出悬浮面板
  - `--settings` 直接打开设置
  - `--diagnose` 显示诊断信息
  - `--demo` 强制打开演示数据
- **诊断工具**：`dist\app\BatteryProbe.exe`

  ```text
  BatteryProbe.exe --dump       # 列出全部 HID 接口与 usage（排查设备是否暴露电量）
  BatteryProbe.exe --scan       # 直接跑一遍各 Provider，打印结果表
  BatteryProbe.exe --selftest   # HID++ 编解码与假接收器端到端自测（53 项）
  BatteryProbe.exe --json       # 机器可读输出
  ```

## 常见问题

**Q：为什么我的雷蛇 / VGN / 赛睿鼠标没有电量？**
A：这些品牌走私有 2.4G 协议，Windows 和本工具都读不到；只有厂商软件知道。详见 [支持矩阵](docs/support-matrix.md)。

**Q：罗技鼠标显示“未知”或读不到？**
A：先确认接收器插好、设备已开机并连接；在 `BatteryProbe.exe --dump` 里应能看到 `VID_046D` 且 Usage Page 为 `0xFF00` 的厂商集合。部分设备（尤其被厂商软件独占的）需要关闭 G HUB / Options+ 再试。仍不行就复制诊断信息反馈。

**Q：蓝牙耳机/鼠标为什么不显示？**
A：只显示“系统已经拿到电量”的设备（Windows 设置 → 蓝牙和设备里能看到电量）。经典蓝牙（非 BLE）或厂商私有通道的耳机通常读不到。

**Q：需要管理员权限吗？**
A：不需要。只有个别被系统独占的 HID 接口可能打不开，程序会跳过并记录日志。

**Q：会上传数据吗？**
A：不会。全部在本机读取与显示，没有网络请求。

**Q：开机启动怎么关？**
A：托盘右键菜单取消“开机启动”，或设置里取消勾选；也可以删注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下的 `PeripheralBattery`。

**Q：为什么主面板显示“未发现支持的外设”，但演示模式有数据？**
A：演示模式是假数据，用于查看界面；真实设备需要匹配上面任一数据源。

## 构建

环境要求：Windows 10/11 x64 + .NET Framework 4.8（系统自带，无需安装 .NET SDK / NuGet / 第三方库）。

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1            # 编译主程序到 dist\app
powershell -ExecutionPolicy Bypass -File build.ps1 -Probe     # 同时生成 BatteryProbe.exe
powershell -ExecutionPolicy Bypass -File build.ps1 -Run       # 编译并启动
```

构建脚本会调用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，源码按 UTF-8（`/codepage:65001`）编译，内嵌 DPI 感知清单与图标。

## 目录结构

```text
src/
  Core/                契约、配置、日志、Provider 宿主
    Hid/               HID 枚举/通道、HID++ 编解码与会话
    Providers/         罗技、标准 HID、蓝牙、XInput、模拟
    Platform/          SetupAPI/XInput 互操作、开机启动
  App/                 WinForms 托盘、悬浮面板、主面板、设置、诊断
tools/
  Probe/               命令行诊断工具（BatteryProbe）
  qa/                  截图/DPI/窗口定位等验收脚本
docs/                  协议调研、支持矩阵、架构
installer/             安装/卸载脚本
dist/app/              构建产物（PeripheralBattery.exe、BatteryProbe.exe）
```

## 工作原理（简版）

1. `HidBus` 通过 SetupAPI 枚举 HID 顶层集合，读取 usage、报告长度、feature value caps（含 0x06/0x20 电量）；
2. 各 Provider 并行扫描：罗技走 HID++（0x10/0x11 报文，0x0000 查特性、0x0005 取名和类型、0x1004/0x1000/0x1001 取电量与电压，老设备回退 HID++ 1.0 寄存器 0x07/0x0D）；蓝牙读 PnP 属性 `{104EA319-6EE2-4701-BD47-8DDBF425BBE5},2`；手柄走 XInput；
3. `ProviderHost` 在后台线程聚合、去重（按品牌+名称+类型），UI 只负责展示；
4. 托盘线程每 30 秒刷新，HID 枚举有 2.5 秒进程内缓存，避免反复扫描 300+ 个 PnP 节点。

新增品牌支持请按 [docs/architecture.md](docs/architecture.md) 的六步实现一个 `IProvider`，并在 `ProviderRegistry` 注册。

## 已知限制

- 罗技链路未在真实罗技硬件上验证（开发机没有罗技接收器），目前由“假接收器端到端自测 + 协议单测”保证；首次接真机建议跑一次 `BatteryProbe.exe --scan --verbose`；
- 只有电压（0x1001）的设备，百分比是按 3.0V–4.2V 线性估算的，不同型号会有偏差，界面会标注“电压估算”；
- Xbox 手柄只有 4 档电量，无法给出精确百分比；
- 托盘图标/气泡/右键菜单是标准 `NotifyIcon` 实现，但开发环境的 shell 无法截图，建议安装后人工确认一次交互；
- exe 未做代码签名，SmartScreen 会提示；不支持 ARM64（仅 x64）；
- 暂未实现：电量历史曲线、多语言、自动更新、随系统主题切换。

## 致谢

协议实现参考了 Logitech HID++ 2.0 草案、[Solaar](https://github.com/pwr-Solaar/Solaar)、[OpenLogi](https://openlogi.org/hidpp)、[OpenRazer](https://github.com/openrazer/openrazer) 等公开资料，均为独立实现；详见 [docs/protocols.md](docs/protocols.md)。
