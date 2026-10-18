# 上架 GitHub 完整清单与流程

> 目标：把当前项目以 **MIT 开源** 发布到 GitHub，启用 CI，最终用 SignPath 免费签名发版。
> 仓库地址占位符：`https://github.com/<你的用户名>/peripheral-battery`。

---

## 0. 仓库里已经准备好的内容

| 内容 | 路径 | 说明 |
| --- | --- | --- |
| 许可证 | `LICENSE` | MIT |
| 项目说明 | `README.md` | 中文主体 + 英文简介、截图、支持范围、构建、更新日志 |
| 贡献指南 | `CONTRIBUTING.md` | 构建、代码约定、新增品牌协议流程 |
| 安全策略 | `SECURITY.md` | 私有漏洞报告、设计边界 |
| CI | `.github/workflows/build.yml` | push/PR 自动构建 + `BatteryProbe --selftest` |
| 发版签名 | `.github/workflows/release-sign.yml` | tag 触发：构建 → SignPath 签名 → GitHub Release |
| Issue 模板 | `.github/ISSUE_TEMPLATE/*.md` | bug / feature |
| 核心代码 | `src/Core`、`src/App` | csc / .NET Framework 4.8，无 NuGet |
| 工具 | `tools/Probe`、`tools/VgnProbe`、`tools/qa` | 诊断/抓包/截图 |
| 安装脚本 | `installer/install.ps1`、`uninstall.ps1`、`sign.ps1` | 安装/卸载/签名 |
| 文档 | `docs/` | 协议调研、支持矩阵、架构、通用方法、签名方案 |

已清理：个人设备名、USB 抓包（`*.pcapng` 已 ignore）、临时探针、构建产物、`_ref/` 厂商解包文件。
仓库当前体积约 1 MB，无 Git LFS 需求。

---

## 1. 推送前自检（本机）

`powershell
cd "C:\Users\ASUS\Desktop\个人\2.4g外设电量管理"

# 1) 工作区必须干净
git status --short

# 2) 全量构建 + 自检
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Probe
.\dist\app\BatteryProbe.exe --selftest

# 3) 检查没有敏感文件被跟踪
git ls-files | Select-String -Pattern 'pcapng|_ref|data/|app.log'
`

预期：构建成功、`PASS 53 / FAIL 0`、最后一条检查无输出。

---

## 2. 创建 GitHub 仓库

1. 打开 <https://github.com/new>；
2. Repository name：`peripheral-battery`（或 `2.4g-battery-monitor`）；
3. 选 **Public**；
4. **不要**勾选 “Add a README / .gitignore / license”（本地已有）；
5. Create repository。

然后在本机推送（二选一）：

`powershell
# HTTPS（推荐，用 Personal Access Token 作为密码）
git branch -M main
git remote add origin https://github.com/<你的用户名>/peripheral-battery.git
git push -u origin main
`

`powershell
# SSH（已配置 SSH key 时）
git remote add origin git@github.com:<你的用户名>/peripheral-battery.git
git push -u origin main
`

推送成功后，GitHub 会自动识别 MIT 许可证并运行 `build` 工作流。

---

## 3. 确认 CI 绿灯

- 仓库 → **Actions** → `build`；
- 应看到：Build → Self test → Upload artifacts 全部成功；
- 失败时点开日志，常见原因：
  - 分支不是 `main`（workflow 只监听 main，可改 workflow 或改分支名）；
  - GitHub 新仓库的 Actions 权限是只读：Settings → Actions → General →
    **Workflow permissions** 选 **Read and write permissions** → Save。

---

## 4. 发布第一个版本

### 情况 A：先发未签名版（想马上有 Release）

1. 本地确认 `dist/2.4G外设电量监控-v1.3.1.zip` 存在（构建/打包后生成）；
2. **先禁用** `release-sign` 工作流（Actions → release-sign → ⋯ → Disable workflow），
   否则推送 tag 会因缺少 SignPath Secret 失败；
3. GitHub → Releases → **Draft a new release**：
   - Tag：`v1.3.1`（Create new tag on publish）
   - Title：`v1.3.1`
   - 说明：可粘贴 README 的“更新日志”段；
   - 附件：上传 `2.4G外设电量监控-v1.3.1.zip`；
4. Publish release。

> 注意：一旦在这里创建了 `v1.3.1` tag，之后 SignPath 配好也不能用同一个 tag 再触发；
> 想补签名版就发 `v1.3.2`。所以更推荐情况 B。

### 情况 B：等 SignPath 通过后直接发签名版（推荐）

先完成第 5、6 步，再打 tag，一步到位。

---

## 5. 申请 SignPath Foundation（免费开源签名）

完整步骤见 [opensource-signing.md](opensource-signing.md)，这里只列顺序：

1. 确认仓库 public、LICENSE 是 MIT、有最近提交；
2. 打开 <https://signpath.org/foundation> 提交申请（项目名/仓库/许可证/用途）；
3. 通过后安装 **SignPath GitHub App**；
4. 在 SignPath 建 Project（`peripheral-battery`）、
   Artifact Configuration（`exe`，勾选三个 exe）、Signing Policy（`release-signing`）；
5. 生成 API token，记录 Organization ID。

申请说明可以写：

`text
Open-source Windows tray utility (MIT) that reads battery levels of 2.4G/Bluetooth
peripherals via HID (Logitech HID++, VGN private protocol, BLE, XInput).
No drivers, no telemetry. Repository: https://github.com/<user>/peripheral-battery
Requesting free code signing for release binaries (PeripheralBattery.exe,
BatteryProbe.exe, VgnProbe.exe).
`

---

## 6. 配置签名并发布

仓库 → Settings → Secrets and variables → Actions：

| 类型 | 名称 | 值 |
| --- | --- | --- |
| Secret | `SIGNPATH_API_TOKEN` | SignPath token |
| Secret | `SIGNPATH_ORGANIZATION_ID` | Organization ID |
| Variable（可选） | `SIGNPATH_PROJECT_SLUG` | peripheral-battery |
| Variable（可选） | `SIGNPATH_POLICY_SLUG` | release-signing |
| Variable（可选） | `SIGNPATH_CONFIG_SLUG` | exe |

确认 `release-sign.yml` 里的 slug 与 SignPath 里一致，然后：

`powershell
git tag -a v1.3.1 -m "v1.3.1"
git push origin v1.3.1
`

工作流会自动：构建 → 自检 → 上传 → SignPath 签名 → 打包 → 创建 Release。
下载 release 里的 exe 验证：

`powershell
Get-AuthenticodeSignature .\PeripheralBattery.exe | Format-List Status, SignerCertificate
`

---

## 7. 仓库设置（让它看起来“正规”）

仓库主页 → ⚙️ Settings / About：

- **Description**：
  `Windows tray battery monitor for 2.4G / Bluetooth peripherals (Logitech HID++, VGN, BLE, Xbox). MIT.`
- **Topics**：
  `windows` `battery` `hid` `logitech` `vgn` `24ghz` `bluetooth` `winforms` `csharp` `system-tray`
- **Social preview**：上传 `docs/images/main.png`；
- **Releases**：勾选 “Set as latest release”；
- **Branches**：main 分支保护，要求 `build` 通过（可选）；
- **Security**：开启 Private vulnerability reporting（SECURITY.md 已写）；
- **Issues**：模板已就绪；需要讨论区可开 Discussions。

README 顶部可加 Actions 徽章（把 URL 换成你的仓库）：

`markdown
![build](https://github.com/<user>/peripheral-battery/actions/workflows/build.yml/badge.svg)
`

---

## 8. 后续维护

- 版本号：`src/Core/AssemblyInfo.cs` 与 `src/App/UiUtil.cs` 同步改；
- 每次发版：改 README 更新日志 → 构建自检 → 提交 → 打 tag → 自动签名发布；
- 新品牌适配：按 [generic-method.md](generic-method.md) 的“五步”，只改协议表；
- 安全漏洞：走 GitHub Security Advisory，不公开 issue；
- 发布包建议始终附 `SHA256SUMS.txt`（仓库根目录已有生成示例）。

---

## 9. 常见问题

| 现象 | 原因/处理 |
| --- | --- |
| Actions 不运行 | 分支名不是 `main`，或仓库 Actions 被禁用 |
| Release 工作流 403 | Settings → Actions → Workflow permissions 改为 Read and write |
| 签名失败 `artifact not found` | `release-sign.yml` 的 artifact slug 与 SignPath 项目不一致 |
| tag 推上去没触发 | tag 不匹配 `v*`，或 workflow 被 Disable |
| SmartScreen 仍提示 | OV 证书需要信誉积累；确认签名有效后再等下载量 |
| 中文文件名在 zip 里乱码 | 解压工具编码问题，建议用 Windows 资源管理器/7-Zip 打开 |
| 仓库变大 | 检查是否误提交 `dist/`、`_artifacts/`、`*.pcapng`（已在 .gitignore） |

---

## 10. 一页命令速查

`powershell
cd "C:\Users\ASUS\Desktop\个人\2.4g外设电量管理"
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Probe
.\dist\app\BatteryProbe.exe --selftest

git branch -M main
git remote add origin https://github.com/<user>/peripheral-battery.git
git push -u origin main

# SignPath 配好后
git tag -a v1.3.1 -m "v1.3.1"
git push origin v1.3.1
`
