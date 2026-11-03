# Обновить сервер на VPS одной командой (после «Arena → Собрать сервер (Linux)»).
# Запуск из PowerShell в папке проекта:   .\Tools\Server\deploy.ps1
# По умолчанию — сервер ICLOUD.KG (185.138.184.154, SSH на порту 10182). Другой сервер: -Ip адрес -Port порт.
# Спросит пароль root (дважды: загрузка и запуск) — или настройте вход по ключу (README, шаг «Вход без пароля»).
# Игроки должны обновить игру до той же версии.
param([string]$Ip = "185.138.184.154", [int]$Port = 10182, [string]$User = "root")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$build = Join-Path $root "Builds\Server"
if (-not (Test-Path (Join-Path $build "ArenaServer.x86_64"))) { Write-Host "Нет сборки сервера в Builds\Server — сначала Arena -> Собрать сервер (Linux)"; exit 1 }
Write-Host "Останавливаю сервер и загружаю сборку..."
ssh -p $Port "$User@$Ip" "systemctl stop arena 2>/dev/null; mkdir -p /home/arena/server"
$files = Get-ChildItem $build | Where-Object { $_.Name -notlike "*BackUpThisFolder*" } | ForEach-Object { $_.FullName } # отладочные файлы Unity на сервер не нужны
scp -P $Port -r $files "${User}@${Ip}:/home/arena/server/"
scp -P $Port (Join-Path $PSScriptRoot "arena.service") "${User}@${Ip}:/etc/systemd/system/arena.service"
Write-Host "Запускаю..."
ssh -p $Port "$User@$Ip" "chown -R arena:arena /home/arena/server && chmod +x /home/arena/server/ArenaServer.x86_64 && systemctl daemon-reload && systemctl enable --now arena && systemctl restart arena && sleep 3 && systemctl --no-pager status arena | head -6"
Write-Host ""
Write-Host "Готово. Игрокам: Онлайн -> Сервер: 77.235.18.42:10184"
Write-Host "Журнал сервера:  ssh -p $Port $User@$Ip journalctl -u arena -f"
