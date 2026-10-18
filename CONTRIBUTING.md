# Contributing

感谢你愿意参与。这个项目是一个 Windows 托盘工具，目标只有一个：**尽可能准确地显示 2.4G / 蓝牙外设的电量，并且不骗人**（读不到就显示未知）。

## 构建

要求：Windows 10/11 x64 + .NET Framework 4.8（系统自带，不需要 .NET SDK / NuGet）。

`powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Probe
.\dist\app\BatteryProbe.exe --selftest
`

## 代码约定

- C# 5 语法（csc.exe 4.8），不使用 NuGet / 第三方库；
- UI 改动请附截图（125% DPI 下验收）；
- 新增品牌协议请先读 `docs/generic-method.md`，然后只改
  `src/Core/Providers/VendorBatteryProtocols.cs` 里的协议表；
- 不允许提交厂商软件的私有代码/二进制；只提交自己实现的协议常量和解析逻辑；
- 提交前跑一次 `BatteryProbe --selftest` 和 `BatteryProbe --vendor`。

## 新增一个品牌

1. 用 USBPcap + Wireshark 抓一次“官方软件读电量”的 USB 包；
2. 找到 OUT/IN 的 HID Data（报告 ID、命令、响应偏移、校验）；
3. 在 `VendorProtocolRegistry.All` 加一条 `VendorBatteryProtocol`；
4. 用 `BatteryProbe.exe --vendor` 和主程序验证；
5. 更新 `docs/support-matrix.md` 与 README。

## 许可

提交代码即表示同意以 MIT License 发布。请确保你有权提交相关内容。
