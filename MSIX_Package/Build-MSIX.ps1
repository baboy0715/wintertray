<#
.SYNOPSIS
    WinterTray MSIX Csomagoló Szkript (Store-hoz és helyi teszteléshez)
.DESCRIPTION
    1. Lefordítja a WinterTray projektet Release módban (dotnet publish)
    2. Frissíti az Assets PNG ikonokat az app.ico alapján
    3. Összeállítja a PackageRoot mappát és a Manifestet
    4. Legyártja az MSIX csomagot a makeappx.exe segítségével
    5. Opcionálisan önaláíró tanúsítvánnyal aláírja helyi teszteléshez
#>

param(
    [switch]$SignForLocalTest = $false,
    [string]$PackageVersion = "1.0.0.0"
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir
$PackageRoot = Join-Path $ScriptDir "PackageRoot"
$OutputDir = Join-Path $ScriptDir "Output"
$AssetsDir = Join-Path $ScriptDir "Assets"
$ManifestTemplate = Join-Path $ScriptDir "AppxManifest.xml"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "   WinterTray - MSIX Csomag Készítő Szkript      " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

# 1. Keresés: makeappx.exe és signtool.exe
$MakeAppx = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\makeappx.exe"
if (-not (Test-Path $MakeAppx)) {
    $found = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*x64*" } | Select-Object -First 1
    if ($found) { $MakeAppx = $found.FullName } else { throw "makeappx.exe nem található a Windows SDK-ban!" }
}

$SignTool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
if (-not (Test-Path $SignTool)) {
    $foundSign = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*x64*" } | Select-Object -First 1
    if ($foundSign) { $SignTool = $foundSign.FullName }
}

# 2. WinterTray lefordítása (dotnet publish)
Write-Host "[1/5] WinterTray .NET Release fordítása..." -ForegroundColor Yellow
$PublishDir = Join-Path $ProjectDir "publish"
dotnet publish "$ProjectDir\WinterTray.csproj" -c Release -r win-x64 --self-contained false -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish sikertelen volt!" }

# 3. PackageRoot előkészítése
Write-Host "[2/5] PackageRoot mappa összeállítása..." -ForegroundColor Yellow
if (Test-Path $PackageRoot) { Remove-Item $PackageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $PackageRoot -Force | Out-Null
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

# Fájlok másolása
Copy-Item "$PublishDir\*" -Destination $PackageRoot -Recurse -Force
Remove-Item (Join-Path $PackageRoot "*.old"), (Join-Path $PackageRoot "*.pdb") -ErrorAction SilentlyContinue

# Assets és Manifest másolása
Copy-Item $AssetsDir -Destination $PackageRoot -Recurse -Force
Copy-Item $ManifestTemplate -Destination (Join-Path $PackageRoot "AppxManifest.xml") -Force

# Verziószám frissítése a manifestben, ha megadták
if ($PackageVersion -ne "1.0.0.0") {
    $manifestContent = Get-Content (Join-Path $PackageRoot "AppxManifest.xml") -Raw
    $manifestContent = $manifestContent -replace 'Version="[0-9\.]+"', ('Version="' + $PackageVersion + '"')
    Set-Content (Join-Path $PackageRoot "AppxManifest.xml") $manifestContent -Encoding UTF8
}

# 4. MSIX Csomagolás (makeappx)
$MsixFile = Join-Path $OutputDir "WinterTray_$($PackageVersion)_x64.msix"
Write-Host "[3/5] MSIX csomag összeállítása: $MsixFile" -ForegroundColor Yellow
& $MakeAppx pack /d $PackageRoot /p $MsixFile /o

if ($LASTEXITCODE -ne 0) { throw "makeappx pack sikertelen!" }

Write-Host ">> MSIX csomag sikeresen elkészült!" -ForegroundColor Green

# 5. Helyi teszteléshez való aláírás (opcionális)
if ($SignForLocalTest) {
    Write-Host "[4/5] Önaláíró tanúsítvány ellenőrzése / létrehozása..." -ForegroundColor Yellow
    $certSubject = "CN=WinterTrayDev"
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1

    if (-not $cert) {
        Write-Host "Új teszt tanúsítvány létrehozása ($certSubject)..." -ForegroundColor Cyan
        $cert = New-SelfSignedCertificate -Type Custom -Subject $certSubject `
            -KeyUsage DigitalSignature -FriendlyName "WinterTray Local Test" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
        
        # Másoljuk át a Trusted People tárba, hogy a Windows elfogadja
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store "TrustedPeople", "CurrentUser"
        $store.Open("ReadWrite")
        $store.Add($cert)
        $store.Close()
        Write-Host "Tanúsítvány hozzáadva a CurrentUser\TrustedPeople tárolóhoz." -ForegroundColor Green
    }

    Write-Host "[5/5] MSIX aláírása a teszt tanúsítvánnyal..." -ForegroundColor Yellow
    & $SignTool sign /fd SHA256 /sha1 $cert.Thumbprint /v $MsixFile
    if ($LASTEXITCODE -eq 0) {
        Write-Host ">> A csomag sikeresen aláírva! Most már duplakattintással közvetlenül telepítheted ezen a gépen teszteléshez." -ForegroundColor Green
    } else {
        Write-Warning "Aláírás sikertelen. Ellenőrizd a signtool kimenetet."
    }
} else {
    Write-Host "[Infó] Store feltöltéshez nem szükséges helyi aláírás, a Microsoft Store a beküldéskor írja alá." -ForegroundColor Cyan
}

Write-Host "`nKÉSZ! Csomag elérési útja:" -ForegroundColor Green
Write-Host "$MsixFile" -ForegroundColor White
