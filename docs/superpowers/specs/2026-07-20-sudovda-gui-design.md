# SudoVDA GUI Design

## Goal

Build a small Windows GUI that controls one temporary SudoVDA virtual monitor. It reuses the SudoVDA driver already installed with Apollo, copies the current primary display mode by default, permits an explicit mode override, can temporarily make the virtual monitor primary, optionally routes newly opened application windows to it, and restores the original display state when stopped or exited.

The first release manages one monitor. Every driver command uses one stable application-owned monitor GUID so later multi-monitor support can replace the single session field with a collection without changing the driver boundary.

## Platform and dependencies

- Windows 10/11 x64.
- .NET 10 WinForms.
- Installed SudoVDA driver. The application does not install, update, reload, or remove the driver.
- Windows APIs through direct P/Invoke. No third-party packages.
- Apollo may remain running. The application owns and removes only its own monitor GUID.

## User interface

One fixed-size window contains:

- Display mode dropdown:
  - `Copy primary` is the default.
  - Explicit entries use `WIDTH x HEIGHT @ HZ`.
- `Make primary` checkbox, enabled by default.
- `Route new windows` checkbox, enabled by default.
- `Start` button while stopped; `Stop` button while active.
- Status text showing stopped, starting, active display identity, stopping, or exact failure.

Controls affecting monitor creation are disabled while active. Changing a mode requires stopping and starting because SudoVDA receives the requested mode when the monitor is added.

Closing the window performs the same cleanup as `Stop`, then exits.

## Components

### `SudoVdaClient`

Opens the SudoVDA device interface and exposes the smallest required protocol:

- Validate driver protocol compatibility.
- Read watchdog timeout.
- Add a monitor with width, height, refresh rate, application GUID, device name, and serial.
- Return the adapter LUID and target ID supplied by the driver.
- Ping the driver before its watchdog timeout.
- Remove only the application GUID.

The IOCTL constants and structures come from SudoVDA's public protocol. Their numeric values and binary sizes are asserted by the executable self-test.

### `DisplayController`

Uses CCD/GDI display APIs to:

- Read the current primary display mode.
- Snapshot active display topology and primary display before mutation.
- Resolve the new SudoVDA target to its GDI display name.
- Activate the target in an extended topology.
- Set it primary when requested while preserving a contiguous desktop arrangement.
- Restore the captured topology and primary display during normal cleanup.

### `WindowRouter`

Installs an out-of-context WinEvent hook after the virtual monitor is ready. It listens for newly shown top-level windows and queues routing work away from the callback thread.

A candidate window must:

- Be a root top-level window with `OBJID_WINDOW`.
- Be visible and not cloaked.
- Belong to a process other than this application.
- Not belong to the Windows shell.
- Not be a tooltip, menu, notification, or non-activating tool window.
- First appear after routing was enabled.

Eligible windows move into the virtual monitor work area with `SetWindowPos`. Windows already on the virtual monitor remain unchanged. A tracked HWND is routed once; replacement HWNDs created by launchers or games are independently routed.

Making the virtual display primary is the main compatibility mechanism for fullscreen games that choose an output before exposing a movable window. Elevated windows can reject direct movement from the non-elevated GUI; primary-display selection remains the fallback.

### `MonitorSession`

Holds one active session:

- Stable monitor GUID.
- Requested mode.
- Adapter LUID and target ID.
- Resolved GDI display name and bounds.
- Original topology and primary display.
- Watchdog cancellation/task.
- Window hook and routed HWND set.

No generic monitor manager, provider abstraction, configuration service, or persistence layer is included in the POC.

## Lifecycle

### Start

1. Reject a second start while a session exists.
2. Open SudoVDA and verify compatible protocol.
3. Resolve `Copy primary` or selected explicit mode.
4. Snapshot active topology and original primary display.
5. Add the application-owned virtual monitor.
6. Start watchdog pings immediately.
7. Wait with a bounded timeout for Windows to expose the returned target.
8. Activate it as an extended display and optionally make it primary.
9. Resolve its bounds.
10. Install the routing hook when selected.
11. Publish the session as active.

Any failure unwinds completed steps in reverse order and returns to stopped state.

### Stop

1. Prevent new routing work.
2. Remove the WinEvent hook and drain queued routing work.
3. Restore original topology and primary display.
4. Stop watchdog pings.
5. Remove the application-owned monitor.
6. Dispose the driver handle and session state.

Cleanup is idempotent. Failure in one cleanup action does not skip later cleanup actions. All failures appear in the final status.

### Unexpected termination

SudoVDA's watchdog can remove the monitor after all client pings stop. Apollo or another client sharing the driver can extend that timing, so a hard process kill may leave the monitor until the next explicit removal. Exact pre-session topology restoration is guaranteed only on normal `Stop`/exit; the POC does not add a recovery service or persistent journal.

## Mode choices

`Copy primary` reads width, height, and refresh at each start. Explicit entries initially cover modes advertised by the current primary display plus common modes accepted by SudoVDA. The complete tuple is selected as one item so resolution and refresh cannot become inconsistent.

The chosen mode is validated before driver mutation. Unsupported application values are rejected rather than silently substituted.

## Error handling

- Driver missing or inaccessible: disable start for that attempt and show driver/interface error.
- Protocol mismatch: show installed and supported protocol versions; do not issue add/remove commands.
- Add failure: preserve original topology.
- Display enumeration/activation timeout: remove the owned monitor and restore topology.
- Unsupported mode: remain stopped.
- Routing failure: keep monitor active, record the affected process/HWND and Win32 error in status/debug output.
- Cleanup failure: attempt every cleanup step and report aggregate errors.
- Apollo activity: never enumerate and delete foreign SudoVDA monitors; GUID ownership is the deletion boundary.

## Verification

The executable supports `--self-test` without changing display state. It checks:

- IOCTL numeric constants.
- Native structure sizes and field offsets.
- Mode parsing and formatting.
- Window eligibility decisions using pure metadata inputs.

Manual integration smoke test:

1. Record displays, topology, primary display, and primary mode.
2. Keep Apollo service running.
3. Start a virtual monitor using `Copy primary` and both checkboxes.
4. Verify exactly one new display, copied mode, and virtual primary.
5. Launch Notepad and verify its window bounds lie on the virtual display.
6. Stop and verify the virtual monitor disappears and the original topology/primary return.
7. Repeat using one explicit mode.
8. Attempt one invalid mode through the internal smoke path and verify rollback leaves no monitor.
9. With no other SudoVDA client pinging, start again, terminate the process without cleanup, and verify watchdog removal while physical displays remain usable.

## Non-goals

- Driver installation, upgrades, signing, or removal.
- Multiple simultaneous virtual monitors in the first release.
- Streaming, capture, remote input, HDR, audio routing, or Apollo session control.
- Moving windows that existed before routing began.
- Guaranteed control of secure desktop, elevated windows, shell UI, or anti-cheat-protected games.
- Persisting settings between runs.
- Restoring exact individual window positions after stop.
