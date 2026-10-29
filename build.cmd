@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo .NET Framework C# compiler was not found.
    exit /b 1
)
if not exist "%~dp0dist" mkdir "%~dp0dist"
if not exist "%~dp0dist" exit /b 1
"%CSC%" /nologo /codepage:65001 /target:exe /platform:anycpu /optimize+ /out:"%~dp0dist\MultisimRetryLauncher.exe" "%~dp0src\MultisimRetryLauncher.cs"
if errorlevel 1 exit /b 1
echo Built dist\MultisimRetryLauncher.exe
exit /b 0
