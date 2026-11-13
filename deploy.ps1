# Builds Release and copies the plugin into SPT. Remove by deleting BepInEx\plugins\FFloridaMann-RealSoundSimulation.
# -Dev builds the developer version (every setting in F12); without it, the public version.
param([string]$SptDir = "G:\SPT", [switch]$Dev)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
dotnet build "$root\src\RealSoundSimulation\RealSoundSimulation.csproj" -c Release -p:DevBuild=$($Dev.IsPresent.ToString().ToLower())
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$out = "$root\src\RealSoundSimulation\bin\Release\netstandard2.1"
$dest = Join-Path $SptDir "BepInEx\plugins\FFloridaMann-RealSoundSimulation"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$out\RealSoundSimulation.dll", "$out\RealSoundSimulation.Dsp.dll" $dest -Force
Write-Host "Deployed to $dest"
