param([string]$GameDir = 'L:\SteamLibrary\steamapps\common\Casualties Unknown')
$ErrorActionPreference = 'Stop'
# The game's HarmonyX expects Exception.PrepForRemoting, absent in CoreCLR. Use Windows .NET Framework.
# Compile owned mock methods with Roslyn; do not start Unity or load any game assembly.
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outputPath = Join-Path $PSScriptRoot 'bin/NetFx'
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$dotnetPath = (Get-Command dotnet).Source
$sdkVersion = (& $dotnetPath --version).Trim()
$sdkRoot = Join-Path (Split-Path $dotnetPath) "sdk/$sdkVersion"
$compiler = Join-Path $sdkRoot 'Roslyn/bincore/csc.dll'
$facades = Join-Path $sdkRoot 'TestHostNetFramework'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$core = Join-Path $GameDir 'BepInEx/core'
$common = @('/nologo', '/noconfig', '/langversion:10', '/optimize+', '/debug:portable', '/deterministic+',
    "/reference:$framework/mscorlib.dll", "/reference:$framework/System.dll", "/reference:$framework/System.Core.dll",
    "/reference:$facades/netstandard.dll", "/reference:$facades/System.Runtime.dll")
$executable = Join-Path $outputPath 'LoongNovaSpatialTests.exe'
$sources = @((Join-Path $repoRoot 'src/DDEN.LoongNova/Integration/NetworkReadBudget.cs'), (Join-Path $PSScriptRoot 'Fixture.cs'), (Join-Path $PSScriptRoot 'Program.cs'),
    (Join-Path $repoRoot 'src/DDEN.LoongNova/Spatial/SpatialPowerState.cs'),
    (Join-Path $repoRoot 'src/DDEN.LoongNova/Spatial/SpatialChargeRules.cs'),
    (Join-Path $repoRoot 'src/DDEN.LoongNova/Spatial/MicroSpaceCrate.cs'),
    (Join-Path $repoRoot 'src/DDEN.LoongNova/Spatial/SpatialDecayGuard.cs'),
    (Join-Path $repoRoot 'src/DDEN.LoongNova/Spatial/SpatialPatches.cs'),
    (Join-Path $repoRoot 'src/DDEN.LoongNova/Spatial/PodSalvage.cs'),
    (Join-Path $repoRoot 'src/DDEN.LoongNova/Spatial/CapsuleMembraneSource.cs'),
    (Join-Path $repoRoot 'src/DDEN.LoongNova/Equipment/EnergyState.cs'),
    (Join-Path $repoRoot 'src/DDEN.LoongNova/Equipment/DragonizationRules.cs'),
    (Join-Path $repoRoot 'src/DDEN.LoongNova/Integration/PendingActions.cs'),
    (Join-Path $repoRoot 'src/DDEN.LoongNova/Supplies/DrillExchange.cs'),
    (Join-Path $repoRoot 'src/DDEN.LoongNova/Supplies/DrillPodSource.cs'))
& $dotnetPath $compiler @common '/target:exe' "/out:$executable" "/reference:$core/0Harmony.dll" "/reference:$facades/Newtonsoft.Json.dll" @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($dependency in @('0Harmony.dll', 'Mono.Cecil.dll', 'MonoMod.RuntimeDetour.dll', 'MonoMod.Utils.dll'))
{ Copy-Item -LiteralPath (Join-Path $core $dependency) -Destination (Join-Path $outputPath $dependency) -Force }
# Unity's JSON build fails CLR strong-name verification; use the SDK's signed NetFx JSON fixture dependency.
Copy-Item -LiteralPath (Join-Path $facades 'Newtonsoft.Json.dll') -Destination (Join-Path $outputPath 'Newtonsoft.Json.dll') -Force
foreach ($facade in @('netstandard.dll', 'System.Runtime.dll'))
{ Copy-Item -LiteralPath (Join-Path $facades $facade) -Destination (Join-Path $outputPath $facade) -Force }
[IO.File]::WriteAllText("$executable.config", '<configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>')
& $executable
exit $LASTEXITCODE
