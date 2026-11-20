# 分支与发布流程

一句话：**dev 上干活 → 你验收 → 合进 master → 打 tag 发包**。
master 永远是可用的，dev 允许是半成品。

```
            agent 每次会话                    你验收 + 合并                 打发布包
本地 dev  ──────────────►  origin/dev  ──────────────►  origin/master  ──────────────►  GitHub Releases
```

## 一、分支模型

| 分支 | 角色 | 规矩 |
| --- | --- | --- |
| `master` | **发布分支**（默认分支） | 只放验收过的代码；每次合并后同步 push |
| `dev` | **日常工作分支** | agent 的所有改动先提交到这里；允许中间状态 |

> 只有两个长期分支，不做 `feature/*`——这个项目改动集中在几个模块，两条线足够，少一层合并就少一类事故。

### agent 每次会话的固定动作

```powershell
git checkout dev
git add -A
git commit -m "<一句话说明这次改了什么>"
git push origin dev          # ← “先上传这个分支”
```

### 你验收 → 合进 master

验收先跑三件套（见 AGENTS.md §4）：`cumod.selftest` / `cumod.uitest` / `cumod.cfgtest`，
再实际进一局游戏看那几条修复。确认没问题：

```powershell
# 方式 A：本地合并（推荐，历史干净）
git checkout master
git merge --no-ff dev -m "合并 dev：<本次内容>"
git push origin master

# 方式 B：GitHub 上开 PR（dev → master），审完点 Merge
```

出问题就**别合**：在 dev 上继续修，或 `git revert <提交>` 掉那一条。

### 合并前的最小自检

```powershell
pwsh scripts/build.sh          # 编译 + 部署（不带存档编辑器）
# 游戏里按 F11 开管理器，翻一遍「全部配置」「兼容性」「关于」
# 日志看：Select-String LogOutput.log -Pattern 'Exception|\[MPCOMPAT\]|\[CraftFix\]'
```

## 二、发布

### 为什么不能交给 GitHub Actions 自动构建

这个项目**编译期就要游戏本体的托管程序集**（`Assembly-CSharp.dll`、`UnityEngine.*.dll`）
和 BepInEx 的 `0Harmony.dll` —— 它们是商业游戏的安装文件，既不能进仓库、也不能传到 CI。
所以：**本地构建，上传产物**。这不是懒，是版权约束下的唯一做法。

### 一条命令发版

```powershell
pwsh scripts/release.ps1                 # 打包到 dist/（不上传）
pwsh scripts/release.ps1 -Upload         # 打包 + 创建 GitHub Release 并上传
pwsh scripts/release.ps1 -Version 1.2.1 -Notes "顺手修了什么" -Upload
```

脚本做的事：

1. 读 `Directory.Build.props` 的 `<DdenVersion>` 作为版本号（**改版本只改这一行**）；
2. `dotnet build -c Release`；
3. 组装 `dist/DDEN-v<版本>/`：三个 dll + `README.md` + `LICENSE` + `LICENSE-ASSETS` + `NOTICE`；
4. 压成 `dist/DDEN-v<版本>.zip`，打印 **SHA256**；
5. 生成 `dist/RELEASE-v<版本>.md`（安装步骤 + 上次 tag 以来的提交 + 校验值 + 授权说明）；
6. 加 `-Upload` 时创建 tag `v<版本>` 的 Release 并挂上 zip。

上传需要二选一：

- **gh CLI**（推荐）：`winget install GitHub.cli` → `gh auth login` → 之后脚本自动用 `gh release create`；
- **Token**：设 `$env:GITHUB_TOKEN`（`repo` 权限），脚本走 REST API 直接建 Release + 传附件。

两者都没有时，脚本只打包并提示你手动把 zip 拖到仓库 Releases 页（这条路永远可用）。

### 版本号怎么走

- 单套发布版本：三个 dll 共用 `Directory.Build.props` 的 `<DdenVersion>`，
  Release 页上看到的就是同一个版本号，不会出现「管理器 1.0.0 / 修复 1.2.0」的混乱。
- 规则建议：**改了行为 → 次版本 +1**（1.2.0 → 1.3.0）；**只修 bug → 修订号 +1**（1.2.0 → 1.2.1）；
  破坏性调整（配置键改名、目录结构变化）→ 主版本 +1 并在 Release 说明里写清迁移方式。
- 发版顺序：合并到 master → 确认 master 是你要发的那份 → 跑 `release.ps1 -Upload`。

### 发布包里有什么

```
DDEN-v1.2.0.zip
├── DDEN.Shared.dll          ← 公共层，必须有
├── DDExcNet-CUmodM.dll      ← 管理器
├── DDEN.MPfix.dll           ← 多人修复
├── README.md                ← 安装与功能说明
├── LICENSE                  ← 代码：MPL-2.0
├── LICENSE-ASSETS           ← 图标/世界观：保留所有权利
└── NOTICE                   ← 第三方归属
```

存档编辑器（`DDEN.SaveEditor.dll`）**不进发布包**：它是可选组件，需要的人自己构建时带上
（`scripts/build.sh --with-saveeditor`）。

### 发完要做的三件事

1. 把 zip 的 SHA256 贴到发布帖（脚本已经写进 Release 说明里）；
2. 在 README 顶部/简介里更新「当前版本」；
3. 有人反馈问题时，先看日志里 `[MPCOMPAT]` 打出的联机框架版本是否在已校对范围内。

## 三、出事了怎么办

| 情况 | 做法 |
| --- | --- |
| 刚合并到 master 就发现坏了 | `git revert <合并提交>` → push → 立刻发一个修订版本 |
| 已经发出去的版本有严重问题 | 不要删 Release（会让人以为被攻击），在其描述里加一行「已知问题，请用 vX.Y.Z」，然后发新版 |
| dev 上改乱了 | `git reset --hard origin/dev`（丢掉本地未提交的）或从 master 重新拉一条 dev |
| 忘记改版本号就发了 | 重新构建会覆盖同名 zip；Release 上的 tag 已经用完这个号，那就把版本号加一下重发 |
