# Aspect-Ratio Lock Button Design

## Goal

Add a compact lock toggle for proportional manual Width and Height editing without changing saved settings, preset behavior, refresh-rate behavior, or driver logic.

## User interface

The dimension row becomes four columns:

`Width | Height | lock button | Refresh rate`

The lock control uses native WinForms toggle-button behavior through a button-appearance checkbox. It has no visible label:

- Unlocked/default: `🔓`.
- Locked/pressed: `🔒`.
- Tooltip and accessible name: `Lock aspect ratio` or `Unlock aspect ratio`.

The narrow button sits after Height and before Refresh rate. Aspect-ratio and resolution-preset dropdowns continue spanning the full row. The lock button disables with all other resolution controls while the virtual display is active or transitioning.

## Behavior

The lock is off by default and is not persisted.

Turning it on captures the current valid Width:Height pair. While locked:

- Editing Width recalculates Height.
- Editing Height recalculates Width.
- The recalculated counterpart rounds to the nearest whole pixel.
- Selecting a preset or `Match primary display` replaces both dimensions and refreshes the captured ratio.

Programmatic counterpart updates suppress recursive text-change handling. Manual edits still select `Custom`.

If current dimensions are invalid when the button is pressed, the button stays unlocked. Existing field validation and Start-button disabling remain authoritative. A calculated value outside existing dimension bounds remains visible and invalid; it is not silently clamped.

## Components

`MainForm` owns the toggle state, captured ratio, counterpart calculation, tooltip, and event suppression. Existing `ResolutionSize`, validation, settings persistence, and driver interfaces remain unchanged. No dependency or image asset is added.

## Testing

The executable form self-test covers:

- Default unlocked icon and accessible text.
- Locking a valid preset.
- Width edit recalculating Height.
- Height edit recalculating Width.
- Preset selection refreshing the captured ratio.
- Invalid dimensions refusing to lock.
- Lock control disabling and reenabling with other resolution controls.
- Existing persistence remaining unchanged.

Verification uses Release `--self-test` and a Release build. No `--smoke-test` or display-topology mutation runs.

## Non-goals

- Persisting lock state.
- Fractional dimensions.
- Changing aspect-filter selection during manual edits.
- Changing resolution validation or driver limits.
