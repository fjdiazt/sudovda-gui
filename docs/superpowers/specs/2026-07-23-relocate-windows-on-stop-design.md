# Relocate Windows on Stop Design

## Goal

Before removing the virtual display, move every visible normal top-level window currently on it to the display that was primary when the virtual-display session started.

## Behavior

- Run relocation during normal **Stop** handling after new-window routing stops and before display topology is restored.
- Use the virtual display's current bounds as the source.
- Use the original primary display's current bounds as the destination.
- Move every visible, uncloaked, activating top-level application window whose largest overlap is with the virtual display.
- Include SudoVDA GUI, Chrome, Explorer folder windows, and windows moved there manually.
- Exclude the desktop, taskbar, tool windows, cloaked windows, and non-activating surfaces.
- Preserve each window's size when it fits. Clamp oversized windows to the destination display.
- Preserve the window's relative offset from the source display, then clamp it fully inside the destination.

## Architecture

Keep relocation in `WindowRouter`, beside the existing Win32 window inspection and positioning code. Add one pure placement calculation for self-testing and one best-effort enumeration method for runtime relocation. Do not add dependencies, configuration, persistent tracking, or per-application rules.

`MainWindow.StopAsync` obtains the current virtual and former-primary bounds, disposes the router, calls the relocation method, then continues the existing topology restore, watchdog shutdown, and virtual-display removal sequence.

## Error Handling

Relocation is best effort. A window that disappears or refuses `SetWindowPos` is reported through the existing stop error collection, but remaining windows are still attempted and display shutdown continues.

## Verification

- Add self-tests for relative translation, destination clamping, and oversized-window shrinking.
- Run the full CLI self-test.
- Build Release with zero warnings and errors.
- Do not run a live display test; the user will perform manual window-relocation testing.
