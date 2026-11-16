# LootTracker

Open F12 → Maps & earnings → LootTracker. The plugin offers a compact three-column HUD
and a detailed view, pause/resume, session archiving, history and CSV export. Its icons
are drawn in ImGui, and no third-party executable or UI asset is required.

Area time is grouped by area ID and server instance hash. Currency observations aggregate
all backpack stacks by metadata before counting positive deltas. Existing inventory is a
baseline, not earnings. Invalid reads, transitions, large panels and pauses reset that baseline;
currency acquired during those gaps is not backfilled. Stack splitting and merging do not
count as new currency. Spending within an interval can offset pickups; dropping and picking
up the same currency can count again. Gold, equipment, ground drops and map costs are excluded.

Map time excludes towns, hideouts, loading, Escape menus, manual pauses and lost focus.
The HUD's session clock additionally includes foreground hideout/menu time. Valuation reads
LootValue's matching league/source cache, rejects prices older than 24 hours, and uses its
exchange rates for Ex/Div. Missing prices show a dash; partial valuations show an asterisk.

State lives under `Plugins/LootTracker/config/`: `active.json`, archived `sessions/` and
CSV `exports/`. Saves run every 10 seconds and on disable, with a previous-save backup.
Offline time is not accumulated. Abrupt termination can lose the latest unsaved observations.

See [the Traditional Chinese guide](README.zh-Hant.md) for controls and
[offline validation](../../Tests/README.md) for ledger, persistence, pricing and HUD checks.
Actual pickup behavior still needs validation against the current game client.
