# 去掉 SmartScreen 警告 / 让程序更"正规"

> 截图里的 **"Windows 已保护你的电脑 / 发布者未知"** 不是杀毒误报，是 Windows SmartScreen
> 对**未签名 + 低信誉**的可执行文件的默认拦截。它看两件事：
> 1. 文件有没有**受信任的代码签名**；
> 2. 这个文件/发布者有没有**信誉记录**（下载量、时间、签名证书）。
>
> 没有证书，就没有"官方"办法彻底关掉它；下面的方案按成本从低到高排列。

---

## 0. 先分清：自己用 vs 发给别人

### 自己这台电脑（最快）
SmartScreen 主要针对"从网上下载"的文件（带 Mark-of-the-Web）。解除方法：

- 右键 exe → **属性** → 勾选 **"解除锁定 / Unblock"** → 确定；
- 或 PowerShell：
  `powershell
  Unblock-File .\PeripheralBattery.exe
  # 整个目录：
  Get-ChildItem .\dist -Recurse -File | Unblock-File
  `
- 或者点截图里的 **"更多信息 → 仍要运行"**（只需一次，之后同文件不再拦）。

本项目的发布包里附了 `tools/unblock.ps1`，双击/右键运行即可批量解除。

> 注意：从压缩包解压时 MOTW 会传给里面的 exe；先解压再 Unblock 才有效。

### 发给别人 / 长期分发
必须面对"签名 + 信誉"两件事，见下表。

---

## 1. 方案对比

| 方案 | 成本 | 效果 | 适合 |
| --- | --- | --- | --- |
| **不签名**（现状） | 0 | 发布者未知；SmartScreen 可能拦；属性里无数字签名 | 自己用、朋友间小范围 |
| **自签名证书** | 0 | 只在本机（装过证书的机器）不提示；别人仍提示 | 公司内网/自己电脑 |
| **OV 代码签名证书** | 约 ¥1000–2500/年 | 发布者显示公司名；SmartScreen 需**积累信誉**，初期仍可能提示 | 小团队正式发布 |
| **EV 代码签名证书** | 约 ¥3000–6000/年 + 硬件令牌 | 通常**立即**通过 SmartScreen；发布者显示公司名 | 商业软件 |
| **Azure Trusted Signing** | $9.99/月 | 微软云签名，信誉较好 | 有 3 年企业历史的团队（个人常不符合） |
| **Microsoft Store (MSIX)** | 个人开发者约 $19 一次性 | 商店签名，用户**不会**看到 SmartScreen；需过审核 | 想最"正规"且预算低 |
| **开源项目签名（SignPath 等）** | 免费 | 面向开源项目 | 公开开源项目 |

> "正规"不等于必须 EV：OV 证书能去掉"未知发布者"，SmartScreen 信誉会随下载量/时间慢慢建立。
> 如果只是自己用，**不要为此买证书**，Unblock 即可。

---

## 2. 用证书签名（OV/EV 到手后）

证书通常以 `.pfx` 或 USB 令牌形式提供。项目提供了脚本：

`powershell
# 1) PFX 方式（OV 常见）
powershell -ExecutionPolicy Bypass -File installer\sign.ps1 `
  -PfxPath D:\cert\mycert.pfx -PfxPassword "******"

# 2) 证书已装在 CurrentUser\My（EV 令牌驱动装好后）
powershell -ExecutionPolicy Bypass -File installer\sign.ps1 -Thumbprint <证书指纹>

# 3) 本机自签名（只用于自己电脑，别人仍会提示）
powershell -ExecutionPolicy Bypass -File installer\sign.ps1 -SelfSigned
`

脚本会对 `dist/app` 下所有 exe 用 `Set-AuthenticodeSignature` 签名，并加 RFC3161 时间戳
（时间戳很重要：证书过期后签名仍然有效）。

验证：

`powershell
Get-AuthenticodeSignature .\dist\app\PeripheralBattery.exe | Format-List Status, SignerCertificate
`

---

## 3. 更"正规"的发布方式（不买证书也行）

1. **做成安装包**（Inno Setup / MSI）：
   - 用户只在"运行安装程序"时看到一次提示，装完后从 Program Files 启动的 exe **没有 MOTW**，
     通常不会再被 SmartScreen 拦；
2. **提供 SHA256 校验值**：发布页附 `SHA256SUMS.txt`，用户可核对完整性；
3. **版本信息完整**：产品名、版本、公司/版权、图标（本项目已设置，见 exe 属性）；
4. **商店上架**：MSIX 打包 + Microsoft Store，商店负责签名与分发，用户体验最好；
5. **误报申诉**：如果 Defender 报毒（不是 SmartScreen），可提交
   <https://www.microsoft.com/en-us/wdsi/filesubmission> 申请复核。

---

## 4. 本项目当前的"正规化"清单

- [x] 完整版本信息（产品名/描述/版本，见 exe 属性）
- [x] 多尺寸应用图标
- [x] 安装/卸载脚本（开始菜单快捷方式、开机启动、卸载保留配置）
- [x] 单实例、日志、诊断信息、崩溃兜底
- [x] 发布包内 `使用说明.txt` + 文档
- [ ] 代码签名证书（需要购买/申请）
- [ ] 安装包 exe（可选，Inno Setup）
- [ ] SHA256SUMS.txt（发布时生成）

---

## 5. 一句话建议

- **只自己用**：`Unblock-File` 或属性解除锁定，点一次"仍要运行"，不用花钱；
- **发给少量朋友**：做成安装包，或附上 SHA256 + 源码构建脚本；
- **公开分发/商业化**：买 **OV**（预算有限）或 **EV**（要立刻过 SmartScreen）证书，
  用 `installer/sign.ps1` 签名并加时间戳；预算极低可考虑 Microsoft Store。
