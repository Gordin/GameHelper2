# Controller map regression checks

Build the full application solution first, as required by the repository:

```powershell
dotnet build GameOverlay.sln -c Release
dotnet run --project Tests/MapUiResolver.Tests -c Release
```

This package-free console runner links the production map resolver and references the
real GameOffsets project. It checks hidden maps, stale/unreadable pointers, invalid
vectors and transforms, and replacement of the map pair. A failed assertion returns
a nonzero exit code. It does not attach to a game or validate live offsets.

## Required in-game validation (PoE2 0.5.5)

Live read-only inspection of PoE2 0.5.5 confirmed controller viewports at
`GameUi[0][1]` (large) and `GameUi[0][2]` (mini). Both have the same viewport
vtable, valid parent backlinks and zoom 0.5. The large viewport was visible and
the minimap hidden with the large map open. The production resolver was also
run directly against that process and resolved both viewports successfully.

The former `ControllerModeMapParentPtr` (+0xB98) points at ordinary containers;
their bytes at the zoom offset decode to `Int32BitsToSingle(7)`, which the first
candidate incorrectly accepted. The regression runner covers that false positive
and both mode-specific paths. The user subsequently confirmed the second build
works in-game in controller mode. The broader gameplay regression checklist is:

- Start in keyboard/mouse mode in a combat area. Check both maps, map pan/zoom,
  terrain outlines, POIs and icons.
- Switch to controller mode. In Data Visualization, expand
  `InGameStateObject -> GameUi` and record `Controller mode`, `Map address source`,
  and both map addresses, visibility, zoom and shifts. Verify alignment while moving.
- Open/close the large map, change minimap size, resize the game window, travel to
  another area, then switch back to keyboard/mouse. Repeat in both startup modes.
- Check that inventory, world map, passive tree and other blocking panels hide
  Radar correctly. Their controller paths are outside this patch's scope.
- If testing local co-op, separately check its map placement and blocking panels;
  this patch does not change co-op camera tracking or input-mode detection.

`Controller GameUi[0][1/2]` and `Keyboard GameUi[6][0/1]` identify the validated
paths. `Unresolved` means the current pair failed validation; no fallback to a
different input mode is attempted. Ensure Radar's Draw Area/Zone Map setting is
enabled when checking the terrain overlay.
