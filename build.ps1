# Tests, then publishes a self-contained single CafeWiFi.exe and zips it.
#   ./build.ps1                                   → build/CafeWiFi-win-x64.zip (version 0.1.0, updater off)
#   ./build.ps1 -Version 1.2.3 -Repo owner/name   → what CI does for a release
param(
    [string]$Version = "0.1.0",
    [string]$Repo = ""
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet test --nologo
if ($LASTEXITCODE -ne 0) { throw "tests failed" }

$out = "build/publish"
Remove-Item -Recurse -Force build -ErrorAction SilentlyContinue
dotnet publish src/CafeWiFi -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Version=$Version -p:GitHubRepo=$Repo -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Compress-Archive -Path "$out/CafeWiFi.exe" -DestinationPath build/CafeWiFi-win-x64.zip -Force
$exe = Get-Item "$out/CafeWiFi.exe"
$zip = Get-Item build/CafeWiFi-win-x64.zip
"CafeWiFi.exe  {0:N1} MB" -f ($exe.Length / 1MB)
"zip           {0:N1} MB  ->  {1}" -f ($zip.Length / 1MB), $zip.FullName
