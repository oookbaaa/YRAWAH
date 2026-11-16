



<p align="center"><strong>Battery for your wireless mouse, headset, controller and keyboard, in the Windows tray.</strong></p>

<p align="center">
  <a href="https://github.com/oookbaaa/YRAWAH/releases/latest"><img src="https://img.shields.io/github/v/release/oookbaaa/YRAWAH?color=FFC21F&labelColor=1D1B18" alt="Latest release"></a>
  <a href="https://github.com/oookbaaa/YRAWAH/releases"><img src="https://img.shields.io/github/downloads/oookbaaa/YRAWAH/total?color=FFC21F&labelColor=1D1B18" alt="Downloads"></a>
  <img src="https://img.shields.io/badge/Windows-11-FFC21F?labelColor=1D1B18" alt="Windows 11">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-AGPL--3.0-FFC21F?labelColor=1D1B18" alt="License: AGPL-3.0"></a>
</p>

<p align="center"><img src="docs/images/bandeira-us.png" alt="" height="14"> <strong>English</strong> · <a href="README.pt-BR.md"><img src="docs/images/bandeira-br.png" alt="" height="14"> Português</a></p>

<p align="center">
  <a href="https://github.com/oookbaaa/YRAWAH/releases/latest"><img src="https://img.shields.io/badge/Download_for_Windows-FFC21F?style=for-the-badge" alt="Download for Windows" height="40"></a>
</p>

<p align="center"><img src="docs/images/yrawah.png" alt="YRAWAH panel and edge widget, dark theme" width="85%"></p>

YRAWAH is a small Windows 11 app that sits in the tray and shows how much
battery your wireless devices have left. It is free, open source and in
**beta**.

## What you get

- **Panel**: click the tray icon to see every connected device, weakest first.
- **Edge widget**: battery rings on the edge of the screen. It hides itself and
  gets out of the way of full-screen apps.
- **Tray icon**: shows at a glance whether anything is running low; hover to
  see each device's battery.
- **Connect notice**: when a device turns on, YRAWAH shows its battery, on the
  widget or as a Windows notification, with sound if you want.
- **Low battery alert**, with a limit you choose.
- **English and Portuguese**: follows the Windows language, and you can switch
  in Settings.
- **Summary after updating**: how many devices YRAWAH tracked and how many
  low-battery warnings it gave, counted only on your PC, plus what changed in
  the version.

## Download

Get the portable zip from the
[Releases page](https://github.com/oookbaaa/YRAWAH/releases), unzip it and run `YRAWAH.exe`.

- No administrator rights and no installation needed.
- The app is not digitally signed, so Windows SmartScreen will warn you.
  Click **More info**, then **Run anyway**.
- YRAWAH does not update itself and makes no network requests.

## Supported devices

| Device | Connection | Status |
|---|---|---|
| Attack Shark R1 | dongle | ✅ checked |
| Rapoo MT760 Mini | dongle | ✅ checked |
| DualSense | cable | ✅ checked |
| Xbox controllers | adapter and cable | ✅ checked |
| 8BitDo Ultimate 2C | cable | ✅ checked |
| Any Bluetooth device whose battery Windows already shows in *Settings > Bluetooth* | Bluetooth | ✅ checked |
| Logitech, Razer, SteelSeries, HyperX and Corsair: mice, keyboards and headsets | dongle | 🧪 not tested yet |
| About 20 other brands: ATK, VXE, VGN, Pulsar, Dareu, AJAZZ, Lamzu, WLmouse, G-Wolves, Hitscan, K-Snake, Bytech, Motospeed, Keychron, Incott and more Attack Shark models | dongle | 🧪 not tested yet |

🧪 means written from open protocol documentation and not yet tested on real
hardware. If you own one, your report is what confirms it. Full list:
[docs/supported-devices.md](docs/supported-devices.md).

## My device is not listed

Open **Settings > Send my device** inside YRAWAH. It listens for 30 seconds
while you use the device, shows you what it found, removes personal data
(serial number, Bluetooth address, PC name, user name) and saves a `.txt`.
You then send that file yourself, as a
[GitHub issue](https://github.com/oookbaaa/YRAWAH/issues/new/choose) or by
[e-mail](#contact). Nothing leaves your PC on its own.

Want to add the device yourself? See
[docs/adding-a-device.md](docs/adding-a-device.md).

<details>
<summary><strong>What YRAWAH does to your devices</strong></summary>

- It reads when you open the app, when you sign in to Windows, when you click
  **Refresh** and when you plug in or turn on a device (only that device, at
  most once every 10 seconds). It never polls in a loop.
- For most devices it only **listens** to what the dongle or cable already
  sends, or reads the number Windows itself stored (Bluetooth).
- For some brands on a dongle (Logitech, Razer, SteelSeries, HyperX, Corsair
  and part of the others) it **asks** the device for its battery, only after
  you allow that brand: the same
  question open projects such as Solaar, OpenRazer and HeadsetControl send,
  once per reading. It never sends a command that changes a setting. The
  **Listen only** switch in Settings turns off every question.
- It never shows a number it did not read. No data is "no data", never "0%".
- It sends nothing anywhere. The only network access is the update check on
  GitHub Releases.

Details: [SECURITY.md](SECURITY.md).

</details>

<details>
<summary><strong>Build from source</strong></summary>

You need Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build src/BatteryTray -c Debug
```

```bash
dotnet run --project src/BatteryTray -- --demo
```

`--demo` shows sample devices without touching any hardware. More in
[CONTRIBUTING.md](CONTRIBUTING.md) and [docs/architecture.md](docs/architecture.md).

</details>


## Contact

- Maintainer: OKBA ([@oookbaaa](https://github.com/oookbaaa))
- Questions and problems: [open an issue](https://github.com/oookbaaa/YRAWAH/issues)

## License

[AGPL-3.0](LICENSE). Third-party licenses and the open projects whose protocol
documentation made YRAWAH possible are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

YRAWAH is not affiliated with Logitech, Razer, SteelSeries, HyperX, Corsair,
Sony, Microsoft, 8BitDo or any other brand mentioned here. Their names and
trademarks belong to their owners.
