@echo off
setlocal
set "ROOT=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%CSC%" (
  echo Cannot find csc.exe at "%CSC%"
  exit /b 1
)

"%CSC%" /nologo /target:winexe ^
  /out:"%ROOT%DesktopBox.exe" ^
  /win32icon:"%ROOT%src\icon.ico" ^
  /win32manifest:"%ROOT%src\app.manifest" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.VisualBasic.dll ^
  "%ROOT%src\DesktopBox.cs"

if errorlevel 1 exit /b 1
echo Built %ROOT%DesktopBox.exe
