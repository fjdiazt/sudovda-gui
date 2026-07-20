# Custom Resolution UI Design

## Goal

Replace the combined display-mode selector with a clearer preset-plus-custom editor. Users can select a resolution preset, freely type validated width and height values, choose a supported integer refresh rate, and retain all form choices across launches.

## User interface

The fixed WinForms window contains:

- `Resolution preset` dropdown with `Copy primary`, discovered/common `WIDTH x HEIGHT` entries, and `Custom`.
- Always-visible `Width` and `Height` textboxes.
- `Refresh rate` dropdown.
- Existing `Make primary`, `Route new windows`, `Start`/`Stop`, and status controls.

Selecting a resolution preset populates Width and Height. Selecting `Copy primary` populates Width, Height, and Refresh from the current primary display. Any manual Width or Height edit changes the preset selection to `Custom`. Refresh remains independent for normal presets.

While a virtual display is active or the form is transitioning, all resolution controls remain disabled with the existing lifecycle controls.

## Presets and refresh rates

Resolution presets are the union of dimensions reported for the current primary display and these common fallbacks:

- 1280 x 720
- 1920 x 1080
- 2560 x 1440
- 3840 x 2160

Presets are deduplicated by Width and Height and sorted ascending. They do not encode a refresh rate.

The refresh dropdown is non-editable and contains:

`24, 25, 30, 48, 50, 60, 72, 75, 90, 100, 120, 144, 165, 240, 360, 480, 500`

If the current primary display uses another integer rate within SudoVDA's supported `1–500 Hz` protocol range, that rate is added so `Copy primary` always remains representable.

## Validation

Width and Height are textboxes rather than numeric spinners so invalid input remains visible instead of silently clamping. Validation runs after every edit:

- Width must be a base-10 integer from `640` through `7680`.
- Height must be a base-10 integer from `480` through `4320`.
- Refresh must be one of the dropdown entries.
- No aspect-ratio or even-number restriction is added.

An `ErrorProvider` attaches an exact range message to each invalid textbox. `Start` remains disabled while either value is invalid. The existing `DisplayController.IsSupported` check remains the final boundary before any driver command.

## Persistence

Settings use the current user's registry at `HKCU\Software\VRPrivacy`. No elevation is required. Persisted values are:

- `Preset`: `CopyPrimary`, `Custom`, or a `WIDTHxHEIGHT` preset key.
- `Width`: DWORD.
- `Height`: DWORD.
- `RefreshHz`: DWORD.
- `MakePrimary`: DWORD boolean.
- `RouteNewWindows`: DWORD boolean.

The form saves the last valid values on close; incomplete or invalid edits are not persisted. A saved `CopyPrimary` value preserves intent: the next launch rereads the current primary mode instead of freezing old numeric values. A saved preset no longer present in the current list restores its saved dimensions as `Custom`. Missing, malformed, or out-of-range values fall back to `Copy primary`, current primary refresh, and enabled checkboxes. Registry failures do not block closing or display operation; the form continues with safe defaults when necessary.

## Components

`MainForm` owns control behavior and lifecycle integration. A small settings record holds persisted values. A small registry store loads and saves that record. Pure validation and preset-selection behavior remain callable by the executable self-test; no UI framework or third-party dependency is added.

`DisplayController.GetModeChoices` continues discovering Windows modes, but the UI deduplicates them by dimensions. `DisplayController.IsSupported` remains unchanged as the driver boundary.

## Testing

Development follows red-green-refactor. Form/self-tests cover:

- Exact default control state.
- Preset selection populating Width and Height.
- `Copy primary` populating all three values.
- Manual Width/Height edits selecting `Custom`.
- Invalid, nonnumeric, and boundary values controlling errors and `Start` availability.
- Refresh list contents and copied-primary exception.
- Settings round-trip, invalid-setting fallback, checkbox restoration, and `CopyPrimary` intent.
- Resolution controls disabling and reenabling with lifecycle state.

Verification is limited to Release build and form/self-tests. Do not run `--smoke-test` or otherwise mutate display topology unless the user separately requests it.

## Non-goals

- Fractional refresh rates.
- Arbitrary typed refresh rates.
- More than one virtual display.
- Preset management or user-named presets.
- Live mode changes while a virtual display is active.
