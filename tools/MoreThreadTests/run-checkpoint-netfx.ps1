param([string]$GameDir = 'L:\SteamLibrary\steamapps\common\Casualties Unknown')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outputPath = Join-Path $PSScriptRoot 'bin/CheckpointNetFx'
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$dotnetPath = (Get-Command dotnet).Source
$sdkVersion = (& $dotnetPath --version).Trim()
$sdkRoot = Join-Path (Split-Path $dotnetPath) "sdk/$sdkVersion"
$compiler = Join-Path $sdkRoot 'Roslyn/bincore/csc.dll'
$facades = Join-Path $sdkRoot 'TestHostNetFramework'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$core = Join-Path $GameDir 'BepInEx/core'
# Unity's repacked Json.NET fails CLR strong-name validation. Use the SDK's signed
# .NET Framework copy for owned fixtures; real game/provider assemblies are metadata-only.
$json = Join-Path $facades 'Newtonsoft.Json.dll'
$arguments = @('/nologo', '/noconfig', '/langversion:10', '/optimize+', '/debug:portable', '/define:CHECKPOINT_STANDALONE',
    "/reference:$framework/mscorlib.dll", "/reference:$framework/System.dll", "/reference:$framework/System.Core.dll",
    "/reference:$facades/netstandard.dll", "/reference:$facades/System.Runtime.dll", "/reference:$core/0Harmony.dll",
    "/reference:$core/Mono.Cecil.dll", "/reference:$json")
$sources = @((Join-Path $PSScriptRoot 'HookFixture/CheckpointFixture.cs'),
    (Join-Path $PSScriptRoot 'HookFixture/CheckpointAdditionalFixture.cs'),
    (Join-Path $PSScriptRoot 'CheckpointContractTests.cs'),
    (Join-Path $PSScriptRoot 'CheckpointWriterTests.cs'),
    (Join-Path $repoRoot 'src/DDEN.MPreducer/Core/CheckpointWriter.cs'),
    (Join-Path $repoRoot 'src/DDEN.MPreducer/Integration/CheckpointBindings.cs'),
    (Join-Path $repoRoot 'src/DDEN.MPreducer/Integration/CheckpointCapture.cs'),
    (Join-Path $repoRoot 'src/DDEN.MPreducer/Integration/CheckpointHooks.cs'),
    (Join-Path $repoRoot 'src/DDEN.MPreducer/Integration/CheckpointIntervalHooks.cs'))
$executable = Join-Path $outputPath 'MoreThreadCheckpointTests.exe'
& $dotnetPath $compiler @arguments '/target:exe' "/out:$executable" @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($dependency in @('0Harmony.dll', 'Mono.Cecil.dll', 'MonoMod.RuntimeDetour.dll', 'MonoMod.Utils.dll'))
{ Copy-Item -LiteralPath (Join-Path $core $dependency) -Destination (Join-Path $outputPath $dependency) -Force }
Copy-Item -LiteralPath $json -Destination (Join-Path $outputPath 'Newtonsoft.Json.dll') -Force
foreach ($facade in @('netstandard.dll', 'System.Runtime.dll'))
{ Copy-Item -LiteralPath (Join-Path $facades $facade) -Destination (Join-Path $outputPath $facade) -Force }
[IO.File]::WriteAllText("$executable.config", '<configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>')
& $executable $GameDir
exit $LASTEXITCODE
