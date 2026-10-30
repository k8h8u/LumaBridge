# DriverSwitcher

Трей-утилита для быстрого переключения драйверов лазерных плат **BJJCZ**
(`USB\VID_9588`, PID `9980 / 9899 / 9990 / 9999`) между двумя режимами:

- **EzCAD2** — штатный драйвер `Lmcv2u.sys` (Beijing JCZ);
- **LightBurn** — `WinUSB` (то, что обычно ставят через Zadig).

Zadig больше не нужен: переключение делается штатными средствами Windows
(`UpdateDriverForPlugAndPlayDevices` + перезапуск устройства), драйверы
зашиты внутрь exe.

## Использование

1. Запустить `DriverSwitcher.exe` (запросит права администратора).
2. В трее появится цветной кружок:
   - зелёный — режим EzCAD2;
   - синий — режим LightBurn;
   - серый — плата не найдена;
   - оранжевый — режим не определён / идёт переключение.
3. Правый клик по иконке → «Режим EzCAD2» или «Режим LightBurn».

Всё необходимое (драйвер EzCAD2, подписанный пакет WinUSB, сертификат)
находится внутри exe, внешних файлов не требуется.

## Как это работает

- Плата определяется по Hardware ID через SetupAPI, текущий режим — по имени
  сервиса устройства (`Lmcv2u`/`LMCUSB` → EzCAD2, `WinUSB` → LightBurn).
- Пакет WinUSB (`drivers/winusb/BJJCZ_WinUSB.inf` + подписанный каталог)
  использует in-box драйвер Microsoft WinUSB (`Include=winusb.inf`).
- Каталог WinUSB подписан самоподписанным сертификатом `DriverSwitcher`;
  при первом переключении в режим LightBurn сертификат добавляется в
  LocalMachine\Root и LocalMachine\TrustedPublisher (модель Zadig/libwdi),
  поэтому установка проходит без предупреждений.

## Сборка

Требуется .NET 8 SDK:

```powershell
dotnet publish src\DriverSwitcher -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -o publish
```

Пересборка подписанного каталога WinUSB (нужен закрытый ключ PFX):

```powershell
scripts\New-SigningCert.ps1      # один раз: создать сертификат и PFX
scripts\Build-WinUsbPackage.ps1  # makecat + signtool -> drivers\winusb\BJJCZ_WinUSB.cat
```
