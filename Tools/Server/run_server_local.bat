@echo off
chcp 65001 >nul
rem Проверка сервера на этом компьютере: запускает обычную Windows-сборку в режиме сервера (без окна игры).
rem Потом: в редакторе Inspector -> Arena -> Test Server = 127.0.0.1, а вторая копия игры — через run_client_local.bat; в обеих «Найти матч».
rem На Windows игра не пишет журнал в консоль, поэтому журнал идёт в файл server.log, а это окно показывает строки сервера.
set "LOG=%~dp0server.log"
cd /d "%~dp0..\..\Builds\Win"
for %%f in (*.exe) do (
  if /I not "%%~nf"=="UnityCrashHandler64" (
    if exist "%LOG%" del "%LOG%"
    echo Запускаю %%f как сервер на порту 7777.
    echo Остановить сервер — Ctrl+C в этом окне.
    echo.
    start "" /B "%%f" -batchmode -nographics -server -port 7777 -logFile "%LOG%"
    powershell -NoProfile -Command "try { while (!(Test-Path '%LOG%')) { Start-Sleep -Milliseconds 200 }; Get-Content -Path '%LOG%' -Wait -Encoding UTF8 | Where-Object { $_ -match '\[Arena\]|Exception' } } finally { Stop-Process -Name '%%~nf' -ErrorAction SilentlyContinue; Write-Host 'Сервер остановлен.' }"
    goto :eof
  )
)
echo Не нашёл сборку в Builds\Win — сначала соберите Windows-версию.
pause
