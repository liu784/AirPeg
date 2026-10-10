# 从源码构建 / Building AirPeg

## 已有工具要求

- Windows x64，PowerShell 7（`pwsh`）
- .NET **8.0.425 SDK**（本仓库的 global.json 固定此版本）
- Visual Studio / Build Tools 的 C++ x64 工具集（MSVC）及 Windows 10/11 SDK；Windows SDK 至少 10.0.19041
- Git，供克隆与源码版本记录使用

构建脚本只查找已有工具，不安装工具、不申请管理员权限、不修改系统 PATH 或注册表。终端应能找到 PowerShell 7。若 dotnet 不在 PATH，可用完整路径传入；该路径仅用于此次构建。

## 干净克隆

```powershell
git clone https://github.com/liu784/AirPeg.git
Set-Location AirPeg
pwsh -NoProfile -File .\scripts\Build-AirPeg.ps1
```

公开仓库可直接克隆。普通构建输出位于 `src/Snapline/bin/Release/net8.0-windows10.0.19041.0/`，内部程序集仍叫 Snapline，这是兼容标识；正式便携入口叫 AirPeg.exe。

使用项目本地 SDK：

```powershell
pwsh -NoProfile -File .\scripts\Build-AirPeg.ps1 -DotNet .\.tools\dotnet8\dotnet.exe
```

脚本将还原缓存及命令行配置写入仓库内被忽略的 `.build`，不读取用户的私有 NuGet 源；NuGet.Config 只指定官方 nuget.org。所有环境覆盖都只作用于当前进程与子进程，并在脚本结束时恢复。

## 自包含便携版

```powershell
pwsh -NoProfile -File .\scripts\Build-AirPeg.ps1 -Portable
```

输出：`dist/AirPeg-1.8.5-win-x64/AirPeg.exe` 和 `dist/AirPeg-1.8.5-win-x64.zip`。包内包含 portable.flag、运行说明及第三方许可证，不包含用户 Data、PDB、SDK 或测试截图。发布包使用 .NET Desktop Runtime 8.0.31，版本更换时必须同时检查其许可证。

## 原生玻璃桥接

`scripts/Build-LiveGlass.ps1` 在 WPF 构建前执行，用现有 MSVC 与 SDK 编译 LiveGlass.cpp。HLSL 从源码嵌入 DLL，并由系统 d3dcompiler_47.dll 在运行时编译。不下载 Shader 或专有二进制组件。

脚本使用 Visual Studio 自带的 vswhere 自动查找安装目录，并选择已有 Windows SDK。若环境需要手动指定：

```powershell
$env:AIRPEG_BUILD_TOOLS_ROOT = '<existing Visual Studio installation>'
$env:AIRPEG_WINDOWS_SDK_ROOT = '<existing Windows Kits/10 directory>'
$env:AIRPEG_WINDOWS_SDK_VERSION = '10.0.26100.0'
pwsh -NoProfile -File .\scripts\Build-AirPeg.ps1
```

这些是可选的进程级变量。缺少工具时构建明确失败，不自动安装。native/LiveGlass/build 的输出和日志均被忽略。

## WPF 控件 Shader

ChromeGlass.ps 是由相邻的原始 ChromeGlass.hlsl 编译出的资源，不是第三方闭源 Shader。通常无需重新生成；修改源文件后，可使用已有 Windows Python 运行 `scripts/compile-glass-shader.py`。它调用系统 D3DCompile，不安装工具。

## 验证边界

干净克隆构建验证与实体桌面功能验收是不同证据。构建成功不等于 Windows 11、不同 DPI、所有显卡或全部鼠标交互已通过。预构建 1.8.5 EXE 来自 SOURCE-PROVENANCE.md 记录的开发源码检查点；发布快照提交包含同字节产品源码及通用构建脚本，Git 提交后缀因此可能不同。
