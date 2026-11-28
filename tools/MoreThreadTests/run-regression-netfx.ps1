param([string]$GameDir = 'L:\SteamLibrary\steamapps\common\Casualties Unknown')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outputPath = Join-Path $PSScriptRoot 'bin/RegressionNetFx'
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$dotnetPath = (Get-Command dotnet).Source
$sdkVersion = (& $dotnetPath --version).Trim()
$sdkRoot = Join-Path (Split-Path $dotnetPath) "sdk/$sdkVersion"
$compiler = Join-Path $sdkRoot 'Roslyn/bincore/csc.dll'
$facades = Join-Path $sdkRoot 'TestHostNetFramework'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$core = Join-Path $GameDir 'BepInEx/core'
$arguments = @('/nologo', '/noconfig', '/langversion:10', '/optimize+', '/debug:portable', '/define:REGRESSION_STANDALONE',
    "/reference:$framework/mscorlib.dll", "/reference:$framework/System.dll", "/reference:$framework/System.Core.dll",
    "/reference:$facades/netstandard.dll", "/reference:$facades/System.Runtime.dll", "/reference:$core/0Harmony.dll", "/reference:$core/Mono.Cecil.dll")
$sources = @((Join-Path $PSScriptRoot 'HookFixture/RegressionFixture.cs'),
    (Join-Path $PSScriptRoot 'RegressionContractTests.cs'),
    (Join-Path $repoRoot 'src/DDEN.MoreThreadOnMP/Integration/RegressionValidationHooks.cs'))
$executable = Join-Path $outputPath 'MoreThreadRegressionTests.exe'
& $dotnetPath $compiler @arguments '/target:exe' "/out:$executable" @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($dependency in @('0Harmony.dll', 'Mono.Cecil.dll', 'MonoMod.RuntimeDetour.dll', 'MonoMod.Utils.dll'))
{ Copy-Item -LiteralPath (Join-Path $core $dependency) -Destination (Join-Path $outputPath $dependency) -Force }
foreach ($facade in @('netstandard.dll', 'System.Runtime.dll'))
{ Copy-Item -LiteralPath (Join-Path $facades $facade) -Destination (Join-Path $outputPath $facade) -Force }
[IO.File]::WriteAllText("$executable.config", '<configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>')
& $executable $GameDir
exit $LASTEXITCODE
