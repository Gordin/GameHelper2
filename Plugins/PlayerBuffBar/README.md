# PlayerBuffBar

Community plugin for [GameHelper2](https://github.com/Gordin/GameHelper2) (Path of Exile 2).

Compact **player buff overlay**: up to **four independent buff bars**, each with its own watchlist and screen position, plus a separate **resource row** for power/frenzy/endurance charges and rage. Icons load from **poe2db.tw** (cached locally). Written by **MordWraith** for community forks. Read-only overlay — **build from source**.

Also bundled in [MordWraith/Gamehelper-Experimental](https://github.com/MordWraith/Gamehelper-Experimental) plugin catalog.

## Features

- **Up to 4 buff bars** — enable any combination; each bar has its own watchlist, anchor (HP bar or fixed position), icon size, and spacing
- **Position dummy** — drag a bar on screen, then disable the dummy (per bar)
- **Resource row** — P/F/E charge icons from poe2db + numeric rage (not mixed into buff watchlists)
- **Watchlist matching** — substring match on in-game buff keys; skill aliases (e.g. `refutation` → `runic_fortress`)
- **Display** — icons only, text chips, or both; inactive watchlist entries, durations, stack counts
- **Tools** — dump active buff keys to `player_buff_dump.txt`, auto-download missing wiki icons
- **DE/EN** — settings via GameHelper localization

## Requirements

- A working [GameHelper2](https://github.com/Gordin/GameHelper2) source tree
- **.NET 10 SDK** (`net10.0-windows`, x64)
- Internet on first run if **auto-download icons** is enabled (poe2db.tw)

## Build & install

```bash
git clone https://github.com/Gordin/GameHelper2.git
cd GameHelper2
git clone https://github.com/MordWraith/PlayerBuffBar.git Plugins/PlayerBuffBar
dotnet build GameHelper/GameHelper.csproj -c Release
dotnet build Plugins/PlayerBuffBar/PlayerBuffBar.csproj -c Release
```

Enable **PlayerBuffBar** in GameHelper → Plugins.

### Config folder (runtime)

| Path | Purpose |
|------|---------|
| `Plugins/PlayerBuffBar/config/settings.txt` | Overlay, buff bars, watchlists (JSON) |
| `Plugins/PlayerBuffBar/icons/` | Cached buff/charge icons (auto) |
| `Plugins/PlayerBuffBar/config/icon_map.json` | Optional icon overrides |
| `Plugins/PlayerBuffBar/player_buff_dump.txt` | Output of **Dump my buffs** (debug) |

### Quick start

1. Open plugin settings → **Buff bars** → tab **Bar 1** — default watchlist is pre-filled
2. Add buff id substrings (one per line), e.g. `refutation`, `fortify`, `blood_rage`
3. Enable **Bar 2–4** for extra groups with different positions
4. Use **Dump my buffs** in-game to see exact internal buff key names

Do **not** put `power_charge`, `frenzy_charge`, `endurance_charge`, or `rage` in buff watchlists — use the resource bar toggles instead.

## Credits

- Author: **MordWraith**
- Buff/charge icon data: [poe2db.tw](https://poe2db.tw/) (PoE2 wiki)
- Built for [GameHelper2](https://github.com/Gordin/GameHelper2) (Gordin / community)

## Disclaimer

Third-party plugin — **use at your own risk**. Read-only overlay; no input automation. Comply with GGG terms of service.

## Support

- **This plugin:** Discord `#plugins` forum thread
- **GameHelper install/crashes:** `#help` with tag **`plugin`**

## Version history

| Version | Notes |
|---------|--------|
| **1.0.2** | Up to 4 independent buff bars (watchlist + position each); settings tabs Bar 1–4 |
| **1.0.1** | Refutation watchlist alias for in-game `runic_fortress` buff key |
| **1.0.0** | Initial experimental release: dual row (resource + buff), poe2db icons, watchlist |
