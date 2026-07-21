# SudoVDA GUI Layout and Rename Design

## Scope

Improve the existing WinForms layout without changing display behavior, resolution validation, preset selection, persistence, or window routing. Rename the project identity from VR Privacy to SudoVDA GUI.

## Layout

The fixed-size main form uses native WinForms controls and two group boxes:

- **Display** contains the full-width resolution preset selector. Beneath it, Width, Height, and Refresh rate appear as three equal columns. Their labels occupy one row and their controls occupy the row below.
- **Behavior** contains Make primary and Route new windows.

A footer remains outside the group boxes. Its left side contains a small colored status dot followed by the existing status text. Its right side contains the Start/Stop button.

Status colors are:

- Red when stopped or when start/stop fails.
- Green when the virtual display is active.
- Amber while starting or stopping.

The status text continues to carry detailed lifecycle and error messages. Color is supplementary, not the only status signal.

## Rename

- Window title: `SudoVDA`
- Project file: `src/SudoVDA.GUI/SudoVDA.GUI.csproj`
- Assembly/executable: `SudoVDA-GUI.exe`
- Root namespace and all source namespaces: `SudoVDA.GUI`
- Source folder: `src/VrPrivacy` to `src/SudoVDA.GUI`
- Repository root, after current-process locks no longer block it: `C:\src\vr-privacy` to `C:\src\SudoVDA-GUI`

README build, run, and test commands will use the renamed project path. Historical design and implementation-plan documents remain unchanged because they describe the paths used when those documents were written.

## Behavior and Data

No application workflow changes. Presets still populate Width and Height, manual dimension edits still select Custom, refresh remains a validated dropdown, and settings remain under the existing per-user registry key. Renaming that key is excluded to avoid silently discarding saved settings.

## Verification

The executable self-test will verify:

- New title, project assembly name, and control layout.
- Display and Behavior group boxes exist.
- Dimension controls share one row beneath their labels.
- Status indicator is red when stopped, green when active, and amber while busy.
- Existing resolution, persistence, and lifecycle checks still pass.

Run a Release build and `--self-test`. Do not run `--smoke-test` or mutate display settings.
