@echo off
:: ====================================================================
:: Windows 11 Post-Installation Hook (SetupComplete.cmd)
:: Helye az ISO-ban: \sources\$OEM$\$$\Setup\Scripts\SetupComplete.cmd
:: ====================================================================

set "APP_DIR=%ProgramFiles%\WinterTray"

:: 1. Automatikus indítás bejegyzése az első bejelentkezéskor
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "WinterTray" /t REG_SZ /d "\"%APP_DIR%\WinterTray.exe\"" /f

:: 2. Start Menü parancsikon létrehozása minden felhasználónak
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('%ProgramData%\Microsoft\Windows\Start Menu\Programs\WinterTray.lnk'); $s.TargetPath = '%APP_DIR%\WinterTray.exe'; $s.WorkingDirectory = '%APP_DIR%'; $s.IconLocation = '%APP_DIR%\Resources\app.ico,0'; $s.Description = 'WinterTray - Teli Hoeses'; $s.Save()"

exit /b 0
