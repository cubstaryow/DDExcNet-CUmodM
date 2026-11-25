param([string]$GameDir = 'L:\SteamLibrary\steamapps\common\Casualties Unknown')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outputPath = Join-Path $PSScriptRoot 'bin/NetFx'
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$dotnetPath = (Get-Command dotnet).Source
$sdkVersion = (& $dotnetPath --version).Trim()
$sdkRoot = Join-Path (Split-Path $dotnetPath) "sdk/$sdkVersion"
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$core = Join-Path $GameDir 'BepInEx/core'
$arguments = @('/nologo','/noconfig','/langversion:10','/optimize+','/deterministic+','/target:exe',
    "/reference:$framework/mscorlib.dll", "/reference:$framework/System.dll", "/reference:$framework/System.Core.dll",
    "/reference:$sdkRoot/TestHostNetFramework/netstandard.dll", "/reference:$sdkRoot/TestHostNetFramework/System.Runtime.dll",
    "/reference:$core/0Harmony.dll", "/out:$outputPath/ChineseHookTests.exe")
& $dotnetPath (Join-Path $sdkRoot 'Roslyn/bincore/csc.dll') @arguments (Join-Path $PSScriptRoot 'Program.cs') (Join-Path $repoRoot 'src/DDEN.MPfix/TogetherChineseRepair.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($dependency in @('0Harmony.dll','Mono.Cecil.dll','MonoMod.RuntimeDetour.dll','MonoMod.Utils.dll')) { Copy-Item -LiteralPath (Join-Path $core $dependency) -Destination $outputPath -Force }
& (Join-Path $outputPath 'ChineseHookTests.exe')
exit $LASTEXITCODE
