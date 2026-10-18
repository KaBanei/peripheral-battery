# 开源项目免费代码签名（SignPath / Certum）

> 目标：GitHub 开源后，用**开源项目专用**的免费代码签名，让 exe 显示正式发布者，
> 不再出现 "Unknown publisher"。注意：免费证书基本都是 OV 级别，
> **SmartScreen 信誉仍会随下载量/时间积累**，不会像 EV 那样立刻全绿。

---

## 方案 A：SignPath Foundation（推荐，可接 GitHub Actions）

SignPath 为开源项目提供免费的代码签名服务（证书由 Sectigo/SignPath Foundation 签发）。
适合：公开仓库 + OSI 认可许可证（MIT/Apache/GPL 等）+ 非商业项目。

### 1. 先把仓库整理成可申请状态

- GitHub 仓库 **public**；
- 根目录有 `LICENSE`（本项目用 MIT）；
- 有 README、构建脚本、release；
- 项目在维护（有提交/issue）。

### 2. 申请

打开 <https://signpath.org/foundation>，填写：

- 项目名、仓库 URL、许可证；
- 维护者联系方式；
- 说明用途（开源桌面工具，签名 Windows exe）。

审核通过后你会收到 SignPath 账号邀请。

### 3. 在 SignPath 里配置

1. 安装 **SignPath GitHub App** 到你的仓库（只勾选目标仓库）；
2. 新建 Project：slug 建议 `peripheral-battery`；
3. 新建 **Artifact Configuration**：slug 建议 `exe`，指定要签名的文件：
   `PeripheralBattery.exe`、`BatteryProbe.exe`、`VgnProbe.exe`；
4. 新建 **Signing Policy**：slug 建议 `release-signing`，绑定该 artifact configuration；
5. 在组织设置里生成 **API token**，记下 **Organization ID**。

### 4. 配置 GitHub Secrets / Variables

仓库 → Settings → Secrets and variables → Actions：

| 类型 | 名称 | 值 |
| --- | --- | --- |
| Secret | `SIGNPATH_API_TOKEN` | SignPath 生成的 token |
| Secret | `SIGNPATH_ORGANIZATION_ID` | SignPath Organization ID |
| Variable（可选） | `SIGNPATH_PROJECT_SLUG` | 默认 peripheral-battery |
| Variable（可选） | `SIGNPATH_POLICY_SLUG` | 默认 release-signing |
| Variable（可选） | `SIGNPATH_CONFIG_SLUG` | 默认 exe |

### 5. 发版

仓库里已经准备好 `.github/workflows/release-sign.yml`：

`bash
git tag v1.3.1
git push origin v1.3.1
`

工作流会：构建 → 跑自检 → 上传未签名产物 → 提交 SignPath 签名 → 等签名完成 →
打包 zip → 创建 GitHub Release。签名后的文件在 `signed/` 目录。

> 第一次跑之前，把 workflow 里的 `artifact-configuration-slug` 等改成你在 SignPath 里实际创建的 slug。

### 6. SmartScreen 预期

- 签名后属性里会显示发布者为 **SignPath Foundation**（或你项目绑定的组织）；
- OV 证书初始仍可能触发 SmartScreen，需要下载量/时间积累信誉；
- 想立刻全绿只能 EV（开源项目一般拿不到免费 EV）。

---

## 方案 B：Certum 开源代码签名证书（手工签名）

Certum 对开源开发者提供免费代码签名证书（Open Source Code Signing）：

1. 打开 Certum 的 Open Source Code Signing 页面，提交项目仓库、许可证、维护者身份；
2. 审核通过后按邮件指引完成身份验证，证书通过 **SimplySign** 云签名使用；
3. 签名（本项目脚本支持 PFX / 证书指纹）：
   `powershell
   powershell -ExecutionPolicy Bypass -File installer\sign.ps1 -Thumbprint <证书指纹>
   `
4. 时间戳已内置（RFC3161），证书过期后签名仍有效。

缺点：没有 SignPath 那种开箱即用的 GitHub Action，需要手工/自定义 CI；仍然是 OV 信誉模型。

---

## 方案 C：不符合免费资格时

- SignPath.io 付费计划（按签名次数）；
- Azure Trusted Signing（要求约 3 年企业历史，个人通常不符合）；
- 购买 OV/EV 证书（见 `docs/signing.md`）。

---

## 发布检查清单

- [ ] 仓库 public + LICENSE（MIT）
- [ ] `.github/workflows/build.yml` 绿灯
- [ ] SignPath 申请通过，GitHub App 已安装
- [ ] Secrets / Variables 配好
- [ ] 打 tag → release-sign 工作流成功
- [ ] 下载 release 里的 exe，属性 → 数字签名 显示有效
- [ ] 在干净机器上运行一次，确认 SmartScreen 表现
- [ ] 发布页附 `SHA256SUMS.txt`

---

## 参考

- SignPath Foundation：<https://signpath.org/foundation>
- SignPath GitHub Action：<https://github.com/signpath/github-action-submit-signing-request>
- Certum Open Source Code Signing：<https://www.certum.eu/en/code-signing-certificates/open-source-code-signing/>
