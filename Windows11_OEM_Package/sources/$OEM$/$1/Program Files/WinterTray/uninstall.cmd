@echo off
setlocal enabledelayedexpansion

:: ====================================================================
:: WinterTray - Eltávolító (Uninstaller)
:: ====================================================================

title WinterTray Eltavolito

set "TARGET_DIR=%ProgramFiles%\WinterTray"
set "SHORTCUT_PATH=%ProgramData%\Microsoft\Windows\Start Menu\Programs\WinterTray.lnk"

echo [*] WinterTray leallitasa es eltavolitasa...

taskkill /f /im WinterTray.exe >nul 2>&1
timeout /t 1 /nobreak >nul 2>&1

reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "WinterTray" /f >nul 2>&1
reg delete "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "WinterTray" /f >nul 2>&1

if exist "!SHORTCUT_PATH!" del /f /q "!SHORTCUT_PATH!" >nul 2>&1
if exist "!TARGET_DIR!" rd /s /q "!TARGET_DIR!" >nul 2>&1

echo [OK] WinterTray sikeresen eltavolitva a rendszerbol.
timeout /t 2 /nobreak >nul 2>&1
exit /b 0
