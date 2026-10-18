# 支持矩阵（Support Matrix）

> 这是本项目对外的**能力承诺**：只写能验证的结论，不写「应该可以」。
> 状态含义：✅ 已实现并验证 / ⚠️ 有条件支持（依赖设备或系统） / ❌ 不支持（已调研，明确不做）。

## 1. 数据源能力

| 数据源 | 覆盖范围 | 精度 | 依赖 | 实现类 |
| --- | --- | --- | --- | --- |
| Logitech HID++ 2.0 | 罗技办公系列、G 系列无线 | 百分比（0x1004/0x1000）或电压换算（0x1001） | 罗技 HID 设备可访问 | `LogitechHidppProvider` |
| Logitech HID++ 1.0 寄存器 | 老款罗技 Unifying 设备 | 百分比/电压（视固件） | 同上 | `LogitechHidppProvider`（兜底） |
| 标准 HID 电量 | 按 Usage 0x06/0x20 上报的设备 | 百分比 | 设备暴露 feature/input report | `StandardHidBatteryProvider` |
| Windows 蓝牙属性 | 已连接且系统能拿到电量的蓝牙设备（以 BLE 为主） | 百分比 | 蓝牙已配对并连接 | `BluetoothBatteryProvider` |
| XInput | Xbox 兼容手柄（USB / 无线适配器 / 蓝牙） | 4 档 → 估 5/25/50/100 | 系统有 xinput1_4/1_3.dll | `XInputBatteryProvider` |
| 通用外设识别 | 任意品牌的 2.4G / 无线 HID 键鼠、手柄（按名称含 2.4G/Wireless/Dongle/Receiver 识别） | 无（只显示“未知”） | 无需协议 | `GenericHidPeripheralProvider` |
| 模拟数据 | 演示/自检 | 任意 | 配置开启 | `SimulationProvider` |

> **通用识别（新增）**：即使品牌没有公开协议，设备也会以“鼠标/键盘/耳机/手柄”类别出现在列表里，电量显示“未知”；同一 `VID:PID` 的接收器会合并为一条记录，可以用“重命名”（≤20 字）自定义显示名，方便区分。它不改变“能不能读到电量”的结论。

## 2. 品牌 / 系列支持状态

| 品牌 | 系列/型号 | 连接方式 | 状态 | 说明 | 证据 |
| --- | --- | --- | --- | --- | --- |
| Logitech | MX Master 3/3S、MX Keys、MX Anywhere 等办公系列 | Unifying / Bolt / 蓝牙 | ✅ | HID++ 2.0：优先 `0x1004`，旧款 `0x1000` | [OpenLogi x1004](https://openlogi.org/hidpp/features/x1004-unified-battery)、[x1000](https://openlogi.org/hidpp/features/x1000-battery-status) |
| Logitech | G915、G903 LIGHTSPEED、G502 X LIGHTSPEED 等 G 系列无线 | Lightspeed 2.4G | ✅ | 只实现 `0x1001` 电压，需按电压换算百分比 | [OpenLogi x1001](https://openlogi.org/hidpp/features/x1001-battery-voltage) |
| Logitech | 老款 HID++ 1.0 Unifying 设备 | Unifying 2.4G | ⚠️ | 试寄存器 `0x07`/`0x0D`，能否读到取决于固件 | [Solaar hidpp10.py](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp10.py) |
| Microsoft | Xbox One / Series 手柄、XInput 兼容第三方手柄 | USB / Xbox 无线适配器 / 蓝牙 | ✅ | 仅 4 档；有线手柄无电量（`Percent = null`） | [XInput 文档](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_battery_information) |
| 任意（蓝牙） | BLE 鼠标/键盘/耳机/手机等 | 蓝牙 | ⚠️ | 系统蓝牙属性有值才显示；本机实测 BLE 设备（iPhone）可读 100% | [fastfetch 属性定义](https://github.com/fastfetch-cli/fastfetch/blob/dev/src/detection/bluetooth/bluetooth_windows.c) |
| 任意（标准 HID） | 按 HID Usage 0x06/0x20 上报的设备 | USB / 2.4G | ⚠️ | 覆盖率低；私有 dongle 基本不暴露该 usage | [HID Usage Tables](https://www.usb.org/sites/default/files/hut1_4.pdf) |
| Keychron | V6 Max 等 V Max 系列 | 蓝牙 | ⚠️ | 实测通过 GATT Battery Service 可被系统读到 | [实测博客](https://pmortensen.eu/world2/2024/11/04/the-battery-state-of-a-keychron-qmk-based-keyboard-can-be-displayed-in-the-operating-system/) |
| Keychron | K5 Pro 等同代型号 | 蓝牙 / 2.4G | ❌ | 同测试中未暴露电量服务；2.4G 模式无系统属性 | 同上 |
| Razer | 无线鼠标/键盘 | 2.4G HyperSpeed | ❌（仅识别） | 私有 HID 控制传输，需逐型号逆向；本项目不实现，仅按通用识别列出（电量未知） | [OpenRazer](https://github.com/openrazer/openrazer) |
| SteelSeries | 无线耳机/鼠标 | 2.4G | ❌（仅识别） | 仅官方 GG 暴露电量；通用识别可列出设备（电量未知） | [SteelSeries GG](https://steelseries.com/gg) |
| Corsair | 无线耳机/鼠标 | 2.4G | ❌（仅识别） | 仅官方 iCUE，协议闭源；通用识别可列出设备（电量未知） | [iCUE](https://www.corsair.com/us/en/s/icue) |
| HyperX | Cloud 系列无线耳机等 | 2.4G | ❌（仅识别） | 仅官方 NGENUITY；通用识别可列出设备（电量未知） | [NGENUITY](https://hyperx.com/pages/ngenuity) |
| VGN | 蜻蜓 F1 系列鼠标（3554:F503 接收器） | 2.4G | ✅（实验） | 已逆向 VHUB WebHID 协议：output report 8、PCDriverStatus 握手、电池命令 0x04；本机实测 90% / 4059mV，设备休眠时显示未知 | 本项目实测 |
| VGN | V98Pro V2 键盘（320F:5088 接收器） | 2.4G | ✅（实验） | COL04（FF1C:0092）/ report 4，命令 0x1A；必须走中断 OUT 端点（WriteFile），控制端点 SetOutputReport 无响应。USBPcap 抓包确认，实测 68% | 本项目实测 |
| Apple / Sony / Bose / Jabra 等 | BLE 耳机、鼠标、键盘 | 蓝牙 | ⚠️ | 若厂商实现 GATT Battery Service（0x2A19）则可能被系统读到；否则读不到 | [Keychron 实测（说明机制）](https://pmortensen.eu/world2/2024/11/04/the-battery-state-of-a-keychron-qmk-based-keyboard-can-be-displayed-in-the-operating-system/) |

## 3. 明确不做的事

| 事项 | 原因 |
| --- | --- |
| 读取 Razer / SteelSeries / Corsair / HyperX / VGN 的私有协议 | 无公开文档，需逐型号逆向，固件更新即失效；见 `docs/protocols.md` 第 5、6 节 |
| 复用/注入厂商软件（Synapse、iCUE、GG、NGENUITY、VGN HUB） | 不稳定、涉及许可与安全风险，不能作为正式数据源 |
| 伪造精确百分比 | XInput 只有 4 档，界面必须显示「档位估算」 |
| 自己实现 BLE GATT 客户端读电量 | 与系统蓝牙栈重复，且需要配对权限与额外兼容性工作；改为读系统属性 |
| 安装驱动 / 内核组件 | 本项目坚持「绿色、免管理员、无驱动」 |

## 4. 怎么确认自己的设备能不能读

1. 打开程序 → 设置里确认对应数据源已开启（蓝牙 / Xbox 手柄 / 罗技 / 标准 HID）；
2. 看主面板是否出现设备，以及卡片上的 `Detail`：
   * 蓝牙：`系统蓝牙电量属性（GUID 104EA319…, PID 2）`；
   * 手柄：`档位估算（XInput 仅提供 4 档…）`；
3. 若蓝牙设备不出现：先在 Windows 设置里确认「已连接」（不是仅配对），再看 `%APPDATA%\PeripheralBattery\app.log`；
4. 开发期可用 `tools/Probe`（HID 组提供）或按 `docs/architecture.md` 第 6 节用临时控制台直接调用 Provider 的 `Scan()`。

## 5. 维护规则

* 新增一个品牌/系列的支持，**必须**同时更新本文件与 `docs/protocols.md` 的来源；
* 状态只能从 ⚠️/❌ 升级为 ✅ 的前提：有可复现的实测数据（设备型号 + 读数 + 日志）；
* 任何「厂商软件才能读」的结论保持不变，直到出现可独立验证的公开协议实现。