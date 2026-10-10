[CmdletBinding()]
param(
    [string] $DotNet = 'dotnet',
    [switch] $Portable
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Snapline\Snapline.csproj'
$buildRoot = Join-Path $repoRoot '.build'
if ($DotNet -eq 'dotnet') {
    $DotNet = (Get-Command dotnet -ErrorAction Stop).Source
} else {
    $DotNet = (Resolve-Path -LiteralPath $DotNet -ErrorAction Stop).Path
}
$vars = @('DOTNET_ROOT','DOTNET_CLI_HOME','DOTNET_CLI_TELEMETRY_OPTOUT','DOTNET_NOLOGO',
    'DOTNET_ADD_GLOBAL_TOOLS_TO_PATH','DOTNET_GENERATE_ASPNET_CERTIFICATE',
    'NUGET_PACKAGES','NUGET_HTTP_CACHE_PATH','NUGET_PLUGINS_CACHE_PATH','APPDATA','LOCALAPPDATA')
$old = @{}
foreach ($name in $vars) { $old[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
Push-Location $repoRoot
try {
    $env:DOTNET_ROOT = Split-Path -Parent $DotNet
    $env:DOTNET_CLI_HOME = Join-Path $buildRoot 'dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $buildRoot 'nuget-packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $buildRoot 'nuget-http-cache'
    $env:NUGET_PLUGINS_CACHE_PATH = Join-Path $buildRoot 'nuget-plugins-cache'
    $env:APPDATA = Join-Path $buildRoot 'profile\Roaming'
    $env:LOCALAPPDATA = Join-Path $buildRoot 'profile\Local'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    New-Item -ItemType Directory -Force -Path $env:APPDATA,$env:LOCALAPPDATA | Out-Null
    & $DotNet --version
    if ($LASTEXITCODE -ne 0) { throw 'The pinned .NET 8.0.425 SDK is required.' }
    $restore = @('restore',$project,'--configfile',(Join-Path $PSScriptRoot 'NuGet.Config'))
    if ($Portable) { $restore += @('-r','win-x64','-p:SelfContained=true','-p:RuntimeFrameworkVersion=8.0.31') }
    & $DotNet @restore
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    if (-not $Portable) {
        & $DotNet build $project -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Source build failed.' }
        return
    }
    $version = '1.8.5'
    $out = Join-Path $repoRoot "dist\AirPeg-$version-win-x64"
    if (Test-Path -LiteralPath $out) { throw 'Output folder already exists. Choose a fresh clone/build directory to preserve earlier artifacts.' }
    & $DotNet publish $project -c Release -r win-x64 --no-restore --self-contained true -o $out '-p:RuntimeFrameworkVersion=8.0.31' '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:EnableCompressionInSingleFile=true' '-p:DebugType=none' '-p:DebugSymbols=false'
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    Move-Item -LiteralPath (Join-Path $out 'Snapline.exe') -Destination (Join-Path $out 'AirPeg.exe')
    Set-Content -LiteralPath (Join-Path $out 'portable.flag') -Value 'Store settings and captures in the adjacent Data folder.' -Encoding utf8NoBOM
    Copy-Item -LiteralPath (Join-Path $repoRoot 'licenses') -Destination $out -Recurse
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE'),(Join-Path $repoRoot 'NOTICE.md') -Destination $out
    @'
轻挂 AirPeg 1.8.5 — Preview
Double-click AirPeg.exe. Windows x64 self-contained; no .NET Desktop Runtime installation required.
Keep portable.flag next to the EXE. Settings/screenshots/history are written to adjacent Data.
Exit old versions and back up Data before upgrading. Simplified Chinese is default; English is available in Settings.
This is a preview pending final stability/publication review. See NOTICE.md and licenses for credits.
'@ | Set-Content -LiteralPath (Join-Path $out 'README.txt') -Encoding utf8NoBOM
    $zip = Join-Path $repoRoot "dist\AirPeg-$version-win-x64.zip"
    if (Test-Path -LiteralPath $zip) { throw 'Archive already exists; previous artifact preserved.' }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
    Get-FileHash -LiteralPath $zip -Algorithm SHA256
} finally {
    Pop-Location
    foreach ($name in $vars) { [Environment]::SetEnvironmentVariable($name,$old[$name],'Process') }
}
