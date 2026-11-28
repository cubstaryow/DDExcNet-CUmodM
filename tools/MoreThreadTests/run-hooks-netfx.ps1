param([string]$GameDir = 'L:\SteamLibrary\steamapps\common\Casualties Unknown')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outputPath = Join-Path $PSScriptRoot 'bin/HookNetFx'
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$dotnetPath = (Get-Command dotnet).Source
$sdkVersion = (& $dotnetPath --version).Trim()
$sdkRoot = Join-Path (Split-Path $dotnetPath) "sdk/$sdkVersion"
$compiler = Join-Path $sdkRoot 'Roslyn/bincore/csc.dll'
$facades = Join-Path $sdkRoot 'TestHostNetFramework'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$core = Join-Path $GameDir 'BepInEx/core'
$common = @('/nologo', '/noconfig', '/langversion:10', '/optimize+', '/debug:portable',
    "/reference:$framework/mscorlib.dll", "/reference:$framework/System.dll", "/reference:$framework/System.Core.dll",
    "/reference:$facades/netstandard.dll", "/reference:$facades/System.Runtime.dll")
$steam = Join-Path $outputPath 'com.rlabrecque.steamworks.net.dll'
# The installed Steamworks binary targets netstandard2.1, which NetFx cannot load.
# Use owned signature-identical managed fixtures; the real binary is checked separately with Cecil.
& $dotnetPath $compiler @common '/target:library' "/out:$steam" (Join-Path $PSScriptRoot 'HookFixture/SteamFixture.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$sources = @((Join-Path $PSScriptRoot 'HookFixture/Program.cs'), (Join-Path $repoRoot 'src/DDEN.MoreThreadOnMP/Integration/TransportHooks.cs'))
$sources += Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/DDEN.MoreThreadOnMP/Core') -Filter '*.cs' | ForEach-Object FullName
$executable = Join-Path $outputPath 'MoreThreadHookTests.exe'
& $dotnetPath $compiler @common '/target:exe' "/out:$executable" "/reference:$core/0Harmony.dll" "/reference:$steam" @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($dependency in @('0Harmony.dll', 'Mono.Cecil.dll', 'MonoMod.RuntimeDetour.dll', 'MonoMod.Utils.dll'))
{ Copy-Item -LiteralPath (Join-Path $core $dependency) -Destination (Join-Path $outputPath $dependency) -Force }
foreach ($facade in @('netstandard.dll', 'System.Runtime.dll'))
{ Copy-Item -LiteralPath (Join-Path $facades $facade) -Destination (Join-Path $outputPath $facade) -Force }
[IO.File]::WriteAllText("$executable.config", '<configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>')
& $executable
exit $LASTEXITCODE
