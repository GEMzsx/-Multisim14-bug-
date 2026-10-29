@echo off
setlocal
set "ROOT=%~dp0.."
call "%ROOT%\build.cmd"
if errorlevel 1 exit /b 1
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /out:"%ROOT%\dist\LauncherProbe.exe" "%~dp0LauncherProbe.cs"
if errorlevel 1 exit /b 1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Verify-Launcher.ps1"
exit /b %errorlevel%
