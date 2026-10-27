# CafeWiFi

A Windows tray app that gets you onto café Wi-Fi without the password dance.

What it covers:

- **Louisa Coffee**: 512 stores, each with its own password.
- **Starbucks**: 577 stores, open network with a terms page it accepts for you.
- **Independent cafés**: any café it can match on OpenStreetMap, remembered on the spot.

## Install

Needs 64-bit Windows 10 (2004 or later) or Windows 11.

1. Download `CafeWiFi-win-x64.zip` from the [latest release](../../releases/latest)
   and unzip `CafeWiFi.exe` anywhere. There's no runtime to install.
2. Run it. The app isn't code-signed, so SmartScreen may warn you. Click
   **More info**, then **Run anyway**.
3. Allow location access. Windows 11 won't let an app read Wi-Fi network or router
   details without it. If no prompt shows up, open
   **Settings > Privacy & security > Location** and turn on location for desktop apps.

A tray icon appears. Turn on **Start with Windows** from its menu and you're set.

## Using it

Most of the time you do nothing. Walk into a store, and it connects and tells you
which one. Click the tray icon when you want to:

- **Nearby stores / All stores**: pick a store and it tries that store's passwords.
- **Connect with password**: type the one on the wall, or pick it from the nearest
  store's guesses.
- **Stats / Footprint map**: how long you've spent in each café, plotted on a map.
- **My cafés**: independent cafés it picked up on its own (below).

## How it works

A wrong password costs 15 to 20 seconds while Windows times out, so the guess list
stays short and ordered by how likely each one is:

1. Try passwords that already worked on this router.
2. If none, use your location to find the 3 nearest stores and interleave their
   guesses: every store's first guess before anyone's second. Louisa's guesses are
   the store phone number in a few formats (`0223458613`, `23458613`, and so on).
3. Once a guess works, remember which store and password that router belongs to.
   From then on the router is enough, and location accuracy stops mattering.

Open networks like Starbucks have no password, so the store list only says where
you are. CafeWiFi auto-joins an open network only within 300 m of a real store, or
on a router it already knows, so a stranger naming their hotspot "Starbucks WiFi"
won't catch it. For the Starbucks terms page, it replays the "connect" request
itself, so you don't have to tap through the browser.

**Independent cafés.** On an unknown network, CafeWiFi checks OpenStreetMap for
cafés within 150 m, matches by name and distance, and remembers the place and its
password. The notification has a "not a café" button that removes it and won't ask
again.

## Adding a brand

The app has no idea how any brand builds its passwords. It just tries the
`passwords` list in each store's JSON, in order. To add a brand:

- `scripts/brands/<id>.py` scrapes the stores and works out the candidate passwords.
- `data/brands/<id>.json` is the output, baked into the exe at build time.

No C# to touch.

## Building

.NET 8, WinForms. With `dotnet` on your PATH:

```
dotnet test
dotnet run --project src/CafeWiFi
```

`./build.ps1 -Version 1.2.3` runs the tests and publishes a single ~75 MB exe to
`build/CafeWiFi-win-x64.zip`.

## License

MIT. See [LICENSE](LICENSE).
