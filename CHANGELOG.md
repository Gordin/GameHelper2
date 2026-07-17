# Changelog

All notable changes to GameHelper2 are documented here. This project tracks
Path of Exile 2; "0.5.x" references are the game patch the build targets.

Sections marked **For plugin devs** describe newly exposed APIs you can read
from your own plugins via `Core.*`.

## [2.6.0] - 2026-07-17

### Added

- **Atlas2 Ritual-line tools.** Ported the Ritual helpers from the legacy Atlas
  plugin, including deterministic Rite-mod predictions and a configurable
  **Head of the King** route planner with reward filtering and weights.
- **Atlas2 Uncharted Waters visualization.** Atlas2 can mark ships hidden in the
  fog and highlight the atlas nodes and leylines revealed by the ship under the
  cursor.
- **Monster categories (for plugin devs).** Entities now expose
  `Entity.MonsterCategory`, a flags value covering Humanoid, Beast, Undead,
  Construct, Demon, and Eldritch classifications. The mapping is loaded from an
  embedded table generated from the game's monster-variety data.
- **Plugin conflict declarations (for plugin devs).** Plugins can override
  `PCore.ConflictsWith` and `ConflictPriority` to declare mutually exclusive
  plugins and deterministic startup precedence. Enabling one automatically
  disables active conflicts after saving their settings and running cleanup.
- **More Atlas state exposed (for plugin devs).**
  `ImportantUiElements.AtlasOceanButtons` exposes Uncharted Waters region
  buttons, while persistent badge data keeps Atlas content available for
  fogged and off-screen nodes.
- **Atlas reverse-engineering credit.** Thanks to **yokkenUA** for documenting
  the Atlas offsets and behavior behind Ritual-line prediction, Uncharted
  Waters ships and leylines, mist nodes, and persistent fog-node content; those
  findings form the basis of the Atlas2 and core implementations in this release.

### Changed

- **Atlas2 content and routing expanded.** Ported the Expedition/Ritual Atlas
  helpers and added mappings for the current Atlas contents, including Breach,
  Expedition, Delirium, Ritual, Irradiated, Abyss, Vaal Beacons, Azmeri Spirits,
  Shrines, Strongboxes, Rogue Exiles, Grand Expedition, and other special nodes.
- **Atlas and Atlas2 are mutually exclusive.** The plugin manager now resolves
  their declared conflict instead of allowing both overlays to run together.
- **Confirmed compatible with game patch 0.5.4c.** Updated the Area Instance
  offsets for environments, server data/local player, awake and sleeping
  entities, and terrain metadata.

### Fixed

- **Atlas content in fog.** Map modifiers remain detectable when the game's
  badge UI children are culled for fogged or off-screen nodes.
- **AutoHotKeyTrigger startup exception.** The blank dynamic-condition editor no
  longer sends an empty expression to the parser; null or whitespace expressions
  remain uncompiled until the user enters a condition.
- **LootValue poe2scout refresh.** Accepts the current top-level array returned by
  poe2scout's leagues endpoint, while remaining compatible with the previous
  object-wrapped and string-wrapped response shapes. Currency entries with null
  item metadata are now parsed without aborting the category.

## [2.5.2] - 2026-07-11

### Added

- **Atlas2 content visualization.** Atlas nodes can display their detected content
  as in-game icons above the map name, while the detailed content text remains
  below it. Icon size is configurable, unknown values can be shown in a debug
  view, and a **Show Node Index (debug/RE)** option labels nodes by their Atlas
  child index. (Icons copied over from yokkenUA's Atlas plugin, thanks!)
- **Atlas2 unified map categories and pathing.** The old separate pathing and Map
  Groups editors are now one ordered, expandable category list. Each category
  controls pathing, maximum hops, node foreground/background colors, individual
  built-in targets, and additional user-entered map names. Built-in categories
  cover search, Atlas progression, league/boss targets, Expedition maps, Towers,
  good layouts, unique maps, and other special maps; custom categories can be
  added, reordered, renamed, and removed. Route colors are selected from the more
  colorful of the category's foreground/background colors.
- **Atlas2 route destination labels.** Paths now identify their destination and
  hop count on the first edge (for example `Secluded Temple (7)`). Routes sharing
  a first edge are grouped into a centered, non-overlapping label stack.
- **OffsetHelper patch-day tool.** Added Settings → Tools → **OffsetHelper (OH)**,
  which checks live struct-field offsets and static-address signature patterns to
  help diagnose game-patch breakage without first opening Ghidra.
- **Panel Finder Tool.** In **OffsetHelper (OH)** → UI Panel finder.
  Can be used to find the pointer chain to any panel by letting it detect visible
  panels, than opening tho panel you want, and detect it. Not remotely finished.
  Created by looking at screenshots from a similar tool in the OriathHub project.
- **Radar: Azmeri Spirit icons.** Added configurable radar icons for the known
  Wild, Vivid, Primal, Sacred, and Abyss spirit variants, including spirits whose
  entities would otherwise be discarded as useless.
- **PickupHelper: more built-in categories.** Added Soul Cores, Breach, the Trial of
  Sekhemas (shown as "Djinn Barya (Trial of Sekhemas)"), and the Trial of Chaos (shown
  as "Ultimatum (Chaos Trials)") as built-in item categories. These, plus Jewels, are
  now enabled by default (in addition to the existing Currency, Gems, Tablets, and
  Waystones).
- **Structured Atlas content APIs (for plugin devs).** `AtlasMapNode.Badges` and
  `AtlasMapNode.Effects` expose resolved `AtlasMapNodeBadge` /
  `AtlasMapNodeEffect` metadata alongside the existing raw badge IDs and content
  tokens. Known entries carry their detection ID, description, and optional icon
  basename through static `Known` lists.
- **Temple Console panel detection.** The Temple Console is now exposed as
  `ImportantUiElements.TempleConsole` and counts as a large panel, so overlays
  that hide for large UI panels no longer draw over it.

### Changed

- **Atlas content decoding expanded.** Added and corrected known mappings for
  Breach, Abyss, Ritual, Vaal Beacons, Grand Mirror, Delirium, bosses, and other
  Atlas badge/effect values. Deliriousness percentages are decoded from their
  packed token values, including values above 127%.
- **Atlas2 search and routing improvements.** Search matches map names and detected
  content, does not route while the query is empty, and content-specific borders
  and routes use the unified category colors. Expedition, Lineage, Citadel,
  Breach, unique, tower, and other targets can be enabled individually.

### Fixed

- **Co-op panel detection.** Left/right panel addresses are now resolved correctly
  in controller co-op mode.
- Fixed incorrect Atlas mappings where Abyss, Breach, Vaal Beacon, and Mirror of
  Delirium effects could be mislabeled.
- Fixed Deliriousness values at 128% and above being truncated (for example 200%
  being displayed as 72%).
- Fixed Atlas2 route-label stacks drifting or appearing far away when many routes
  shared an accessible starting node but took different first edges.
- Fixed Radar Azmeri Spirit path matching for the known variants.

## [2.5.1] - 2026-07-04

### Added

- **PickupHelper plugin.** A lightweight hover-to-pickup assistant: when the ground
  item under your cursor matches your filter, it left-clicks it in place. It never
  moves the cursor and never interacts with league mechanics (rituals, essences,
  altars, etc.). An item is picked up when it matches the whitelist **or** an enabled
  category **or** an enabled rarity:
  - **Categories** are derived from each item's metadata path and auto-discovered as
    you play. Currency, Gems, Tablets, and Waystones are enabled by default.
  - **Explicit whitelist** by base type, with a configurable hotkey to add whatever
    you are currently hovering.
  - **Rarity** toggles (Normal / Magic / Rare / Unique).
  - Optional **hold-a-key-to-pick-up** mode, a **max pickup distance**, a randomized
    **detection-to-click delay** (configurable min/max), click cooldowns, and a
    **block while a large panel is open** safety toggle.
  - A debug window shows the hovered item's name, path, category, rarity, distance,
    and whether it would be picked up.
  - Localized in all 11 supported languages. Uses only existing `Core.*` APIs — no
    core changes required.
- **`Stack` component now exposes stack caps (for plugin devs).** In addition to
  `Count`, the `Stack` component on stackable items now reports `MaxStack` (the
  normal stack cap for the backpack and normal stash tabs) and `MaxStackTab` (the
  currency stash tab stack cap).

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

[2.6.0]: https://github.com/Gordin/GameHelper2/compare/v2.5.2...v2.6.0
[2.5.2]: https://github.com/Gordin/GameHelper2/compare/2.5.1...2.5.2
[2.5.1]: https://github.com/Gordin/GameHelper2/compare/2.5.0...2.5.1
[2.5.0]: https://github.com/Gordin/GameHelper2/compare/2.4.2...2.5.0
[2.4.2]: https://github.com/Gordin/GameHelper2/compare/2.4.1...2.4.2
[2.4.1]: https://github.com/Gordin/GameHelper2/compare/v2.4.0...2.4.1
