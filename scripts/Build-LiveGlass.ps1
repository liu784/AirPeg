[CmdletBinding()]
param(
    [string] $BuildToolsRoot = $env:AIRPEG_BUILD_TOOLS_ROOT,
    [string] $WindowsSdkRoot = $env:AIRPEG_WINDOWS_SDK_ROOT,
    [string] $WindowsSdkVersion = $env:AIRPEG_WINDOWS_SDK_VERSION,
    [switch] $Force
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$nativeRoot = Join-Path $repoRoot 'native\LiveGlass'
$buildRoot = Join-Path $nativeRoot 'build'
$dll = Join-Path $buildRoot 'Tendedero.LiveGlass.dll'
$inputs = @((Join-Path $nativeRoot 'LiveGlass.cpp'), (Join-Path $nativeRoot 'LiveGlass.hlsl'), $PSCommandPath, (Join-Path $nativeRoot 'CompareBackground.hlsl'))
foreach ($inputPath in $inputs) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) { throw "Missing original native input: $inputPath" }
}
if (-not $Force -and (Test-Path -LiteralPath $dll -PathType Leaf)) {
    $newest = ($inputs | Get-Item | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
    if ((Get-Item -LiteralPath $dll).LastWriteTimeUtc -gt $newest) {
        Write-Output "LiveGlass native output is current: $dll"
        return
    }
}
if (-not $BuildToolsRoot) {
    $vswhere = Join-Path "${env:ProgramFiles(x86)}" 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'Existing vswhere not found. Set AIRPEG_BUILD_TOOLS_ROOT to an installed Visual Studio / Build Tools directory.'
    }
    $BuildToolsRoot = (& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
    if (-not $BuildToolsRoot) { throw 'Existing x64 C++ build tools not found; no installation is performed.' }
}
if (-not $WindowsSdkRoot) {
    $WindowsSdkRoot = Join-Path "${env:ProgramFiles(x86)}" 'Windows Kits\10'
}
if (-not $WindowsSdkVersion) {
    $WindowsSdkVersion = Get-ChildItem -LiteralPath (Join-Path $WindowsSdkRoot 'Lib') -Directory |
        Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and [version]$_.Name -ge [version]'10.0.19041.0' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'um\x64\d3d11.lib')) -and (Test-Path -LiteralPath (Join-Path $WindowsSdkRoot "Include\$($_.Name)\um\d3d11.h")) } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 -ExpandProperty Name
    if (-not $WindowsSdkVersion) { throw 'Existing compatible Windows SDK not found; no installation is performed.' }
}
$vcRoot = Join-Path $BuildToolsRoot 'VC\Tools\MSVC'
$vcVersion = Get-ChildItem -LiteralPath $vcRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
if (-not $vcVersion) { throw "No existing MSVC toolset at $vcRoot. Installation is not performed." }
$compiler = Join-Path $vcVersion.FullName 'bin\Hostx64\x64\cl.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw "No existing x64 C++ compiler: $compiler" }
$includeRoots = @(
    (Join-Path $vcVersion.FullName 'include'),
    (Join-Path $WindowsSdkRoot "Include\$WindowsSdkVersion\um"),
    (Join-Path $WindowsSdkRoot "Include\$WindowsSdkVersion\shared"),
    (Join-Path $WindowsSdkRoot "Include\$WindowsSdkVersion\ucrt"),
    (Join-Path $WindowsSdkRoot "Include\$WindowsSdkVersion\winrt")
)
$libraryRoots = @(
    (Join-Path $vcVersion.FullName 'lib\x64'),
    (Join-Path $WindowsSdkRoot "Lib\$WindowsSdkVersion\um\x64"),
    (Join-Path $WindowsSdkRoot "Lib\$WindowsSdkVersion\ucrt\x64")
)
foreach ($directory in ($includeRoots + $libraryRoots)) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Required installed SDK directory absent: $directory" }
}
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
$shader = Get-Content -LiteralPath $inputs[1] -Raw
if ($shader.Contains(')LIVEGLASS"')) { throw 'Shader contains reserved C++ raw-string terminator.' }
$comparison = Get-Content -LiteralPath $inputs[3] -Raw
$shaderHeader = 'static const char kComparisonSource[] = R"LIVEGLASS(' + $comparison + ')LIVEGLASS";' + [Environment]::NewLine + 'static const char kShaderSource[] = R"LIVEGLASS(' + $shader + ')LIVEGLASS";'
Set-Content -LiteralPath (Join-Path $buildRoot 'ShaderSource.h') -Value $shaderHeader -Encoding utf8NoBOM
$evidenceRoot = Join-Path $buildRoot 'logs'
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
$logPath = Join-Path $evidenceRoot ('native-build-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
$arguments = @('/nologo', '/LD', '/MT', '/O2', '/W4', '/EHsc', '/std:c++17', '/utf-8', '/D_WIN32_WINNT=0x0A00',
    "/I$buildRoot", "/Fo$(Join-Path $buildRoot 'LiveGlass.obj')", "/Fe$dll")
$arguments += $includeRoots | ForEach-Object { "/I$_" }
$arguments += $inputs[0]
$arguments += @('/link', '/NOLOGO', '/MACHINE:X64', "/IMPLIB:$(Join-Path $buildRoot 'Tendedero.LiveGlass.lib')",
    "/PDB:$(Join-Path $buildRoot 'Tendedero.LiveGlass.pdb')")
$arguments += $libraryRoots | ForEach-Object { "/LIBPATH:$_" }
$arguments += @('d3d11.lib','dxgi.lib','d3d9.lib','d3dcompiler.lib','user32.lib','gdi32.lib')
"Compiler: $compiler", "SDK: $WindowsSdkVersion", 'All environment options are invocation-local; no global PATH or configuration is changed.' |
    Set-Content -LiteralPath $logPath -Encoding utf8NoBOM
& $compiler @arguments 2>&1 | Tee-Object -FilePath $logPath -Append -Encoding utf8
$nativeExit = $LASTEXITCODE
if ($nativeExit -ne 0) { throw "Native helper build failed with exit $nativeExit. Log: $logPath" }
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "Compiler did not produce $dll" }
$summary = [ordered]@{
    time = (Get-Date).ToString('o')
    compiler = $compiler
    toolset = $vcVersion.Name
    sdkVersion = $WindowsSdkVersion
    dll = $dll
    bytes = (Get-Item -LiteralPath $dll).Length
    sha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant()
    sourceSha256 = (Get-FileHash -LiteralPath $inputs[0] -Algorithm SHA256).Hash.ToLowerInvariant()
    shaderSha256 = (Get-FileHash -LiteralPath $inputs[1] -Algorithm SHA256).Hash.ToLowerInvariant()
    log = $logPath
    systemConfigurationChanged = $false
    compiledOnly = $true
}
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceRoot 'native-build-latest.json') -Encoding utf8NoBOM
Write-Output "Built original live GPU helper: $dll"
