@echo off
rem Stark HUD - avvia l'installer grafico (Windows PowerShell 5.1, nessun privilegio admin richiesto)
cd /d "%~dp0"
start "" "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%~dp0StarkUI.ps1"
