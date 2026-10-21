@echo off
rem Stark HUD - ripristino d'emergenza da riga di comando (riporta Windows com'era prima)
title Stark HUD - Ripristino
cd /d "%~dp0"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0StarkUI.ps1" -Restore
echo.
pause
