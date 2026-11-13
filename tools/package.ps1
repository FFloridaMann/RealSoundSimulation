# Builds the public release and packs the download zip for the Forge, laid out to extract straight into the SPT folder.
# The room route files come from Sound Engine Backport (shipped with red_the_fox's permission); by default they are taken
# from the installed plugin folder.
param(
    [string]$SptDir = "G:\SPT",
    [string]$RoutesDir = "",
    [string]$OutDir = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $RoutesDir) { $RoutesDir = Join-Path $SptDir "BepInEx\plugins\FFloridaMann-RealSoundSimulation\seb_routes" }
if (-not $OutDir) { $OutDir = Join-Path $root "dist" }

dotnet build "$root\src\RealSoundSimulation\RealSoundSimulation.csproj" -c Release -p:DevBuild=false
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$bin = "$root\src\RealSoundSimulation\bin\Release\netstandard2.1"
$version = (Select-String -Path "$root\src\RealSoundSimulation\Plugin.cs" -Pattern 'const string Version = "([^"]+)"').Matches[0].Groups[1].Value

$stage = Join-Path $OutDir "stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$plugin = Join-Path $stage "BepInEx\plugins\FFloridaMann-RealSoundSimulation"
New-Item -ItemType Directory -Force $plugin | Out-Null
Copy-Item "$bin\RealSoundSimulation.dll", "$bin\RealSoundSimulation.Dsp.dll" $plugin
Copy-Item "$root\LICENSE", "$root\THIRD_PARTY_NOTICES.md" $plugin
if (-not (Test-Path (Join-Path $RoutesDir "AudioBakeData"))) { throw "route files not found in $RoutesDir" }
Copy-Item -Recurse $RoutesDir (Join-Path $plugin "seb_routes")

$zip = Join-Path $OutDir "RealSoundSimulation-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
$sevenZip = "C:\Program Files\7-Zip\7z.exe"
Push-Location $stage
try {
    if (Test-Path $sevenZip) { & $sevenZip a -tzip -mx=9 $zip "BepInEx" | Out-Null; if ($LASTEXITCODE -ne 0) { throw "7-Zip failed" } }
    else { Compress-Archive -Path "BepInEx" -DestinationPath $zip }
}
finally { Pop-Location }
Remove-Item -Recurse -Force $stage
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host "Packed $zip ($([math]::Round((Get-Item $zip).Length / 1MB)) MB)"
Write-Host "SHA256 $hash"
