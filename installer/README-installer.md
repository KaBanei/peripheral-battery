# 安装 / 卸载说明

本项目是**绿色免安装**的托盘程序：安装脚本只做三件事——复制文件、建快捷方式、写当前用户的开机启动项。
**不需要管理员权限**，不写 HKLM，不注册服务，不安装驱动。

## 1. 前置条件

| 项目 | 要求 |
| --- | --- |
| 系统 | Windows 10 1809+ / Windows 11，**x64** |
| 运行时 | .NET Framework **4.8**（Windows 10 1903+ / Windows 11 已内置；旧系统需先安装） |
| 权限 | 普通用户（安装到 %LOCALAPPDATA%，只写 HKCU） |
| 构建产物 | dist\app\PeripheralBattery.exe 及其依赖文件 |

先构建：

    powershell -ExecutionPolicy Bypass -File build.ps1

## 2. 安装

在仓库根目录运行：

    powershell -ExecutionPolicy Bypass -File installer\install.ps1

它会：

1. 结束正在运行的 PeripheralBattery.exe（避免文件占用）；
2. 把 dist\app\* 复制到 %LOCALAPPDATA%\Programs\PeripheralBattery；
3. 创建开始菜单快捷方式 %APPDATA%\Microsoft\Windows\Start Menu\Programs\PeripheralBattery.lnk；
4. 写开机启动：HKCU\Software\Microsoft\Windows\CurrentVersion\Run 的 PeripheralBattery 值，内容为：

    "C:\Users\<你>\AppData\Local\Programs\PeripheralBattery\PeripheralBattery.exe" --tray

> 路径始终用引号包住，且脚本只通过 Join-Path / -LiteralPath 拼路径，因此**中文用户名、含空格的路径**都能正常工作。

### 参数

| 参数 | 作用 |
| --- | --- |
| -WhatIf | 预演：只打印将要做的事，不做任何修改 |
| -DesktopShortcut | 额外创建桌面快捷方式 |
| -NoAutoStart | 不写开机启动项（之后可在程序设置里开启） |
| -NoShortcut | 不创建任何快捷方式（绿色使用/测试） |
| -SourceDir <dir> | 指定构建输出目录，默认 <仓库>\dist\app |
| -InstallDir <dir> | 自定义安装目录（默认为 LOCALAPPDATA，主要用于测试） |

示例：

    # 先看会改什么
    powershell -ExecutionPolicy Bypass -File installer\install.ps1 -WhatIf

    # 带桌面快捷方式安装
    powershell -ExecutionPolicy Bypass -File installer\install.ps1 -DesktopShortcut

    # 静默安装（不建快捷方式、不开机启动）
    powershell -ExecutionPolicy Bypass -File installer\install.ps1 -NoShortcut -NoAutoStart

### 安装后启动

* 开始菜单搜索 **PeripheralBattery**；
* 或直接双击 %LOCALAPPDATA%\Programs\PeripheralBattery\PeripheralBattery.exe。

## 3. 卸载

    powershell -ExecutionPolicy Bypass -File installer\uninstall.ps1

默认行为：删除快捷方式、删除开机启动项、删除程序目录，**保留** %APPDATA%\PeripheralBattery（配置 + 日志）。

彻底清除（连配置和日志一起删）：

    powershell -ExecutionPolicy Bypass -File installer\uninstall.ps1 -Purge

同样支持 -WhatIf 预演、-InstallDir 指定非默认安装目录。

### 手动卸载（脚本不可用时）

1. 退出托盘程序（右键托盘图标 → 退出）；
2. 删除 %LOCALAPPDATA%\Programs\PeripheralBattery；
3. 删除 %APPDATA%\Microsoft\Windows\Start Menu\Programs\PeripheralBattery.lnk（以及桌面上的同名快捷方式）；
4. 打开 regedit，删除 HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run 下的 PeripheralBattery；
5. 如需清除数据：删除 %APPDATA%\PeripheralBattery。

## 4. SmartScreen / 未签名提示

本项目**没有代码签名证书**（个人项目，证书成本高），因此首次运行未签名 exe 时，Windows 可能弹出：

> **Windows 已保护你的电脑**
> 应用：PeripheralBattery.exe
> 发布者：未知发布者

处理方式（确认来源可信后）：

1. 点提示框里的 **更多信息**；
2. 点 **仍要运行**。

如果 exe 是从压缩包/网络下载的，还可能被标记为「来自其他计算机」，可以：

* 右键 exe → **属性** → 底部勾选 **解除锁定** → 确定；或
* 在 PowerShell 里执行：

    Unblock-File -LiteralPath "$env:LOCALAPPDATA\Programs\PeripheralBattery\PeripheralBattery.exe"

### 自签名（可选，团队内部分发）

想消除「未知发布者」可以自签名，但需要把自签名证书导入到「受信任的根证书颁发机构」，否则仍会提示：

    # 1) 生成自签名代码签名证书（仅示例，按需调整）
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=PeripheralBattery Dev' -CertStoreLocation 'Cert:\CurrentUser\My'

    # 2) 签名（需要 Windows SDK 的 signtool.exe）
    signtool sign /fd SHA256 /a /n 'PeripheralBattery Dev' PeripheralBattery.exe

### 校验文件完整性

    Get-FileHash -Algorithm SHA256 "$env:LOCALAPPDATA\Programs\PeripheralBattery\PeripheralBattery.exe"

## 5. 脚本对系统做的全部改动

| 位置 | 内容 | 卸载时 |
| --- | --- | --- |
| %LOCALAPPDATA%\Programs\PeripheralBattery\ | 程序文件 | 删除 |
| %APPDATA%\Microsoft\Windows\Start Menu\Programs\PeripheralBattery.lnk | 开始菜单快捷方式 | 删除 |
| 桌面 PeripheralBattery.lnk | 仅 -DesktopShortcut 时创建 | 删除 |
| HKCU\...\CurrentVersion\Run → PeripheralBattery | 开机启动（"<exe>" --tray） | 删除 |
| %APPDATA%\PeripheralBattery\ | 程序运行时自己创建：config.json、app.log | **保留**（-Purge 才删） |

不写注册表其他位置，不写 Program Files，不装服务/驱动，不联网。

## 6. 常见问题

**Q：安装后没有开机自启？**

A：三种可能：安装时加了 -NoAutoStart；程序设置里「开机启动」被关掉；或任务管理器 →「启动」里被禁用。程序与安装脚本用的是**同一个**注册表值名（PeripheralBattery），不会互相打架。

**Q：升级怎么装？**

A：重新构建后**再跑一次 install.ps1** 即可：脚本会先结束进程，再覆盖复制；配置和日志不受影响。

**Q：路径里有中文/空格会不会出问题？**

A：不会。安装路径来自 %LOCALAPPDATA%，脚本全程用 Join-Path + -LiteralPath；注册表值整体加引号。

**Q：脚本被杀毒软件拦截？**

A：脚本只做「复制文件 / 建 .lnk / 写 HKCU Run」，可先用 -WhatIf 查看每一步，或直接按第 3 节手动卸载。

**Q：为什么不需要管理员？**

A：安装目录在用户配置目录，快捷方式在用户开始菜单，启动项在 HKCU，全部是当前用户可写范围。

**Q：卸载后配置还在？**

A：这是设计行为（重装后保留设置）。彻底清除用 -Purge。

## 7. 本脚本的验证记录（2026-10-18）

| 验证项 | 命令 | 结果 |
| --- | --- | --- |
| 语法解析（PS 5.1 Parser） | Parser::ParseFile | 两个脚本 0 错误（注意：文件必须是 **UTF-8 带 BOM**，否则 PS 5.1 按 GBK 解析中文会报错） |
| 预演 | install.ps1 -SourceDir _scratch\fakeapp -InstallDir _scratch\install-test -WhatIf | 只打印 3 个 What if 动作，目录未被创建，HKCU 未被修改 |
| 真实复制 | 去掉 -WhatIf，加 -NoShortcut -NoAutoStart | 退出码 0，临时目录出现 PeripheralBattery.exe 与附属文件 |
| 卸载预演 | uninstall.ps1 -InstallDir _scratch\install-test -WhatIf | 只打印动作，目录仍存在 |
| 真实卸载 | 去掉 -WhatIf | 退出码 0，临时目录已删除 |
| 危险路径 | uninstall.ps1 -InstallDir 'C:\' | 拒绝执行（抛错，非 0 退出） |
| 系统污染检查 | 安装测试后查 HKCU Run 与开始菜单 | 均为空/不存在（测试路径没有触碰系统位置） |

> 说明：真实安装会写 HKCU 与开始菜单，属于对用户环境的持久改动，因此自动化验证只做到「临时目录 + 预演」。正式安装请自行执行第 2 节命令，卸载可完全回退。