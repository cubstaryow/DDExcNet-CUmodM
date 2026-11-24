<#
.SYNOPSIS
  把当前代码打成可发布的包（dist/DDExcNet-v<版本>.zip），可选直接创建 GitHub Release 并上传。

.EXAMPLE
  pwsh scripts/release.ps1
      构建 Release 并打包，不上传。产物在 dist/，会打印 SHA256。

.EXAMPLE
  pwsh scripts/release.ps1 -Upload
      打完包后创建 GitHub Release 并上传 zip（需要 gh CLI 已登录，或 GITHUB_TOKEN 环境变量）。

.EXAMPLE
  pwsh scripts/release.ps1 -Version 1.7.1 -Notes "本次发布说明" -Upload
      指定版本号与一段自定义说明。

.NOTES
  · 包版本默认读取管理器项目；每个DLL按自身项目版本验证，-Version不会修改源码。
  发布包内容 = DDEN 七个组件（公共层 / 管理器 / 多人修复 / 性能跟踪器 / 存档编辑器 / 单位清单管理器 / 龙灼线束）+ 许可与说明文件。
  · 不会自动 push 分支；分支推送、创建tag及线上Release各自按用户授权处理。
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Upload,
    [string]$Notes,
    [switch]$SkipBuild,
    [string]$Target,
    # 没有 gh / token 时用这条：把发布说明复制到剪贴板，并打开 GitHub 的「新建 Release」页
    [switch]$OpenPage
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Push-Location $Root
try {
    # ===== 1. 版本号 =====
    function Read-ProjectVersion([string]$relativePath) {
        [xml]$project = [IO.File]::ReadAllText((Join-Path $Root $relativePath))
        $values = @($project.SelectNodes('/Project/PropertyGroup/Version') | ForEach-Object { $_.InnerText.Trim() } | Where-Object { $_ })
        if ($values.Count -ne 1 -or $values[0] -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw "项目未声明明确的独立版本：$relativePath" }
        return $values[0]
    }
    # 发布包版本跟 **MPfix（DDEN.MPfix）** 走；各组件各自独立版本，不再要求与管理器同号。
    $packageSource = 'src/DDEN.MPfix/DDEN.MPfix.csproj'
    $sourceVersion = Read-ProjectVersion $packageSource
    if (-not $Version) { $Version = $sourceVersion }
    if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw "发布包版本格式不合法：$Version" }
    $Tag = "v$Version"
    Write-Host "== 版本: $Version（tag $Tag，按 MPfix 版本；管理器 $((Read-ProjectVersion 'src/DDExcNet.CUmodM/DDExcNet.CUmodM.csproj'))）" -ForegroundColor Cyan

    # ===== 2. 构建 =====
    if (-not $SkipBuild) {
        Write-Host '== 构建 Release…' -ForegroundColor Cyan
        dotnet build 'DDExcNet.slnx' -c Release -v minimal -m:1 -nodeReuse:false -p:DeployOnBuild=false -p:UseSharedCompilation=false -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) { throw "构建失败（exit $LASTEXITCODE）" }
    }

    $releaseBin = @{
        'DDEN.Shared.dll'           = 'src/DDEN.Shared/bin/Release/DDEN.Shared.dll'
        'DDExcNet-CUmodM.dll'       = 'src/DDExcNet.CUmodM/bin/Release/DDExcNet-CUmodM.dll'
        'DDEN.MPfix.dll'            = 'src/DDEN.MPfix/bin/Release/DDEN.MPfix.dll'
        'DDEN.NetPerfTracker.dll'   = 'src/DDEN.NetPerfTracker/bin/Release/DDEN.NetPerfTracker.dll'
        'DDEN.SaveEditor.dll'       = 'src/DDEN.SaveEditor/bin/Release/DDEN.SaveEditor.dll'
        'DDEN.UnitListMgr.dll'      = 'src/DDEN.UnitListMgr/bin/Release/DDEN.UnitListMgr.dll'
        'DDEN.LoongNova.dll'       = 'src/DDEN.LoongNova/bin/Release/DDEN.LoongNova.dll'
    }
    # 注意：发布包 = **DDEN 六个组件**（公共层 / 管理器 / 多人修复 / 性能跟踪器 / 存档编辑器 / 单位清单管理器）。
    # 独立外部模组「ET-Fix（ET 加热笼修复）」**不随本包发布**，它有自己的仓库与发布节奏；
    # 本脚本不会构建或部署外部模组；JDI-DDEN同样不随包发布。
    foreach ($pair in $releaseBin.GetEnumerator()) {
        if (-not (Test-Path -LiteralPath $pair.Value -PathType Leaf)) { throw "缺少产物：$($pair.Value)（先构建 Release）" }
        $projectName = if ($pair.Key -eq 'DDExcNet-CUmodM.dll') { 'DDExcNet.CUmodM' } else { [IO.Path]::GetFileNameWithoutExtension($pair.Key) }
        $componentVersion = Read-ProjectVersion "src/$projectName/$projectName.csproj"
        $expectedAssemblyVersion = ($componentVersion -split '-')[0] + '.0'
        $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $Root $pair.Value)).Version.ToString()
        $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Root $pair.Value)).ProductVersion
        if (-not $productVersion -or ($productVersion -split '\+')[0] -ne $componentVersion) { throw "组件发行版本不匹配：$($pair.Key) = $productVersion，项目 $componentVersion" }
        if ($assemblyVersion -ne $expectedAssemblyVersion) { throw "组件版本不匹配：$($pair.Key) = $assemblyVersion，项目 $componentVersion（预期 $expectedAssemblyVersion）" }

    }

    # ===== 3. 组装发布目录 =====
    $dist = Join-Path $Root 'dist'
    $stage = Join-Path $dist "DDExcNet-$Tag"
    if (Test-Path -LiteralPath $stage) {
        $resolvedDist = (Resolve-Path -LiteralPath $dist).Path.TrimEnd('\')
        $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
        if ((Split-Path -Parent $resolvedStage) -ne $resolvedDist -or (Split-Path -Leaf $resolvedStage) -ne "DDExcNet-$Tag" -or ((Get-Item -LiteralPath $stage).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '拒绝清理非预期发布路径。' }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    foreach ($pair in $releaseBin.GetEnumerator()) {
        Copy-Item $pair.Value (Join-Path $stage $pair.Key) -Force
    }
    Copy-Item -LiteralPath (Join-Path $Root 'docs/release/INSTALL.md') -Destination (Join-Path $stage 'README.md')
    foreach ($doc in @('LICENSE', 'LICENSE-ASSETS', 'NOTICE')) {
        if (Test-Path $doc) { Copy-Item $doc $stage -Force }
    }

    # ===== 4. 打包 =====
    $zip = Join-Path $dist "DDExcNet-$Tag.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    $size = [math]::Round((Get-Item $zip).Length / 1KB, 1)
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    Write-Host "== 产物: dist/DDExcNet-$Tag.zip（$size KB）" -ForegroundColor Green
    Write-Host "   SHA256: $hash"

    # ===== 4b. Nexus 包：目录结构固定为 BepInEx/plugins/DDEN/*.dll（直接解压到游戏根目录即可） =====
    $nexusStage = Join-Path $dist "nexus-stage-$Tag"
    if (Test-Path -LiteralPath $nexusStage) { Remove-Item -LiteralPath $nexusStage -Recurse -Force }
    $nexusDden = Join-Path $nexusStage 'BepInEx/plugins/DDEN'
    New-Item -ItemType Directory -Force -Path $nexusDden | Out-Null
    foreach ($pair in $releaseBin.GetEnumerator()) {
        Copy-Item $pair.Value (Join-Path $nexusDden $pair.Key) -Force
    }
    $nexusZip = Join-Path $dist "DDExcNet-$Tag-Nexus.zip"
    if (Test-Path $nexusZip) { Remove-Item $nexusZip -Force }
    Compress-Archive -Path (Join-Path $nexusStage '*') -DestinationPath $nexusZip -CompressionLevel Optimal
    $nexusSize = [math]::Round((Get-Item $nexusZip).Length / 1KB, 1)
    $nexusHash = (Get-FileHash $nexusZip -Algorithm SHA256).Hash.ToLower()
    Remove-Item -LiteralPath $nexusStage -Recurse -Force
    Write-Host "== 产物: dist/DDExcNet-$Tag-Nexus.zip（$nexusSize KB，BepInEx/plugins/DDEN/*.dll）" -ForegroundColor Green
    Write-Host "   SHA256: $nexusHash"

    # ===== 4c. 外部模组：顺手编译并放到 dist/other/<版本号>/*.dll =====
    $externalMods = @(
        @{ Name = 'ET-Fix';            Project = 'src/外部mod/ET-Fix/ET-Fix.csproj';                         Dll = 'ET-Fix.dll' },
        @{ Name = 'JDI-DDEN';          Project = 'src/外部mod/JDI-DDEN/JDI-DDEN.csproj';                     Dll = 'JDI-DDEN.dll' },
        @{ Name = 'DS Tool Extension'; Project = 'src/外部mod/DS Tool Extension/DS Tool Extension.csproj';   Dll = 'DS Tool Extension.dll' }
    )
    $otherRoot = Join-Path $dist 'other'
    $otherLines = @()
    foreach ($mod in $externalMods) {
        $projectPath = Join-Path $Root $mod.Project
        if (-not (Test-Path -LiteralPath $projectPath)) { Write-Host "   （跳过外部模组 $($mod.Name)：项目不存在）" -ForegroundColor Yellow; continue }
        dotnet build $projectPath -c Release -v quiet -m:1 -nodeReuse:false -p:NuGetAudit=false | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host "   （外部模组 $($mod.Name) 构建失败，已跳过）" -ForegroundColor Yellow; continue }
        $modVersion = Read-ProjectVersion $mod.Project
        $builtDll = Join-Path (Split-Path -Parent $projectPath) "bin/Release/$($mod.Dll)"
        if (-not (Test-Path -LiteralPath $builtDll)) { Write-Host "   （外部模组 $($mod.Name) 缺产物，已跳过）" -ForegroundColor Yellow; continue }
        $targetDir = Join-Path $otherRoot $modVersion
        New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        Copy-Item -LiteralPath $builtDll -Destination (Join-Path $targetDir $mod.Dll) -Force
        $dllHash = (Get-FileHash (Join-Path $targetDir $mod.Dll) -Algorithm SHA256).Hash.ToLower()
        $otherLines += "$($mod.Name) $modVersion -> other/$modVersion/$($mod.Dll)  SHA256 $dllHash"
        Write-Host "   other/$modVersion/$($mod.Dll)（$($mod.Name) $modVersion）" -ForegroundColor DarkGray
    }
    if ($otherLines.Count -gt 0) {
        Set-Content -LiteralPath (Join-Path $otherRoot 'MANIFEST.txt') -Value ($otherLines -join "`r`n") -Encoding UTF8
        Write-Host "== 外部模组: dist/other/<版本号>/（清单 other/MANIFEST.txt）" -ForegroundColor Green
    }

    # ===== 5. 发布说明 =====
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    $lastTag = (git tag --sort=-v:refname | Select-Object -First 1)
    $logRange = if ($lastTag) { "$lastTag..HEAD" } else { 'HEAD' }
    $commits = @(git log --no-merges --pretty=format:'- %s' $logRange)
    if ($commits.Count -eq 0) { $commits = @('- （本次没有新的提交说明）') }
    if (-not $lastTag) {
        # 首次发布没有上一个 tag，别把整部历史全倒进 Release 页
        $total = $commits.Count
        if ($total -gt 15) {
            $commits = @($commits[0..14]) + @("- …（更早的 $($total - 15) 条改动见提交历史）")
        }
        $commits = @('首次发布，下面只列最近的提交。') + $commits
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("## DDExcNet $Tag")
    $lines.Add('')
    # 正文全部来自 docs/release-page.md（适用版本 / 安装 / 模块说明 / 修复的功能模块与核心开关），
    # 之后只追加「校验」与「授权」。标题统一降一级：它自己的 # 变成 ##，嵌在 Release 页里层级才顺。
    $pageDoc = Join-Path $Root 'docs/release-page.md'
    if (Test-Path $pageDoc) {
        $skippedTitle = $false
        $inComment = $false
        foreach ($l in (Get-Content $pageDoc)) {
            # 作者备注（<!-- … --> 注释块）不进发布页
            if ($inComment) { if ($l -match '-->') { $inComment = $false }; continue }
            if ($l -match '^\s*<!--') { if ($l -notmatch '-->') { $inComment = $true }; continue }
            if (-not $skippedTitle -and $l -match '^#\s') { $skippedTitle = $true; continue }   # 文件自带的一级标题不进发布页
            if ($l -match '^#{1,5}\s') { $lines.Add('#' + $l) } else { $lines.Add($l) }
        }
        while ($lines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($lines[$lines.Count - 1])) { $lines.RemoveAt($lines.Count - 1) }
        $lines.Add('')
    }
    if ($Notes) {
        $lines.Add('### 说明')
        $lines.Add($Notes)
        $lines.Add('')
    }

    # 校验
    $lines.Add('### 校验')
    $lines.Add('```')
    $lines.Add("SHA256(DDExcNet-$Tag.zip) = $hash")
    $lines.Add("大小 = $size KB")
    $lines.Add('```')
    $lines.Add('')

    # 授权：细节指向仓库内文件，发布页不重复法律文本
    $lines.Add('### 授权')
    $lines.Add('见仓库内 `LICENSE`（代码，MPL-2.0）、`LICENSE-ASSETS`（图标 / 名称 / 世界观文本，保留所有权利）、`NOTICE`（第三方归属）。作者：**cubstaryow**')

    $notesPath = Join-Path $dist "RELEASE-$Tag.md"
    [IO.File]::WriteAllText($notesPath, ($lines -join "`r`n"), (New-Object Text.UTF8Encoding($false)))
    Write-Host "== 发布说明: dist/RELEASE-$Tag.md"

    # ===== 6. 上传（可选） =====
    # 仓库地址：gh / REST / 手动三种方式都要用
    $remote = (git remote get-url origin) -replace '\.git$', ''
    $rm = [regex]::Match($remote, 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/]+)$')
    $repoUrl = if ($rm.Success) { "https://github.com/$($rm.Groups['owner'].Value)/$($rm.Groups['repo'].Value)" } else { $null }
    $newReleaseUrl = if ($repoUrl) {
        "$repoUrl/releases/new?tag=$Tag&title=" + [uri]::EscapeDataString("DDExcNet $Tag") + "&body=" + [uri]::EscapeDataString((Get-Content $notesPath -Raw))
    } else { $null }

    if (-not $Upload) {
        Write-Host ''
        if ($newReleaseUrl) {
            Write-Host '手动发版（GitHub 桌面版 / 网页）：' -ForegroundColor Yellow
            Write-Host "  1. 打开发布页：$repoUrl/releases/new" -ForegroundColor Yellow
            Write-Host "     tag 填 $Tag，标题填 DDExcNet $Tag" -ForegroundColor Yellow
            Write-Host "  2. 发布说明直接从 dist/RELEASE-$Tag.md 复制（或加 -OpenPage 自动复制到剪贴板并打开页面）" -ForegroundColor Yellow
            Write-Host "  3. 把 dist/DDExcNet-$Tag.zip 拖进附件框，点 Publish release" -ForegroundColor Yellow
        }
        Write-Host ''
        Write-Host '自动上传：加 -Upload（需要 gh CLI 或 $env:GITHUB_TOKEN）。' -ForegroundColor Yellow
        if ($OpenPage -and $newReleaseUrl) {
            Set-Clipboard -Value (Get-Content $notesPath -Raw)
            Write-Host '== 发布说明已复制到剪贴板，正在打开新建 Release 页面…' -ForegroundColor Cyan
            Start-Process $newReleaseUrl
        }
        return
    }

    if (-not $Target) { $Target = $branch }
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) {
        Write-Host "== 用 gh 创建 Release（target $Target）…" -ForegroundColor Cyan
        gh release create $Tag $zip --title "DDExcNet $Tag" --notes-file $notesPath --target $Target
        if ($LASTEXITCODE -ne 0) { throw "gh release create 失败（exit $LASTEXITCODE）。先确认 gh auth status 正常。" }
        Write-Host '== 已上传' -ForegroundColor Green
        return
    }

    if (-not $env:GITHUB_TOKEN) {
        throw '既没有 gh CLI，也没有 GITHUB_TOKEN。用 -OpenPage 走网页手动上传，或装 gh（winget install GitHub.cli）后 gh auth login。'
    }

    if (-not $rm.Success) { throw "解析不出 GitHub 仓库：$remote" }
    $owner = $rm.Groups['owner'].Value
    $repo = $rm.Groups['repo'].Value

    $headers = @{ Authorization = "token $env:GITHUB_TOKEN"; Accept = 'application/vnd.github+json'; 'User-Agent' = 'DDEN-release' }
    Write-Host "== 用 REST API 创建 Release（$owner/$repo，target $Target）…" -ForegroundColor Cyan
    $body = @{
        tag_name         = $Tag
        target_commitish = $Target
        name             = "DDExcNet $Tag"
        body             = (Get-Content $notesPath -Raw)
        draft            = $false
        prerelease       = $false
    } | ConvertTo-Json -Depth 4

    $rel = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$owner/$repo/releases" -Headers $headers -Body $body
    $uploadUrl = $rel.upload_url -replace '\{.*$', ''
    Write-Host '== 上传 zip…' -ForegroundColor Cyan
    Invoke-RestMethod -Method Post -Uri "$uploadUrl?name=DDExcNet-$Tag.zip" `
        -Headers @{ Authorization = "token $env:GITHUB_TOKEN"; 'Content-Type' = 'application/zip'; 'User-Agent' = 'DDEN-release' } `
        -InFile $zip | Out-Null
    Write-Host "== 已发布: $($rel.html_url)" -ForegroundColor Green
}
finally {
    Pop-Location
}
