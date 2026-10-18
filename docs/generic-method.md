# 2.4G 无线外设电量检测通用方法

> 本文是项目里"怎么判断任意一台 2.4G 外设能不能读电量、以及怎么读"的总方法论。
> 代码对应：`src/Core/Providers/VendorBatteryProtocols.cs`（协议表）+
> `VendorHidBatteryProvider.cs`（通用执行器）+ 各标准 Provider。
>
> 结论先说：**2.4G 没有统一电量标准**，所以"通用"不是一条命令通吃，而是一套
> **分层探测 + 数据驱动协议表** 的流程；新增品牌是填表，不是改架构。

---

## 0. 分层模型：先走通用层，再走厂商层

| 层 | 数据源 | 覆盖 | 代价 |
| --- | --- | --- | --- |
| L1 标准 HID | Feature/Input Usage `0x06:0x20`（Battery Strength） | 少数规范设备 | 零协议知识 |
| L2 操作系统 | Windows 蓝牙电量属性 `{104EA319-6EE2-4701-BD47-8DDBF425BBE5},2` | BLE 设备 | 读系统属性 |
| L2 操作系统 | XInput `XInputGetBatteryInformation` | Xbox 手柄 | 4 档 |
| L3 厂商私有 | HID 控制集合 + 厂商命令 | 罗技 HID++、VGN 等 | 需要抓包/协议表 |
| L4 兜底 | 通用识别（只列设备、不读电量） | 任意 2.4G 键鼠 | 无需协议 |

程序按 L1 → L2 → L3 → L4 顺序执行；L3 命中后，L4 的重复条目会被去重（同 VID:PID）。

---

## 1. 六步通用检测流程

### Step 1：枚举物理设备并分组
- SetupAPI 枚举 `GUID_DEVINTERFACE_HID`，拿到每个顶层集合的：
  VID/PID、UsagePage/Usage、Input/Output/Feature 报告长度、产品名、父设备、设备路径。
- 按 **VID:PID** 分组（一个接收器通常对应一个物理设备，多个 collection 属于同一台）。
- 注意：键鼠主集合常被 `kbdhid`/`mouhid` 独占，打不开也**不能丢**——
  用注册表信息保留设备，否则会"看不见"。

### Step 2：标准电量探测
- 在 FeatureUsages 里找 `UsagePage=0x06, Usage=0x20`；
- 读 feature report，按 `LogicalMin/Max` 换算百分比；
- 命中即可直接出电量，**不需要任何厂商协议**。

### Step 3：寻找厂商控制通道
在同一个物理设备的分组里，找"像控制通道"的集合：

```text
UsagePage >= 0xFF00（厂商自定义）
且 OutputReportLen 或 FeatureReportLen > 0
优先：同一集合同时有 input + output report
常见形态：64 字节 in/out 的 vendor collection
```

记录该集合的 **report ID**（通过 `HidP_GetValueCaps`/`HidP_GetButtonCaps` 读出来，
不要靠猜；Windows 的 `OutputReportByteLength` 是集合内最大值，不等于某个命令的长度）。

### Step 4：确定传输方式（最容易踩坑的一步）
同一条命令，可能只认其中一种端点：

| 方式 | API | 现象 |
| --- | --- | --- |
| 控制端点 | `HidD_SetOutputReport` | 返回 True，但设备不回应（VGN 键盘） |
| 中断 OUT | `WriteFile`（64 字节整包） | 设备才回应（VGN 键盘） |
| Feature | `HidD_SetFeature`/`GetFeature` | 部分设备用 feature 传命令 |

**判定方法**：同一条命令分别用两种方式发一次，看哪个能收到回包；
两种都试、都超时就说明命令/帧格式不对，不是端点问题。
（本项目 `IHidChannel` 同时提供 `Write` 和 `WriteInterrupt`。）

### Step 5：还原帧格式与命令
从抓包或厂商软件中提取：

- **报告 ID**（首字节）；
- **命令字节**与参数布局（例如 VGN 鼠标 `[cmd,0,0,0,val...]`，键盘 `[0,0,0x1A,6,...]`）；
- **校验**：有的设备要 CRC/反码（VGN 鼠标：15 字节求和 → `85-sum` → 再减 reportId）；
- **握手**：有的设备必须先发"驱动在线/版本查询"（VGN 鼠标先发 `PCDriverStatus(2)+1`）；
- **长度**：整包补齐到集合的 output report 长度。

### Step 6：解析响应 + 校验
不要假设固定偏移，至少做三层校验：

1. **回显匹配**：响应里的命令字节/报告 ID 与请求一致；
2. **范围校验**：电量 0–100（电压 2500–4500 mV）；
3. **充电位**只认明确的 0/1，其它值当未知。

校验通过才产出 `Peripheral`；否则宁可显示"未知"，也不显示假数据。

---

## 2. 决策树（程序实际执行顺序）

```text
for 每个物理设备（VID:PID）:
    if 标准 HID 0x06:0x20 有值            -> 直接用电量
    else if 蓝牙属性有值                  -> 直接用电量
    else if XInput 手柄                   -> 档位估算
    else if 命中协议表（L3）              -> 按表里的 transport/命令/解析读取
    else                                  -> 通用识别：列出设备，电量显示"未知"
```

---

## 3. 两个真实踩坑（VGN，本项目实测）

| 坑 | 错误做法 | 正确做法 |
| --- | --- | --- |
| 发错 collection | 键盘命令发到 COL05（FFEF）/ report 5 | VHUB 实际用 **COL04（FF1C:0092）/ report 4** |
| 发错端点 | 用 `HidD_SetOutputReport`（控制端点）发键盘命令 | 必须走 **中断 OUT（WriteFile）**；控制端点设备不回 |
| 命令值靠猜 | 以为键盘电池命令是 0x05 | USBPcap 抓包确认是 **0x1A**，电量在响应第 8 字节 |

抓包后的原始帧（USBPcap，设备地址 7，endpoint 0x05/0x82）：

```text
OUT 04 00 00 1A 06 00 00 00 00 ...   (64B)
IN  04 00 00 1A 06 00 00 00 47 00 ... (0x47 = 71%)
```

---

## 4. 新增一个品牌/型号：五步

1. **抓包**：USBPcap + Wireshark，按设备地址过滤，导出 OUT/IN 的 HID Data；
2. **找请求/响应**：找到回显命令字节，确认电量字段与充电位；
3. **填表**：在 `VendorProtocolRegistry.All` 里加一条 `VendorBatteryProtocol`：
   `Vendor/Model/Kind/Vid/Pid/UsagePage/Usage/MinReportLength/Transport/PreCommands/Command/Parse`；
4. **验证**：`BatteryProbe.exe --vendor` 看通道匹配与读数，再跑主程序；
5. **登记**：更新 `docs/support-matrix.md` 与 README 的支持范围。

> 不需要新写 Provider、不需要改 UI：表驱动，UI 自动按类别/品牌展示。

---

## 5. 当前协议表

| Key | 品牌/型号 | VID:PID | 控制集合 | report ID | 传输 | 命令 | 解析 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| vgn-f1-mouse | VGN 蜻蜓 F1 系鼠标 | 3554:F503 | FF02:0002 | 8 | 控制端点 | 握手 2/3 + 电量 4 | level=r[6], chg=r[7], mV=r[8..9] |
| vgn-v98pro | VGN V98Pro V2 键盘 | 320F:5088 | FF1C:0092 | 4 | 中断 OUT | 0x1A | level=r[8], chg=r[9] |

---

## 6. 鲁棒性与安全

- **只发只读查询命令**，不写配置、不升级固件；
- 单设备超时 1.2s，失败只跳过，不影响其它 Provider；
- 不猜数据：校验不过返回"未知"，绝不用 0 或默认值充数；
- 不需要管理员、不装驱动、不联网；
- 设备休眠时可能短暂读不到，下一次 30s 轮询会恢复。

---

## 7. 代码地图

| 文件 | 职责 |
| --- | --- |
| `src/Core/Hid/HidBus.cs` | 枚举/缓存/独占容错、父设备 |
| `src/Core/Hid/HidChannel.cs` | `Write`（控制端点）/ `WriteInterrupt`（中断 OUT）/ `Read` |
| `src/Core/Providers/VendorBatteryProtocols.cs` | 协议表（数据驱动） |
| `src/Core/Providers/VendorHidBatteryProvider.cs` | 通用执行器（匹配→握手→查询→解析） |
| `src/Core/Providers/StandardHidBatteryProvider.cs` | L1 标准 HID 电量 |
| `src/Core/Providers/BluetoothBatteryProvider.cs` / `XInputBatteryProvider.cs` | L2 系统数据源 |
| `src/Core/Providers/GenericHidPeripheralProvider.cs` | L4 仅识别兜底 |
| `tools/Probe` | `--vendor` 通道侦察 / `--scan` / `--selftest` |
