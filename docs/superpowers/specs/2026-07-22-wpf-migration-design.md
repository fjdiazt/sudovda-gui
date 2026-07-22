# Pure WPF Migration Design

## Goal

Replace the Windows Forms presentation layer with pure WPF while preserving the app's current virtual-display behavior, settings, command-line checks, and cleanup guarantees.

## Scope

- Target .NET 10 Windows x64.
- Remove `UseWindowsForms` and every `System.Windows.Forms` dependency.
- Use WPF `Application`, `Window`, XAML controls, dispatcher, and automation properties.
- Preserve the existing driver, display topology, routing, settings, resolution, aspect-filter, and aspect-lock logic.
- Preserve `--self-test`, `--smoke-test`, and `--smoke-window` behavior.
- Keep `System.Drawing` primitives used by native display and window-routing code; they are not a Windows Forms dependency.
- Do not provide a compatibility layer, hybrid host, alternate UI, or theme selector.

## User Experience

The app has one default dark theme. The window remains compact and non-resizable. It keeps the same information architecture:

1. Display group: aspect ratio, resolution preset, width, height, aspect lock, and refresh rate.
2. Behavior group: Make primary and Route new windows.
3. Footer: colored status indicator, status text, and Start/Stop action.

WPF controls use explicit labels, logical tab order, tooltips, and automation names. The lock is an icon-only `ToggleButton` showing `🔓` or `🔒`. Invalid dimensions get a visible error border and explanatory tooltip. Busy or active lifecycle states disable configuration controls exactly as today.

The single dark palette uses high-contrast neutral surfaces, light foreground text, blue focus/accent states, green active status, amber transitional status, and red stopped/error status. Native control templates are restyled only as needed for consistent dark rendering; no theming framework or dependency is added.

## Architecture

`App.xaml` owns the dark resource palette and shared control styles. `App.xaml.cs` handles command-line modes and opens `MainWindow` for normal execution.

`MainWindow.xaml` declares the named visual tree. `MainWindow.xaml.cs` keeps the existing code-behind state machine and event flow. This is deliberately not an MVVM rewrite: the current app has one small window, and separating commands/view-models would add migration risk without user value.

The non-UI files remain unchanged unless they directly reference Windows Forms. The smoke-test window becomes a WPF `Window`. Monitor detection replaces `Screen.FromHandle` with direct User32 monitor information. UI message pumping uses WPF's dispatcher where needed.

## Data and Lifecycle Flow

Startup loads primary-display information and registry settings, discovers modes, populates WPF controls, and validates the selected mode. Selection and typing events follow the current suppression rules so presets, filtering, validation, persistence, and proportional edits remain synchronous and deterministic.

Start captures topology, opens SudoVDA, adds the monitor, starts watchdog pings, places the display, optionally makes it primary, and optionally starts new-window routing. Stop reverses those operations and reports partial cleanup errors. Window closing persists settings and asynchronously stops an active session before allowing shutdown.

Background errors marshal through `Dispatcher` before updating status UI.

## Error Handling

- Invalid width or height disables Start and exposes the existing exact validation messages.
- Settings save failures appear in status without crashing.
- Partial start failures restore topology, stop routing/watchdog, and remove the app-owned monitor.
- Stop failures retain the session when monitor removal fails, preserving the ability to retry.
- Closing remains cancelled until an active session has stopped successfully.

## Verification

The self-test is rewritten to locate named WPF elements and verify:

- dark-theme resources and window structure;
- default values, resolution filtering, labels, and ordering;
- preset selection, manual validation, persistence, and enabled states;
- aspect-lock icons, accessibility, proportional edits, and preset ratio refresh;
- protocol layouts, driver access, display discovery, routing eligibility, and registry round-trip.

Release `--self-test` and Release build must pass with zero warnings and errors. The live `--smoke-test` remains available but is not run automatically because it mutates display topology.

## Non-Goals

- No WinForms compatibility or hybrid hosting.
- No MVVM framework, dependency injection, navigation, theme switching, localization rewrite, installer, or packaging change.
- No driver, IOCTL, display-policy, registry-schema, or routing-policy changes.
