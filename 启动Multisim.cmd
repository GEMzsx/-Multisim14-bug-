@echo off
chcp 65001 >nul
if not exist "%~dp0dist\MultisimRetryLauncher.exe" (
  echo 启动器尚未编译，请先运行 build.cmd。
  pause
  exit /b 1
)
"%~dp0dist\MultisimRetryLauncher.exe" %*
set "launcher_result=%errorlevel%"
if not "%launcher_result%"=="0" (
  echo.
  echo 启动器已停止。详细原因见上方文字及 dist\logs 文件夹。
  pause
)
exit /b %launcher_result%
