# Changelog

All notable changes to GameHelper2 are documented here. This project tracks
Path of Exile 2; "0.5.x" references are the game patch the build targets.

Sections marked **For plugin devs** describe newly exposed APIs you can read
from your own plugins via `Core.*`.

## [2.5.0] - 2026-07-03

### Added

- **Multi-language overlay UI.** Overlay text is now translated through keyed JSON
  resources, with a **UI Language** selector in the Settings → General tab.
  Supported languages: English, French, German, Spanish (Spain), Japanese, Korean,
  Portuguese (Brazil), Russian, Thai, Simplified Chinese, and Traditional Chinese.
  Missing keys fall back to English. The core UI and the bundled plugins are
  localized; each plugin owns its translations under
  `Plugins/<Name>/Localization/<lang>.json`.

  **For plugin devs:**
  - `GameHelper.Localization.OverlayLocalization` — static helpers for core/overlay
    text: `T(key, fallback)`, `F(key, fallback, args)`, `Label(key, fallback, id)`,
    `Title(key, fallback, id)`, plus `CurrentLanguage` and `SupportedLanguages`.
  - `PCore<TSettings>.PluginText` (a `PluginLocalization`) — loads your plugin's own
    `Localization/*.json` and exposes the same `T`/`F`/`Label`/`Title` API. Override
    `GetDescription()` (defaults to the `plugin.description` key) to show a localized
    one-liner in the plugin manager.
  - `Plugins/Directory.Build.targets` auto-copies each plugin's `Localization/*.json`
    into the deployed plugin folder — no per-plugin `.csproj` change required.
- **Plugin manager description column.** The plugin list now shows a short,
  localized description for each plugin next to its enable toggle.
- **Atlas Skills panel is now a "large panel".** Radar and Health Bars hide while
  the Atlas skill tree is open, matching the other large panels.

### Changed

- **Confirmed compatible with game patch 0.5.4b** — no offset or signature
  changes were required; the version shown in the About section is now `0.5.4b`.
- **Renamed the `Atlas` plugin to `Atlas2`.**
- **Removed the legacy German/English-only translation shim**
  (`OverlayLocalization.L(english, german)` / `IsGerman`) in favor of the keyed
  multi-language system above. Plugins that used it (MapKillCounter, PlayerBuffBar)
  were migrated, and their German text is preserved as `de-DE.json` resources.

### Fixed

- Reduced memory read-failure log spam.
- The debugger no longer breaks on a first-chance `NullReferenceException` in
  `GameProcess.Pid` on every startup (now a null check instead of throw/catch).

## [2.4.2] - 2026-06-26

### Added

- **Radar:** Expedition explosive/remnant post icons.
- **Mouse-over entity in game state.** The entity currently under the cursor is
  now tracked and reset cleanly when nothing is hovered.

  **For plugin devs** — `GameHelper.RemoteObjects.States.InGameState.MouseOverEntity`
  (an `Entity`, reachable as `Core.States.InGameStateObject.MouseOverEntity`).
  Use it to act on whatever the player is hovering (tooltip, highlight, price
  lookup, etc.). It follows the host→sub→entity pointer chain each frame; when
  nothing is hovered its `Address` is `IntPtr.Zero` and `IsValid` is `false`, so
  always gate on `IsValid` before reading `Path`/`Id`.

## [2.4.1] - 2026-06-25

### Added

- **LootValue plugin.** Reads ground/inventory item identity from memory and
  prices it against poe.ninja / poe2scout — no clipboard copy required.
- **Item identity components for plugins.** Two new entity components were added
  to support price lookups and item identification.

  **For plugin devs:**
  - `GameHelper.RemoteObjects.Components.Base` — present on item entities. Exposes
    `BaseItemName` (localized display name, e.g. "Greater Orb of Augmentation")
    and `InternalName` (locale-independent BaseItemTypes id, e.g.
    `CurrencyAddModToMagic2`). Use `BaseItemName` as a price key for non-uniques
    and `InternalName` as a stable cross-language/patch key. Reading these means
    you no longer need the user to copy an item to the clipboard to learn its name.
  - `GameHelper.RemoteObjects.Components.RenderItem` — present on item entities.
    Exposes `ResourcePath`, the full 2D inventory art (.dds) path. The basename is
    a stable, unambiguous identity for uniques (each has its own icon) and matches
    poe.ninja / poe2scout's `IconUrl` basename, so it works as a unique price key
    without reading the item's name. Currency tiers share one art — prefer `Base`
    for non-uniques.
- **Universal font in core.** DejaVuSans + GNU Unifont are bundled and applied
  app-wide so non-Latin glyphs (item/affix names, etc.) render correctly.

  **For plugin devs** — `GameHelper.Utils.UniversalFont` (`ApplyFromSettings()`,
  `ApplyConfigured()`). The core applies this for you; you generally just get
  correct glyph coverage in your `DrawUI` for free.
- **Chat-active detection via UI flag.** Whether the chat box is focused is now
  read from a UI element flag instead of inferred from background color
  (more reliable).

  **For plugin devs** — `GameHelper.RemoteObjects.UiElement.ChatParentUiElement.IsChatActive`
  (a `bool`). Check it before sending input or reacting to key presses so you
  don't act while the user is typing in chat.
- **More Atlas/map data exposed** (`ImportantUiElements`) and partial parsing of
  in-map content info, used by the Atlas plugin.
- **Radar:** Rituals, Breaches, Abyss, Sacred Water, Research Strongboxes,
  Runestone encounters and adjustable Runestone socket-count display; Breach
  Strongholds and Hive Fortresses are now identified.
- **HealthBars:** Ward and Life are now combined into a single bar.
- `pull-external-plugins.bat` to `git pull` all plugin repos cloned into `Plugins`.
- **Memory read-failure diagnostic tool** for troubleshooting offset/read issues.

### Changed

- Updated offsets and signature patterns for game patch **0.5.4**.
- Radar no longer recalculates paths that have already been reached, and hides
  them once reached.
- Discord invite URL updated to the new server.

### Fixed

- App locking up completely after the earlier memory-read hardening.
- Many memory-read exceptions are now caught instead of crashing reads.
- Bad pointers used when parsing Atlas nodes.
- Disabled plugins' settings being overwritten on close.
- Co-op (multiplayer) read fix.

[2.5.0]: https://github.com/Gordin/GameHelper2/compare/2.4.2...2.5.0
[2.4.2]: https://github.com/Gordin/GameHelper2/compare/2.4.1...2.4.2
[2.4.1]: https://github.com/Gordin/GameHelper2/compare/v2.4.0...2.4.1
