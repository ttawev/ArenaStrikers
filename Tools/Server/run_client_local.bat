@echo off
chcp 65001 >nul
rem Копия игры, которая ищет матч на сервере этого компьютера (run_server_local.bat), а не на нашем VPS.
cd /d "%~dp0..\..\Builds\Win"
for %%f in (*.exe) do (
  if /I not "%%~nf"=="UnityCrashHandler64" ( start "" "%%f" -matchserver 127.0.0.1 & goto :eof )
)
echo Не нашёл сборку в Builds\Win — сначала соберите Windows-версию.
pause
