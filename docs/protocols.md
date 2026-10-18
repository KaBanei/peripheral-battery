# 2.4G / 蓝牙外设电量协议调研

> 目标：说明本项目每条数据源**实际能读到什么、怎么读、为什么读不到**，并给出可核查来源。
> 调研日期：2026-10-18。文中「实测」均指在本机（Windows，x64）用本项目代码的真实运行结果。

## 0. 结论速览

| 数据源 | 覆盖设备 | 精度 | 本项目实现 |
| --- | --- | --- | --- |
| Logitech HID++ 2.0 / 1.0 | 罗技办公系列（MX 等）、G 系列 LIGHTSPEED | 精确百分比 / 电压 | `LogitechHidppProvider`（HID 组） |
| 标准 HID 电量（Usage 0x06/0x20） | 少数按规范上报的外设 | 百分比 | `StandardHidBatteryProvider`（HID 组） |
| Windows 蓝牙电量属性 | 已配对、已连接、且系统能拿到电量的蓝牙设备（以 BLE 为主） | 百分比 | `BluetoothBatteryProvider`（本文） |
| XInput | Xbox 手柄（有线/无线适配器/蓝牙） | 仅 4 档（估 5/25/50/100） | `XInputBatteryProvider`（本文） |

**一句话结论**：2.4G 外设不存在统一的「电量读取标准」。罗技有相对完整的公开/半公开协议（HID++），Xbox 手柄有系统 API（XInput），蓝牙设备依赖系统蓝牙栈的属性；其余品牌绝大多数是私有协议，只能靠厂商驱动。详见第 6 节。

---

## 1. Logitech HID++ 协议

### 1.1 传输层：两种报告长度

HID++ 走 HID 的 output/input report（Windows 上就是 `WriteFile` / `ReadFile`，罗技设备通常是中断端点）。报告 ID 只有两个：

* **0x10**：短报告，总长 **7 字节**（报告 ID + 6 字节负载）。
* **0x11**：长报告，总长 **20 字节**（报告 ID + 19 字节负载）。

请求帧布局（来自 Logitech HID++ 2.0 草案规范）：

```
短报告 0x10:
字节:   0     1            2             3                 4   5   6
内容: 0x10  设备索引     功能索引     高4位=功能号       参数(3 字节)
         (1..255)    (1..254)     低4位=软件ID(1..15)

长报告 0x11:
字节:   0     1            2             3                 4 ................ 19
内容: 0x11  设备索引     功能索引     高4位=功能号       参数(16 字节)
         (1..255)    (1..254)     低4位=软件ID(1..15)
```

响应帧与请求同构，只是把「参数」换成「结果」，且设备索引/功能索引/功能号/软件 ID **原样回显**。规范同时规定：除特别说明外协议使用**大端序**（16 位值高字节在前），见草案规范 *Protocol HID++2.0 essential features* 一节。

来源：
* Logitech HID++ 2.0 Draft Specification（2012-06-04，Logitech 官方草案，可公开访问）：https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf
* OpenLogi HID++ 参考（对规范的可读整理）：https://openlogi.org/hidpp

### 1.2 设备索引与接收器（Unifying / Bolt / Lightspeed）

* 每个 HID++ 帧第 1 字节是**设备索引**：接收器本身固定为 **0xFF**；接收器下挂的配对设备占用 **1..6** 的槽位（即配对槽位号）。
* **Unifying 接收器最多带 6 台设备**（Logitech 官方支持文档原文：*Your Unifying USB receiver can be paired with up to six Unifying devices at a time*）。
* **Logi Bolt 接收器同样最多 6 台**，但它是 BLE 架构，配对需要 passkey 认证；OpenLogi 文档称 Bolt 是其接收器实现中最完整的一类。
* **Lightspeed 接收器**（G 系列游戏外设）复用同一套 HID++ 1.0 寄存器，因此按 Unifying 的枚举/路由路径处理，只是显示名不同；常见套装是「一台接收器对应一台设备」。

接收器 USB PID（`VID_046D` 下）：

| 类型 | PID | 说明 |
| --- | --- | --- |
| Unifying | `C52B`、`C532`、`C537`、`C539` | 经典 Unifying / DJ 接收器；`C539` 是 G 系列 Lightspeed 接收器，但按 Unifying 路由 |
| Logi Bolt | `C548` | BLE based，passkey 配对，最多 6 台 |
| Lightspeed | `C53F`、`C547` | 与 Unifying 寄存器相同；`C53F`（G305 nano）、`C547`（G915 / G502 X LIGHTSPEED） |

来源：
* OpenLogi *Receivers*（PID 表、0xFF 索引、Bolt 6 台、Lightspeed 路由方式）：https://openlogi.org/hidpp/receivers
* Logitech 官方支持：Unifying 接收器可配对最多 6 台设备：https://support.logi.com/hc/en-us/articles/5470036605975-Unifying-Receiver-Software-Pairing-and-Troubleshooting

### 1.3 常用功能（Feature）

HID++ 2.0 的能力按 16 位 **Feature ID** 组织。访问任何功能前，必须先通过 `0x0000 root` 的 `getFeature(featureId)` 换取该功能在当前会话里的 **feature index（1 字节）**，之后所有请求的第 2 字节都填这个 index。

| Feature | 名称 | 关键函数 | 返回内容 |
| --- | --- | --- | --- |
| `0x0000` | root | fn0 `getFeature(id: u16)`；fn1 `ping(data)` | 功能索引（u8）、类型标志、版本；root 自身永远在索引 0 |
| `0x0005` | deviceTypeAndName | fn0 名字长度、fn1 按偏移取名字、fn2 设备类型 | 设备类型枚举：Keyboard=0、RemoteControl=1、Numpad=2、Mouse=3、Trackpad=4、Trackball=5、Presenter=6、Receiver=7、Headset=8…Gamepad=12 |
| `0x1000` | batteryStatus（旧） | fn0 `getBatteryLevelStatus` | 电量百分比（0=未知）、下一档百分比、充电状态（0 放电/1 充电中/2 快满/3 满/4 慢充/5 电池无效/6 温度异常） |
| `0x1001` | batteryVoltage | fn0 `getBatteryInfo` | 大端 u16 毫伏 + 1 字节充电标志（bit5=电量严重不足）；**不含百分比** |
| `0x1004` | unifiedBattery（新） | fn0 `getBatteryCapabilities`；fn1 `getBatteryInfo` | fn1 返回百分比 u8、档位（Critical=1/Low=2/Good=4/Full=8）、充电状态（0 放电/1 充电/2 慢充/3 满/4 错误）；并支持设备主动推送事件 |

实际探测顺序建议：**`0x1004` → `0x1000` → `0x1001` →（HID++ 1.0 寄存器 0x07/0x0D 兜底）**。原因：
* 新设备（MX Master 3S 等）只有 `0x1004`；
* 老设备（MX Master 2S）只有 `0x1000`，从不实现 `0x1004`；
* G 系列无线（G915、G903 LIGHTSPEED、G502 LIGHTSPEED）**只实现 `0x1001`**，既不实现 `0x1000` 也不实现 `0x1004`——不读电压就完全拿不到电量。

注意 `0x1001` 的线格式**不在公开规范里**（OpenLogi 明确标注为逆向所得，对齐 Solaar 的 `decipher_battery_voltage` 与 libratbag 的共识）。单节锂聚合物大致 3500 mV（空）～4200 mV（满），需要换算。

来源：
* `0x0000`：https://openlogi.org/hidpp/features/x0000-root
* `0x0005`（类型枚举、名字分块读取）：https://openlogi.org/hidpp/features/x0005-device-type-and-name
* `0x1000`（老设备、状态枚举）：https://openlogi.org/hidpp/features/x1000-battery-status
* `0x1001`（G 系列只有电压、逆向说明）：https://openlogi.org/hidpp/features/x1001-battery-voltage
* `0x1004`（新统一电量、档位/状态枚举）：https://openlogi.org/hidpp/features/x1004-unified-battery
* 草案规范中 `0x0000`/`0x0005`/`0x1000` 的原始描述：https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf

### 1.4 HID++ 1.0 寄存器 0x07 / 0x0D（老设备兜底）

HID++ 1.0 没有 feature 概念，用**编号寄存器**。Solaar 的实现（`lib/logitech_receiver/hidpp10.py`）显示：
* 读寄存器：`request_id = 0x8100 | register`；写寄存器：`0x8000 | register`（注释：*support long registers by adding a 2 in front of the register number*）。
* 电池相关寄存器在 `hidpp10_constants.py` 的 `Registers` 里定义：**`BATTERY_STATUS = 0x07`**、**`BATTERY_CHARGE = 0x0D`**。
* `get_battery()` 的逻辑：HID++ 2.0 设备直接跳过（2.0 设备电量走 feature）；否则先试设备声明拥有的寄存器，再依次试 `0x0D`、`0x07`，成功后缓存。

实践含义：**0x07 返回电量状态（百分比 + 充电标志）**，**0x0D 返回电池充电信息（老设备上常是电压）**；两者具体字节含义随固件有差异，建议像 Solaar 一样「能读到就用、读不到就换下一个」，不要硬编码单一寄存器。

来源：
* Solaar 仓库：https://github.com/pwr-Solaar/Solaar
* `hidpp10.py`（read_register / write_register / get_battery）：https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp10.py
* `hidpp10_constants.py`（`BATTERY_STATUS = 0x07`、`BATTERY_CHARGE = 0x0D`）：https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp10_constants.py

---

## 2. 标准 HID 电量（Usage Page 0x06 / Usage 0x20）

USB HID Usage Tables 为「通用设备控制页（Generic Device Controls Page，0x06）」定义了 **Usage 0x20 = Battery Strength**（Data/Variable，DV）。它在 HID 报告描述符里就是一个普通的数值字段，设备可以用 **input report** 周期上报，也可以放在 **feature report** 里供主机查询。

读取方式（本项目 HID 组实现）：
1. 解析 HID 报告描述符，找 `Usage Page 0x06` + `Usage 0x20` 所在的 report；
2. 记下 report ID、逻辑最小值/最大值（通常 0..100）；
3. 用 `HidD_GetFeature`（feature report）或读取 input report 取值；
4. 按逻辑范围换算成百分比。

**现实覆盖率很低**。原因：
* 2.4G 私有 dongle（罗技、雷蛇、VGN 等）多用厂商私有通道，根本不暴露 0x06/0x20；
* 厂商更愿意在自家软件里展示电量，或走私有 HID++/控制传输；
* 蓝牙设备走的是 GATT Battery Service，而不是这条 HID usage。

蓝牙侧的实际机制是 **GATT Battery Service（UUID 0x180F）的 Battery Level 特征（0x2A19）**。有实测博客记录 Keychron V6 Max 在 `bluetoothctl info` 中输出 `UUID: Battery Service (0000180f-...)` 和 `Battery Percentage: 0x56 (86)`；而同系列 K5 Pro 不暴露该服务，就读不到。Windows 11 的「设置 → 蓝牙和设备」里显示的电量也来自设备上报（GATT 或厂商通道），**系统不会凭空推断**。

来源：
* HID Usage Tables（USB-IF 官方，Generic Device Controls 页在 0x06，Battery Strength 为 0x20）：https://www.usb.org/sites/default/files/hut1_4.pdf
* 该页 usage 的中文对照表（含 `20 Battery Strength DV`）：https://www.usbzh.com/article/detail-975.html
* Keychron V6 Max 的 GATT Battery Service 实测：https://pmortensen.eu/world2/2024/11/04/the-battery-state-of-a-keychron-qmk-based-keyboard-can-be-displayed-in-the-operating-system/

---

## 3. Windows 蓝牙电量属性（本项目 `BluetoothBatteryProvider` 的数据源）

### 3.1 属性键

Windows 蓝牙栈把「设备电量」写成该 PnP 设备节点上的一个**设备属性**：

```
GUID : {104EA319-6EE2-4701-BD47-8DDBF425BBE5}
PID  : 2
名称 : DEVPKEY_Bluetooth_BatteryLevel
类型 : DEVPROP_TYPE_UINT8（字节）
取值 : 0..100
```

该定义可见于 fastfetch 的 Windows 蓝牙检测源码（`DEFINE_DEVPROPKEY(DEVPKEY_Bluetooth_BatteryLevel, 0x104ea319, 0x6ee2, 0x4701, 0xbd, 0x47, 0x8d, 0xdb, 0xf4, 0x25, 0xbb, 0xe5, 2)`，注释 `DEVPROP_TYPE_UINT8`）。同一文件还定义了 `DEVPKEY_Bluetooth_DeviceAddress`(pid 1)、`DEVPKEY_Bluetooth_ServiceGUID`(pid 2)、`ClassOfDevice`(pid 10)、`LastConnectedTime`(pid 11) 等同族键。

> 社区常说的「PID 4 备用键」没有出现在该头文件里，属于未公开行为；本项目把它作为**兜底试探**（PID 2 读不到时再试 4），不做任何保证。

### 3.2 读取方式（SetupAPI）

1. `SetupDiGetClassDevs(NULL, NULL, NULL, DIGCF_PRESENT | DIGCF_ALLCLASSES)` 拿到全部**在场**设备节点；
2. `SetupDiEnumDeviceInfo` 逐个枚举，`SetupDiGetDeviceInstanceId` 取实例 ID；
3. 只保留 InstanceId 以 `BTHLE\` 或 `BTHENUM\` 开头的节点（已配对蓝牙设备）；跳过 `BTHENUM\DEV_...`（本机适配器枚举出的本地服务对象，不是远端外设）；
4. `SetupDiGetDeviceProperty(&DEVPKEY_Bluetooth_BatteryLevel)` 读电量；读不到再试 PID 4；
5. 用 `DEVPKEY_Device_FriendlyName`（{A45C254E-DF1C-4EFD-8020-67D146A850E0},14）取名，缺失时退回 `DEVPKEY_Device_DeviceDesc` / `BusReportedDeviceDesc`。

**坑（本项目实际踩到）**：`SetupDiGetDeviceProperty` 在传入空缓冲区探测所需长度时，返回的是 `FALSE` + `ERROR_INSUFFICIENT_BUFFER(122)`，同时把所需字节数写进 `RequiredSize`。如果把 `FALSE` 直接当成「属性不存在」，就会**永远读不到名字和电量**。正确做法：视为「FALSE 且 last error == 122」时继续，分配缓冲后再调用一次。

### 3.3 限制（为什么经常读不到）

* 属性由蓝牙栈在**设备连接并且拿到电量后**写入；未连接、刚配对、驱动未上报时为空。
* 以 BLE/GATT Battery Service 为主。经典 BR/EDR 设备即使支持 HFP 电量指示，也未必写进该属性；fastfetch 源码注释也指出 Windows 把经典（BR/EDR）与低功耗（LE）栈分开处理。
* 厂商私有的 2.4G dongle 完全不经过蓝牙栈，永远不会出现在这里。
* **本机实测**：全部 324 个 PnP 节点中 `BTH*` 39 个，只有 1 个带该属性——`BTHLE\DEV_51C16CD6C239`（名称「卡巴内的 iPhone」），值 100。`BTHENUM` 下的 AirPods/iPhone 服务节点、蓝牙适配器节点均为空。这解释了为什么该类 Provider 必须容忍「0 结果」。

来源：
* fastfetch `bluetooth_windows.c`（属性键定义、BR/EDR 与 LE 栈分离的说明）：https://github.com/fastfetch-cli/fastfetch/blob/dev/src/detection/bluetooth/bluetooth_windows.c
* Stack Overflow：用 PowerShell/C# 读蓝牙设备电量（同一 GUID 的社区用法）：https://stackoverflow.com/questions/71736070/how-can-i-get-the-bluetooth-device-battery-percentage-using-powershell-on-windows
* Microsoft Q&A：系统托盘/设置里的蓝牙电量显示（说明系统行为，非公开 API 文档）：https://learn.microsoft.com/en-us/answers/questions/1156194/bluetooth-device-battery-indicator-display-in-syst

---

## 4. XInput（Xbox 手柄）

* 动态加载 `xinput1_4.dll`（Windows 8+），失败回退 `xinput1_3.dll`（Windows 7）。两者都导出 `XInputGetState` 与 `XInputGetBatteryInformation`。
* `XInputGetState(i, out state)` 遍历 `i = 0..3`，返回 `ERROR_SUCCESS(0)` 表示该槽位有手柄；`ERROR_DEVICE_NOT_CONNECTED(1167)` 表示没有。
* `XInputGetBatteryInformation(dwUserIndex, devType, out XINPUT_BATTERY_INFORMATION)`：`devType` **必须是 `BATTERY_DEVTYPE_GAMEPAD(0)` 或 `BATTERY_DEVTYPE_HEADSET(1)`**（官方文档原文）。
* 结构体只有 2 字节：
  * `BatteryType`：0 未连接、1 有线、2 碱性电池、3 镍氢电池、0xFF 未知（`xinput.h` 只定义到 0x03 和 0xFF；网上流传的「4=锂电池」不在官方头文件里，本项目仅在第三方固件上报 4 时按锂电池显示）。
  * `BatteryLevel`：0 空、1 低、2 中、3 满。官方文档特别注明：**只有无线且电池类型已知时该值才有效**。
* **没有百分比、没有电压**。本项目把 0/1/2/3 映射为 5/25/50/100，并在 `Detail` 里写明「档位估算（XInput 仅提供 4 档，非真实百分比）」；有线手柄不显示百分比（`Percent = null`，`State = Wired`，`Connection = USB`）。

来源：
* `XInputGetBatteryInformation` 函数：https://learn.microsoft.com/en-us/windows/win32/api/xinput/nf-xinput-xinputgetbatteryinformation
* `XINPUT_BATTERY_INFORMATION` 结构体（BatteryType / BatteryLevel 取值）：https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_battery_information

---

## 5. 各品牌可获取性逐项调研

> 判定标准：**可直读** = 有公开/半公开协议或系统 API，第三方程序能独立读到电量；**需官方软件** = 只能通过厂商闭源驱动/服务读取；**不可行** = 无公开协议且无已知可靠逆向成果。

### 5.1 Logitech（MX/办公系列、G 系列 LIGHTSPEED）—— 可直读
* 办公系列（MX Master/MX Keys 等）使用 HID++ 2.0：`0x1004`（新）或 `0x1000`（旧）给百分比；
* G 系列无线（G915/G903 LIGHTSPEED/G502 LIGHTSPEED）走 HID++ `0x1001` 电压 + 换算；
* 接收器统一按 `0x046D:C52B/C532/C537/C539/C548/C53F/C547` 识别；
* 项目内由 `LogitechHidppProvider` 实现（HID 组）。

### 5.2 Razer —— 理论可直读，成本高，本项目未实现
* 官方只通过闭源 Synapse 暴露电量，没有公开协议文档；
* 社区项目 **OpenRazer** 通过逆向 USB HID 控制传输，实现了灯光、DPI、宏等大量设备功能，说明「协议可逆」——但需要逐设备维护命令表（电量支持视具体型号），且固件更新可能失效；
* 结论：在 Windows 上要自己实现 USB 控制传输 + 逐型号命令，工作量与维护成本都远超本项目范围。
* 来源：https://github.com/openrazer/openrazer

### 5.3 SteelSeries —— 需官方软件
* 没有公开的电量协议文档；官方功能集中在 **SteelSeries GG**（Engine 的继任者），电量/续航只在 GG 中展示；
* 结论：不可行（除非逆向）。
* 来源：https://steelseries.com/gg

### 5.4 Corsair —— 需官方软件
* 官方统一由 **iCUE** 管理，协议闭源；社区没有成熟的通用电量读取方案；
* 结论：不可行。
* 来源：https://www.corsair.com/us/en/s/icue

### 5.5 HyperX —— 需官方软件
* 官方软件为 **NGENUITY**，电量在软件内展示；无公开协议；
* 结论：不可行。
* 来源：https://hyperx.com/pages/ngenuity

### 5.6 Keychron —— 视型号：部分可直读
* 无线 QMK 键盘中，**V Max 系列**（如 V6 Max）通过 BLE 暴露 GATT Battery Service（0x180F），系统可直接显示百分比（实测 86%）；**K Pro 系列**（如 K5 Pro）在该测试中不暴露，读不到；
* 因此能否读到取决于具体型号与固件，属于「蓝牙 Provider 可能覆盖」的范围，不能保证；
* 结论：部分可读（走系统蓝牙属性），不支持的在 2.4G 模式下不可读。
* 来源：https://pmortensen.eu/world2/2024/11/04/the-battery-state-of-a-keychron-qmk-based-keyboard-can-be-displayed-in-the-operating-system/

### 5.7 VGN（国产）—— 不可行
* 官方只提供 **VGN HUB** 驱动/软件，无公开协议；第三方无法可靠读取；
* 结论：不可行。
* 来源：https://www.vgnlab.com.cn/

### 5.8 其他 BLE 音频/办公设备（Apple、Sony、Bose、Jabra、小米等）—— 视系统是否拿到 GATT 电量
* 这类设备多数实现 GATT Battery Service；Windows 若已读到，就会写入第 3 节的蓝牙属性，本项目即可显示；
* 若厂商把电量放在私有 GATT 特征（非 0x2A19）或私有通道，则读不到；
* 结论：由 `BluetoothBatteryProvider` 尽力覆盖，不承诺。

### 5.9 Microsoft Xbox 手柄 —— 可直读（XInput）
* 通过 `XInputGetBatteryInformation` 拿 4 档电量；
* 项目内由 `XInputBatteryProvider` 实现。

### 5.10 标准 HID 电量设备 —— 可直读（少数）
* 按 Usage 0x06/0x20 上报的设备，由 `StandardHidBatteryProvider` 读取；
* 2.4G 私有 dongle 基本不暴露该 usage。

---

## 6. 为什么「全品牌支持」在现实中不可行

1. **没有统一标准**。2.4G 私有 dongle 不受 USB HID 标准约束，厂商各写一套私有通道；即使同为 HID 设备，电量也可能藏在 vendor-defined usage page（0xFF00 段）里，没有公开描述符含义。
2. **协议闭源且逐型号**。Razer、SteelSeries、Corsair、HyperX、VGN 都没有公开电量协议；逆向成果（如 OpenRazer）需要逐设备维护命令表，固件一升级就可能失效。
3. **系统层面只覆盖蓝牙与 XInput**。Windows 能给的只有：蓝牙 PnP 电量属性（依赖设备上报 GATT/厂商通道）与 XInput 的 4 档电量；其余全靠应用自己实现。
4. **厂商软件不能被合法/稳定地复用**。读取厂商驱动的共享内存、注入进程、模拟其私有服务，既不稳定也涉及许可与安全问题，不适合作为正式产品的数据源。
5. **维护成本不对称**。每增加一个品牌，就要一套设备识别 + 协议实现 + 回归测试 + 固件兼容性跟踪；而收益只是该品牌的少量型号。

**本项目的策略**：不承诺「全品牌」，只承诺「可验证的数据源」，并把扩展做成插件：

* 统一接口 `IProvider`（`Key` / `DisplayName` / `Enabled` / `Scan` / `Dispose`），宿主 `ProviderHost` 负责调度、计时、异常隔离、去重合并；
* `ProviderRegistry` 用**反射**按类型名创建 Provider，构造签名固定为 `(AppConfig, IAppContext)`，因此新增品牌**不需要改 UI**；
* 每个品牌一个 Provider + 一个 `appsettings`/config 开关（`EnableLogitech` / `EnableStandardHid` / `EnableBluetooth` / `EnableXInput`），单个 Provider 崩溃不影响其他 Provider；
* `docs/support-matrix.md` 维护「品牌 → 系列 → 连接方式 → 支持状态」的公开承诺，`tools/Probe` 提供现场诊断；
* 新增步骤见 `docs/architecture.md`。

---

## 7. 参考来源汇总

| # | 来源 | 用途 |
| --- | --- | --- |
| 1 | https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf | HID++ 2.0 报告布局、0x0000/0x0005/0x1000 |
| 2 | https://openlogi.org/hidpp | HID++ 1.0/2.0 概览、feature discovery |
| 3 | https://openlogi.org/hidpp/receivers | 接收器 PID、0xFF 索引、Bolt 6 台、Lightspeed |
| 4 | https://openlogi.org/hidpp/features/x0000-root | root.getFeature |
| 5 | https://openlogi.org/hidpp/features/x0005-device-type-and-name | 设备类型枚举、名字读取 |
| 6 | https://openlogi.org/hidpp/features/x1000-battery-status | 旧电量功能与状态枚举 |
| 7 | https://openlogi.org/hidpp/features/x1001-battery-voltage | 电压功能（G 系列唯一来源） |
| 8 | https://openlogi.org/hidpp/features/x1004-unified-battery | 新统一电量功能 |
| 9 | https://support.logi.com/hc/en-us/articles/5470036605975-Unifying-Receiver-Software-Pairing-and-Troubleshooting | Unifying 最多 6 台（官方） |
| 10 | https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp10.py | 寄存器读写、电池兜底逻辑 |
| 11 | https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp10_constants.py | 0x07 / 0x0D 寄存器号 |
| 12 | https://www.usb.org/sites/default/files/hut1_4.pdf | HID Usage Tables（0x06/0x20） |
| 13 | https://www.usbzh.com/article/detail-975.html | Generic Device Controls 0x06 的 usage 表（0x20 Battery Strength） |
| 14 | https://pmortensen.eu/world2/2024/11/04/the-battery-state-of-a-keychron-qmk-based-keyboard-can-be-displayed-in-the-operating-system/ | Keychron BLE GATT 电量实测 |
| 15 | https://github.com/fastfetch-cli/fastfetch/blob/dev/src/detection/bluetooth/bluetooth_windows.c | 蓝牙电量属性键定义 |
| 16 | https://stackoverflow.com/questions/71736070/how-can-i-get-the-bluetooth-device-battery-percentage-using-powershell-on-windows | 蓝牙电量社区读法 |
| 17 | https://learn.microsoft.com/en-us/windows/win32/api/xinput/nf-xinput-xinputgetbatteryinformation | XInputGetBatteryInformation |
| 18 | https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_battery_information | XINPUT_BATTERY_INFORMATION 取值 |
| 19 | https://github.com/openrazer/openrazer | Razer 逆向（OpenRazer） |
| 20 | https://steelseries.com/gg | SteelSeries 官方软件 |
| 21 | https://www.corsair.com/us/en/s/icue | Corsair 官方软件 |
| 22 | https://hyperx.com/pages/ngenuity | HyperX 官方软件 |
| 23 | https://www.vgnlab.com.cn/ | VGN 官方（仅 HUB 驱动） |

---

## 8. 本项目在调研基础上的取舍

* **蓝牙 Provider 只读系统属性，不自己发 GATT 命令**：避免配对/权限/蓝牙栈兼容问题；代价是必须等系统填充属性。
* **XInput 只做 4 档估算并显式标注**：不伪造精确百分比。
* **不实现任何厂商私有协议**（Razer/SteelSeries/Corsair/HyperX/VGN）：收益/成本比不成立，列入支持矩阵的「不支持」。
* **罗技协议由 HID 组实现**，本文只描述协议，避免重复实现。