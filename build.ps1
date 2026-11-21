param(
    [switch]$NoShortcut
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) {
    throw "找不到 csc.exe：$csc（需要 Windows 自带的 .NET Framework 4.8）"
}

& $csc /nologo /target:winexe `
    /out:"$root\DesktopBox.exe" `
    /win32icon:"$root\src\icon.ico" `
    /win32manifest:"$root\src\app.manifest" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "$root\src\DesktopBox.cs"

if ($LASTEXITCODE -ne 0) { throw "编译失败" }

if (-not $NoShortcut) {
    $ws = New-Object -ComObject WScript.Shell
    $sc = $ws.CreateShortcut("$root\桌面应用.lnk")
    $sc.TargetPath = "$root\DesktopBox.exe"
    $sc.WorkingDirectory = $root
    $sc.IconLocation = "$root\DesktopBox.exe,0"
    $sc.Description = "桌面应用盒子"
    $sc.Save()
}

Write-Host "已生成：$root\DesktopBox.exe"
