<#
.SYNOPSIS
  把当前代码打成可发布的包（dist/DDEN-v<版本>.zip），可选直接创建 GitHub Release 并上传。

.EXAMPLE
  pwsh scripts/release.ps1
      构建 Release 并打包，不上传。产物在 dist/，会打印 SHA256。

.EXAMPLE
  pwsh scripts/release.ps1 -Upload
      打完包后创建 GitHub Release 并上传 zip（需要 gh CLI 已登录，或 GITHUB_TOKEN 环境变量）。

.EXAMPLE
  pwsh scripts/release.ps1 -Version 1.2.1 -Notes "修身上海挂贴图的问题" -Upload
      指定版本号与一段自定义说明。

.NOTES
  · 版本号默认取 Directory.Build.props 里的 <DdenVersion>，改版本只改那一行。
  · 发布包内容 = 三个 dll + 许可与说明文件；存档编辑器不打包（可选组件）。
  · 不会自动 push 分支；发版前请确认 master 已经是最新的。
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Upload,
    [string]$Notes,
    [switch]$SkipBuild,
    [string]$Target
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Push-Location $Root
try {
    # ===== 1. 版本号 =====
    if (-not $Version) {
        $props = Get-Content 'Directory.Build.props' -Raw
        $m = [regex]::Match($props, '<DdenVersion>([^<]+)</DdenVersion>')
        if (-not $m.Success) { throw 'Directory.Build.props 里没有找到 <DdenVersion>，请先补上或用 -Version 指定。' }
        $Version = $m.Groups[1].Value.Trim()
    }
    $Tag = "v$Version"
    Write-Host "== 版本: $Version（tag $Tag）" -ForegroundColor Cyan

    # ===== 2. 构建 =====
    if (-not $SkipBuild) {
        Write-Host '== 构建 Release…' -ForegroundColor Cyan
        dotnet build 'CUmod.slnx' -c Release -v m
        if ($LASTEXITCODE -ne 0) { throw "构建失败（exit $LASTEXITCODE）" }
    }

    $releaseBin = @{
        'DDEN.Shared.dll'        = 'src/DDEN.Shared/bin/Release/DDEN.Shared.dll'
        'DDExcNet-CUmodM.dll'    = 'src/DDExcNet.CUmodM/bin/Release/DDExcNet-CUmodM.dll'
        'DDEN.MPfix.dll'         = 'src/DDEN.MPfix/bin/Release/DDEN.MPfix.dll'
    }
    foreach ($pair in $releaseBin.GetEnumerator()) {
        if (-not (Test-Path $pair.Value)) { throw "缺少产物：$($pair.Value)（先构建 Release）" }
    }

    # ===== 3. 组装发布目录 =====
    $dist = Join-Path $Root 'dist'
    $stage = Join-Path $dist "DDEN-$Tag"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    foreach ($pair in $releaseBin.GetEnumerator()) {
        Copy-Item $pair.Value (Join-Path $stage $pair.Key) -Force
    }
    foreach ($doc in @('README.md', 'LICENSE', 'LICENSE-ASSETS', 'NOTICE')) {
        if (Test-Path $doc) { Copy-Item $doc $stage -Force }
    }

    # ===== 4. 打包 =====
    $zip = Join-Path $dist "DDEN-$Tag.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    $size = [math]::Round((Get-Item $zip).Length / 1KB, 1)
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    Write-Host "== 产物: dist/DDEN-$Tag.zip（$size KB）" -ForegroundColor Green
    Write-Host "   SHA256: $hash"

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
    $lines.Add("## DDEN $Tag")
    $lines.Add('')
    $lines.Add("适用：Casualties Unknown（Unity 2022.3）· BepInEx 5 · 联机框架 Together / CasualtiesMP 5.5.x")
    $lines.Add('')
    $lines.Add('### 安装')
    $lines.Add('1. 先装好 BepInEx 5；')
    $lines.Add('2. 把压缩包里的 dll **一起**放进 `BepInEx/plugins/DDEN/`（别拆开、别改名）；')
    $lines.Add('3. 联机修复需要 CUCoreLib 与 Together / CasualtiesMP，缺了也能进游戏，只是联机那部分不生效。')
    $lines.Add('')
    $lines.Add('### 本次改动')
    foreach ($c in $commits) { $lines.Add($c) }
    if ($Notes) {
        $lines.Add('')
        $lines.Add('### 说明')
        $lines.Add($Notes)
    }
    $lines.Add('')
    $lines.Add('### 校验')
    $lines.Add('```')
    $lines.Add("SHA256(DDEN-$Tag.zip) = $hash")
    $lines.Add('```')
    $lines.Add('')
    $lines.Add('授权：代码 MPL-2.0；图标、名称与世界观文本保留所有权利（见包内 LICENSE / LICENSE-ASSETS）。')

    $notesPath = Join-Path $dist "RELEASE-$Tag.md"
    [IO.File]::WriteAllText($notesPath, ($lines -join "`r`n"), (New-Object Text.UTF8Encoding($false)))
    Write-Host "== 发布说明: dist/RELEASE-$Tag.md"

    # ===== 6. 上传（可选） =====
    if (-not $Upload) {
        Write-Host ''
        Write-Host '未上传。要创建 GitHub Release，加 -Upload；或手动把 zip 拖到 Releases 页。' -ForegroundColor Yellow
        return
    }

    if (-not $Target) { $Target = $branch }
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) {
        Write-Host "== 用 gh 创建 Release（target $Target）…" -ForegroundColor Cyan
        gh release create $Tag $zip --title "DDEN $Tag" --notes-file $notesPath --target $Target
        if ($LASTEXITCODE -ne 0) { throw "gh release create 失败（exit $LASTEXITCODE）。先确认 gh auth status 正常。" }
        Write-Host '== 已上传' -ForegroundColor Green
        return
    }

    if (-not $env:GITHUB_TOKEN) {
        throw '既没有 gh CLI，也没有 GITHUB_TOKEN 环境变量。装 gh（winget install GitHub.cli）后 gh auth login，或设置 $env:GITHUB_TOKEN 再跑。'
    }

    $remote = (git remote get-url origin) -replace '\.git$', ''
    $m = [regex]::Match($remote, 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/]+)$')
    if (-not $m.Success) { throw "解析不出 GitHub 仓库：$remote" }
    $owner = $m.Groups['owner'].Value
    $repo = $m.Groups['repo'].Value

    $headers = @{ Authorization = "token $env:GITHUB_TOKEN"; Accept = 'application/vnd.github+json'; 'User-Agent' = 'DDEN-release' }
    Write-Host "== 用 REST API 创建 Release（$owner/$repo，target $Target）…" -ForegroundColor Cyan
    $body = @{
        tag_name         = $Tag
        target_commitish = $Target
        name             = "DDEN $Tag"
        body             = (Get-Content $notesPath -Raw)
        draft            = $false
        prerelease       = $false
    } | ConvertTo-Json -Depth 4

    $rel = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$owner/$repo/releases" -Headers $headers -Body $body
    $uploadUrl = $rel.upload_url -replace '\{.*$', ''
    Write-Host '== 上传 zip…' -ForegroundColor Cyan
    Invoke-RestMethod -Method Post -Uri "$uploadUrl?name=DDEN-$Tag.zip" `
        -Headers @{ Authorization = "token $env:GITHUB_TOKEN"; 'Content-Type' = 'application/zip'; 'User-Agent' = 'DDEN-release' } `
        -InFile $zip | Out-Null
    Write-Host "== 已发布: $($rel.html_url)" -ForegroundColor Green
}
finally {
    Pop-Location
}
