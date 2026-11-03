# Microsoft Store Feltöltési Útmutató (MSIX)

Ez az útmutató bemutatja, hogyan tudod a generált MSIX csomagot feltölteni és publikálni a **Microsoft Partner Center** fiókodban.

---

## 1. A kész fájlok helye

- **MSIX csomagoló mappa:** `C:\projektekű\WinterTray\MSIX_Package`
- **Kész Store MSIX fájl:** `C:\projektekű\WinterTray\MSIX_Package\Output\WinterTray_1.0.0.0_x64.msix`
- **Manifest:** `C:\projektekű\WinterTray\MSIX_Package\AppxManifest.xml`
- **Build parancsfájl:** `C:\projektekű\WinterTray\MSIX_Package\Build-MSIX.ps1`

---

## 2. Lépések a Partner Centerben

### 1. lépés: Alkalmazásnév lefoglalása
1. Nyisd meg a [Microsoft Partner Centert](https://partner.microsoft.com/dashboard).
2. Menj az **Apps & Games** (Alkalmazások és játékok) menüpontra.
3. Kattints a **New product** -> **MSIX or PWA app** gombra.
4. Add meg a kívánt nevet (pl. `WinterTray`) és foglald le (**Reserve product name**).

### 2. lépés: A csomag azonosítóinak (Product Identity) lekérése
A Microsoft Store megköveteli, hogy az `AppxManifest.xml`-ben lévő azonosítók pontosan megegyezzenek a Partner Center által generált azonosítókkal:
1. A Partner Centerben kattints a lefoglalt alkalmazásodra.
2. A bal oldali menüben kattints a **Product management** -> **Product identity** (Termékkezelés -> Termékazonosító) menüpontra.
3. Itt találsz 3 fontos értéket:
   - **Package/Identity/Name** (pl. `12345YourName.WinterTray`)
   - **Package/Identity/Publisher** (pl. `CN=01234567-89AB-CDEF-0123-456789ABCDEF`)
   - **Package/Properties/PublisherDisplayName** (a te neved vagy kiadói neved)

### 3. lépés: Az azonosítók beírása az `AppxManifest.xml`-be
Nyisd meg a `C:\projektekű\WinterTray\MSIX_Package\AppxManifest.xml` fájlt, és a Partner Centerből kimásolt értékekkel írd át a fejlécet:

```xml
  <Identity
    Name="IDE_JÖN_A_PARTNER_CENTER_NAME"
    Publisher="CN=IDE_JÖN_A_PARTNER_CENTER_PUBLISHER_ID"
    Version="1.0.0.0"
    ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>WinterTray</DisplayName>
    <PublisherDisplayName>IDE_JÖN_A_PUBLISHER_DISPLAY_NAME</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
```

### 4. lépés: Újragenerálás
Futtasd le a PowerShell szkriptet az azonosítók frissítése után:
```powershell
powershell -ExecutionPolicy Bypass -File C:\projektekű\WinterTray\MSIX_Package\Build-MSIX.ps1
```
*(Ha növelni akarod a verziószámot: `-PackageVersion "1.0.1.0"`)*

### 5. lépés: Csomag feltöltése
1. A Partner Centerben kattints a **Start your submission** (Beküldés indítása) gombra.
2. A **Packages** (Csomagok) szekcióban húzd be a `WinterTray_1.0.0.0_x64.msix` fájlt.
3. Töltsd ki a Store Listing adatokat:
   - **Leírás:** Rövid összefoglaló a WinterTray-ről (hóesés, tálcaikon, háttérkép animáció).
   - **Képernyőképek:** Legalább 1 kép a működésről.
   - **Adatkezelési nyilatkozat URL (Privacy Policy):** Egy tetszőleges URL (pl. a GitHub repód linkje), amely leírja, hogy az app nem gyűjt adatokat.
   - **Árazás:** Ingyenes (Free).
   - **Korhatár besorolás:** Rövid kérdőív kitöltése.
4. Kattints a **Submit to the Store** gombra!

---

## 3. Helyi tesztelés (Saját gépen még a feltöltés előtt)

Ha a saját gépeden szeretnéd kipróbálni az MSIX telepítést duplakattintással, a Windows megköveteli az aláírást:
```powershell
powershell -ExecutionPolicy Bypass -File C:\projektekű\WinterTray\MSIX_Package\Build-MSIX.ps1 -SignForLocalTest
```
Ez a parancs automatikusan készít egy helyi teszt tanúsítványt, beteszi a Megbízható személyek közé, és aláírja az MSIX-et, így azonnal duplakattintással telepíthető és tesztelhető!
