# ❄ / 🍂 WinterTray - Szezonális Asztali Hóesés és Őszi Falevelek Windows 11-re

A **WinterTray** egy könnyű, energiatakarékos, hardveresen gyorsított Windows 11 asztali alkalmazás, amely valósághű téli hóesést és őszi falevélhullást varázsol a képernyődre. A hópelyhek és falevelek nemcsak hullanak a háttérben, hanem **valós időben ráfekszenek és megülnek a nyitott ablakok tetején és a tálcán**, ablakmozgatáskor együtt mozognak az ablakkal, és az egérkurzorral lesöpörhetőek vagy elhessegethetőek!

---

## 🌟 Főbb funkciók

- **Szezonválasztó (Tél ❄ / Ősz 🍂):** Egyetlen kattintással válthatsz a csendes téli hóesés és a meleg őszi falevelek hullása között a tálca menüből vagy a Beállítások ablakból.
- **Őszi falevél-szimuláció:** Különböző színű (narancssárga, aranybarna, karmazsinvörös, alkonyati) és formájú (juhar, tölgy, nyír) levelek hullanak a képernyő tetejéről természetes szinuszos ringatózással és finom forgással.
- **Ablak-ütközés:** A Win32 EnumWindows és GetWindowRect hívásokkal figyeli a látható ablakok felső szélét (Top). Ha egy levél eléri az ablak felső lécét, megáll és ráfekszik a felületre!
- **Interakció (GetCursorPos):** Ha a felhasználó a kurzort a megült falevelek fölé viszi, a levél lendületet kapva lerepül az ablakról és esik tovább, pont mint a hópelyhek lesöprésekor!
- **Megült levelek élettartama:** A felületen megpihent levelek 10 másodperc után finoman eltűnnek (fade out), hogy a memória és a képernyő mindig tiszta és dinamikus maradjon.
- **Ablak-hófelhalmozódás (Window Snow Caps):** Téli módban a hópelyhek automatikusan megtelepednek bármely ablak felső szegélyén, organikus, puha hódunyhát képezve.
- **Lavina-effektus (Avalanche Effect):** Amikor egy ablakot elmozdítasz vagy bezársz, a rajta felhalmozódott hó vagy falevelek lezúdulnak / lehullanak.
- **Szezonális háttérkép:** Téli módban a `wallpaper.jpeg`, őszi módban a `wallpaper-fall.jpeg` automatikus alkalmazása, kilépéskor az eredeti háttérkép hibátlan visszaállításával.
- **Átklikkelhető (Click-Through Overlay):** Az átlátszó réteg nem fogja el az egérkattintásokat (`WS_EX_TRANSPARENT`), így a munkát és a játékot semennyire sem zavarja.
- **Többmonitoros támogatás:** A kiterjesztett asztal teljes felületén folyamatos a működés.
- **Tálca ikon (System Tray):** Értesítési területről elérhető gyors évszakváltás, beállítások, letakarítás, szüneteltetés és kilépés.

---

## ⚙️ Releváns beállítások

A beállítások ablak a tálca ikonra duplán kattintva, vagy a jobb gombos menüből a **Beállítások...** opcióval nyitható meg:

| Beállítás | Leírás | Alapértelmezett érték |
| :--- | :--- | :--- |
| **Hópelyhek stílusa** | Választható: Vegyes (Kristályok és Bolyhos), Csak lágy bolyhos hógolyók, vagy Részletes forgó jégkristályok. | **Vegyes** |
| **Hópelyhek száma (Flake Count)** | Szabályozza a képernyőn egyszerre szálló pelyhek sűrűségét (30-tól egészen 500 darabig). | **200 db** |
| **Esési sebesség (Fall Speed)** | A gravitáció és a hullás sebessége (0.3x nyugodt szállingózástól 2.5x viharos tempóig). | **1.0x** |
| **Szélerősség (Wind Drift)** | Vízszintes szélirány és természetes légmozgás (balra vagy jobbra fújó szél). | **+0.25** |
| **Hópehely mérete (Flake Size)** | A pelyhek méretének szórása (mélységélességet keltve porhó, kristályok és előtéri bokeh között). | **2.3 – 6.0 px** |
| **Egérmutató szél erősség** | A kurzor által keltett légörvény finom alaperőssége (0.2x – 3.0x). | **1.0x** |
| **Egérmozgás sebesség-érzékenység** | Minél gyorsabban húzod az egeret, annál erősebb légáramlatot kelt (0.0x – 3.0x). | **1.0x** |
| **Kurzor hatótávolsága** | A légörvény hatótávolsága az egérmutató körül (40 – 250 px). | **90 px** |
| **Hó lesöprése egérrel** | Ha a kurzort végighúzod a felgyűlt hóhalmon, az egérrel fizikailag lesöpörheted a havat (hódarabok és hópor röppen fel). | **Bekapcsolva** |
| **Hó megmaradása lebegő ablakokon** | A hó megtelepszik a lebegő ablakok felső szegélyén. (A teljes méretű ablakok és a képernyő teteje mentes marad). | **Bekapcsolva** |
| **Hó ráhajlása a fejléc élére** | A hóréteg nem vágódik le egyenes vonalban, hanem 2-4 px lágy, hullámos peremmel ráhajlik az ablak címsorára. | **Bekapcsolva** |
| **Jégcsapok az ablakok alján** | Valósághű, csillogó jégcsapok képződnek a lebegő ablakok alsó szegélyén természetes klaszterekben. | **Bekapcsolva** |
| **Felhalmozódás gyorsasága** | A hóréteg felépülésének természetes sebessége (0.05x – 2.0x). | **0.20x** |
| **Hó megmaradása a tálcán** | Ha ki van kapcsolva, a hó a Windows tálca alatt tűnik el természetesen. | **Kikapcsolva** |
| **Hóréteg max. vastagsága** | Meghatározza, milyen vastag hódunyha épülhet fel maximálisan a lebegő ablakok tetején (6 – 48 px). | **22 px** |
| **Olvadási időtartam (Melt Rate)** | Időzített természetes elpárolgás/olvadás másodpercben (0 = nem olvad el). | **0 (soha)** |
| **Tehetetlenség és lavina ablak mozgásakor** | Lassú mozgatásnál a hó együtt utazik az ablakkal; gyors rántásnál a tehetetlenség miatt darabokban hullik le és lecsordul a sarkokon. Bezáráskor a teljes réteg lezúdul. | **Bekapcsolva** |
| **Képfrissítés (FPS)** | Választható: Monitor Natív (VSync / 120+ FPS), 120 FPS (Szuper sima), 60 FPS (Alapértelmezett), 30 FPS (Kímélő mód). | **Monitor Natív (VSync / 120+ FPS)** |
| **Effektek minősége** | Részecskék és lavina animációk intenzitása: Teljes / Ultra (120 FPS sima részecskék), Közepes (kiegyensúlyozott), Kikapcsolva (gyengébb gépekre). | **Teljes / Ultra** |
| **Szünet, ha takarva van** | Ha egy teljes méretű ablak eltakarja a képernyőt, az alkalmazás szünetelteti a rajzolást (nulla erőforrás). | **Bekapcsolva** |
| **Téli háttérkép** | A mellékelt téli háttérkép beállítása a futás alatt, kilépéskor az eredeti háttérkép automatikus visszaállításával. | **Bekapcsolva** |
| **Indulás a Windowszal** | Automatikus háttérben indulás a felhasználó bejelentkezésekor (Registry `Run` kulcs). | **Bekapcsolva** |

A beállítások automatikusan mentésre kerülnek a `%APPDATA%\WinterTray\config.json` fájlba.

---

## 💻 Rendszerkövetelmények (System Requirements)

### Minimális rendszerkövetelmény:
- **Operációs rendszer:** Windows 11 (bármely kiadás: Home, Pro, Enterprise, LTSC - 64-bit) vagy Windows 10 (1809 vagy újabb)
- **Processzor:** Kétmagos x64 processzor (Intel Core 2 Duo, AMD Athlon X2 vagy újabb, min. 1.8 GHz)
- **Memória (RAM):** 2 GB rendszermemória
- **Videokártya:** DirectX 9 / WDDM 1.0 kompatibilis integrált vagy dedikált grafikus vezérlő (hardveres WPF gyorsítás)
- **Tárhely:** 50 MB szabad lemezterület
- **Szoftver:** .NET 8.0 Desktop Runtime (a mellékelt telepítőcsomag ezt igény esetén automatikusan kezeli, vagy a Windows 11 telepítés során hozzáadható)

### Ajánlott konfiguráció:
- **Operációs rendszer:** Windows 11 22H2 / 23H2 / 24H2 (64-bit)
- **Processzor:** Négymagos modern x64 processzor (Intel Core i3/i5/i7 8. generáció vagy újabb, AMD Ryzen 3000 vagy újabb)
- **Memória (RAM):** 4 GB vagy több rendszermemória
- **Videokártya:** DirectX 11+ kompatibilis GPU (Intel UHD/Iris Xe, AMD Radeon, NVIDIA GeForce)
- **Kijelző:** Bármilyen felbontás (Full HD, 2K, 4K, többmonitoros rendszer, Per-Monitor V2 DPI támogatással)

### Erőforrás-felhasználási mutatók mérések alapján:
- **Processzor (CPU) terhelés:** ~0.3% – 1.2% modern i5/Ryzen processzoron
- **Memória (RAM) használat:** ~25 MB – 40 MB
- **Grafikus vezérlő (GPU):** < 1% terhelés (Direct3D gyorsított vektoros kirajzolás)
- **Akkumulátorkímélő:** Teljes képernyős játékok alatt a terhelés azonnal **0.0%**-ra esik!

---

## 📦 Telepítő készítése (Inno Setup)

A projekt egy dedikált **`Installer`** mappát tartalmaz, amelyben megtalálható az Inno Setup telepítő szkript és a hozzá tartozó segédfájlok:
- **`Installer\WinterTray.iss`**: Az Inno Setup 7 telepítő definíció.
- **`Installer\Build-Installer.cmd`**: Egykattintásos fordító parancsfájl, amely automatikusan lefordítja a Release buildet (`App_Release`), majd létrehozza a végleges telepítőt:
  - Kimeneti fájl: **`Output\WinterTray-Setup-v1.1.exe`**
- **`Installer\install.cmd`**: Csendes telepítő helyi használathoz (`/silent` kapcsolóval).
- **`Installer\uninstall.cmd`**: Tiszta eltávolító szkript.

---

## 💿 Beépítés a Windows 11 telepítőfájlba (ISO / USB telepítő)

A WinterTray könnyen integrálható a Windows 11 telepítő médiába:

### 1. Módszer: USB telepítőre másolás ($OEM$ szabvány)

1. Készíts egy bootolható Windows 11 telepítő pendrive-ot a szokásos módon (pl. Rufus vagy a Microsoft hivatalos Media Creation Tool használatával).
2. Nyisd meg a pendrive gyökerében lévő `sources` mappát (`E:\sources\`).
3. Hozd létre a következő mappastruktúrát, és másold be az `App_Release` tartalmát:
   - `E:\sources\$OEM$\$1\Program Files\WinterTray\` (ide másold az `App_Release` fájljait és az `Installer\install.cmd`-t)
   - `E:\sources\$OEM$\$$\Setup\Scripts\SetupComplete.cmd` (ide másold az `Installer\SetupComplete.cmd` fájlt)
4. **Kész!** A Windows 11 telepítése után a rendszer automatikusan bemásolja a programot és beállítja az automatikus indítást.

---

### 2. Módszer: Beépítés NTLite vagy MSMG Toolkit használatával

Ha NTLite-tal készítesz testreszabott / optimalizált Windows 11 ISO-t:
1. Töltsd be a Windows 11 képmást az NTLite-ba.
2. Lépj a **Post-Setup** (Telepítés utáni műveletek) fülre.
3. Kattints a **Hozzáadás (Add) -> Fájl** gombra, és válaszd ki az `App_Release\WinterTray.exe`-t vagy az `Installer\install.cmd`-t `/silent` paraméterrel.
4. Generáld le az új ISO fájlt.

---

### 3. Módszer: Válaszfájl (`autounattend.xml`) használata

Ha teljesen automatizált (unattended) Windows 11 telepítőt használsz, a mellékelt `Installer\autounattend_sample.xml` alapján másold be a következő blokkot a saját `autounattend.xml` fájlodba:

```xml
<settings pass="oobeSystem">
    <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
        <FirstLogonCommands>
            <SynchronousCommand wcm:action="add">
                <Order>1</Order>
                <CommandLine>cmd /c "%ProgramFiles%\WinterTray\install.cmd /silent"</CommandLine>
                <Description>WinterTray Csendes Telepites</Description>
            </SynchronousCommand>
        </FirstLogonCommands>
    </component>
</settings>
```

---

## 🚀 Helyi tesztelés és futtatás a jelenlegi gépen

Ha a jelenlegi Windows rendszeren szeretnéd kipróbálni:
1. Indítás: Nyisd meg az `App_Release\WinterTray.exe` fájlt.
2. A tálcán megjelenik a hópehely ikon, a képernyőn elindul a hóesés, és megtelepszik a nyitott ablakok tetején.
3. Csendes telepítés a rendszerbe: Futtasd rendszergazdaként az `Installer\install.cmd` parancsfájlt.
4. Eltávolítás: Futtasd az `Installer\uninstall.cmd` fájlt.
