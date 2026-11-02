@echo off
setlocal enabledelayedexpansion

:: ====================================================================
:: WinterTray - Csendes Telepítő (Silent Installer for Windows 11)
:: ====================================================================

title WinterTray Telepito

set "TARGET_DIR=%ProgramFiles%\WinterTray"
set "SOURCE_DIR=%~dp0..\App_Release"

if not exist "!SOURCE_DIR!\WinterTray.exe" (
    set "SOURCE_DIR=%~dp0..\publish"
)
if not exist "!SOURCE_DIR!\WinterTray.exe" (
    set "SOURCE_DIR=%~dp0"
)

echo [*] WinterTray telepitese folyamatban...
echo [*] Celkonyvtar: !TARGET_DIR!

:: 1. Futó folyamat leállítása
taskkill /f /im WinterTray.exe >nul 2>&1

:: 2. Célkönyvtár létrehozása és fájlok másolása
if not exist "!TARGET_DIR!" mkdir "!TARGET_DIR!"
xcopy /y /e /i /q "!SOURCE_DIR!\*" "!TARGET_DIR!\" >nul 2>&1

:: 3. Indítóparancs bejegyzése a Registrybe (Minden felhasználó számára)
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "WinterTray" /t REG_SZ /d "\"!TARGET_DIR!\WinterTray.exe\"" /f >nul 2>&1
reg add "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "WinterTray" /t REG_SZ /d "\"!TARGET_DIR!\WinterTray.exe\"" /f >nul 2>&1

:: 4. Start menü parancsikon létrehozása PowerShell segítségével
set "SHORTCUT_PATH=%ProgramData%\Microsoft\Windows\Start Menu\Programs\WinterTray.lnk"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('!SHORTCUT_PATH!'); $s.TargetPath = '!TARGET_DIR!\WinterTray.exe'; $s.WorkingDirectory = '!TARGET_DIR!'; $s.IconLocation = '!TARGET_DIR!\Resources\app.ico,0'; $s.Description = 'WinterTray - Teli Hoeses Asztali Alkalmazas'; $s.Save()" >nul 2>&1

echo [OK] WinterTray sikeresen telepitve!

:: Ha nincs csendes kapcsoló, indítsuk el
if /i not "%~1"=="/silent" (
    if /i not "%~1"=="-s" (
        start "" "!TARGET_DIR!\WinterTray.exe"
    )
)

exit /b 0
