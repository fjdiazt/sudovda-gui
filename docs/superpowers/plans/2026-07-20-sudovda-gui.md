# SudoVDA GUI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a one-monitor Windows SudoVDA GUI with copied/selected display mode, temporary-primary control, optional new-window routing, and safe teardown.

**Architecture:** A .NET 10 WinForms executable calls the installed SudoVDA IOCTL protocol directly. Focused driver, display, and window-routing classes feed one `MainForm`; one `MonitorSession` owns all temporary state.

**Tech Stack:** C# 14, .NET 10, WinForms, direct Win32 P/Invoke, SudoVDA protocol 0.2.

## Global Constraints

- Windows 10/11 x64.
- Reuse installed SudoVDA; never install, update, reload, or remove its driver.
- No third-party packages.
- One app-owned stable monitor GUID; never remove foreign monitors.
- One simultaneous virtual monitor in first release.
- Closing or stopping restores original display state before monitor removal.
- `Route new windows` affects only eligible top-level windows first shown after activation.

---

## File map

- `src/VrPrivacy/VrPrivacy.csproj`: WinForms executable settings.
- `src/VrPrivacy/Program.cs`: GUI entry point and `--self-test` dispatch.
- `src/VrPrivacy/Models.cs`: display mode, display snapshot, native-output, and session records.
- `src/VrPrivacy/SudoVdaClient.cs`: SetupAPI device discovery, IOCTL protocol, watchdog.
- `src/VrPrivacy/DisplayController.cs`: display enumeration, positioning, primary selection, restoration.
- `src/VrPrivacy/WindowRouter.cs`: WinEvent hook, eligibility, queued movement.
- `src/VrPrivacy/MainForm.cs`: programmatic WinForms UI and lifecycle orchestration.
- `src/VrPrivacy/SelfTest.cs`: mutation-free executable checks.
- `.gitignore`: .NET output exclusions.
- `README.md`: build, run, requirements, limitations, smoke test.

### Task 1: Executable shell and pure models

**Files:**
- Create: `.gitignore`
- Create: `src/VrPrivacy/VrPrivacy.csproj`
- Create: `src/VrPrivacy/Program.cs`
- Create: `src/VrPrivacy/Models.cs`
- Create: `src/VrPrivacy/SelfTest.cs`

**Interfaces:**
- Produces: `DisplayMode`, `DisplaySnapshot`, `AddedDisplay`, `SelfTest.Run()`.

- [ ] **Step 1: Add failing self-test entry point**

```csharp
[STAThread]
static int Main(string[] args)
{
    if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        return SelfTest.Run();
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
    return 0;
}
```

- [ ] **Step 2: Build and confirm missing model/UI failures**

Run: `dotnet build src/VrPrivacy/VrPrivacy.csproj`
Expected: FAIL for undefined `SelfTest` or `MainForm`.

- [ ] **Step 3: Add minimal models and assertions**

```csharp
internal readonly record struct DisplayMode(uint Width, uint Height, uint RefreshHz)
{
    public override string ToString() => $"{Width} x {Height} @ {RefreshHz} Hz";
}

internal sealed record DisplayState(string DeviceName, int X, int Y, uint Width, uint Height, uint RefreshHz, bool Primary);
internal sealed record DisplaySnapshot(IReadOnlyList<DisplayState> Displays);
internal readonly record struct AddedDisplay(long AdapterLuid, uint TargetId);
```

`SelfTest.Run()` calls a local `Check(bool, string)` and returns `0` only when all pure assertions pass.

- [ ] **Step 4: Build and run self-test**

Run: `dotnet run --project src/VrPrivacy/VrPrivacy.csproj -- --self-test`
Expected: `Self-test passed.` and exit `0`.

- [ ] **Step 5: Commit**

```powershell
git add .gitignore src/VrPrivacy
git commit -m "build: add WinForms application shell"
```

### Task 2: SudoVDA protocol client

**Files:**
- Create: `src/VrPrivacy/SudoVdaClient.cs`
- Modify: `src/VrPrivacy/SelfTest.cs`

**Interfaces:**
- Produces: `SudoVdaClient.Open()`, `GetWatchdog()`, `Add(DisplayMode, Guid)`, `Ping()`, `Remove(Guid)`.
- Produces: `ProtocolVersion` and native structure-size checks.

- [ ] **Step 1: Add failing protocol-layout checks**

```csharp
Check(SudoVdaClient.IoctlAdd == 0x00222000, "ADD IOCTL");
Check(SudoVdaClient.IoctlRemove == 0x00222004, "REMOVE IOCTL");
Check(SudoVdaClient.IoctlGetWatchdog == 0x0022200C, "watchdog IOCTL");
Check(SudoVdaClient.IoctlPing == 0x00222220, "ping IOCTL");
Check(SudoVdaClient.IoctlGetProtocol == 0x002223FC, "protocol IOCTL");
Check(Marshal.SizeOf<SudoVdaClient.AddParams>() == 56, "ADD layout");
Check(Marshal.SizeOf<SudoVdaClient.AddOut>() == 12, "ADD output layout");
```

- [ ] **Step 2: Run self-test and verify compile failure**

Run: `dotnet run --project src/VrPrivacy/VrPrivacy.csproj -- --self-test`
Expected: FAIL because `SudoVdaClient` is undefined.

- [ ] **Step 3: Implement minimal protocol client**

Use interface GUID `e5bcc234-1e0c-418a-a0d4-ef8b7501414d`, `SetupDiGetClassDevsW`, `SetupDiEnumDeviceInterfaces`, `SetupDiGetDeviceInterfaceDetailW`, and `CreateFileW` with read/write sharing. Declare packed-by-default sequential structs matching:

```csharp
internal const uint IoctlAdd = 0x00222000;
internal const uint IoctlRemove = 0x00222004;
internal const uint IoctlGetWatchdog = 0x0022200C;
internal const uint IoctlPing = 0x00222220;
internal const uint IoctlGetProtocol = 0x002223FC;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal unsafe struct AddParams
{
    public uint Width, Height, RefreshRate;
    public Guid MonitorGuid;
    public fixed byte DeviceName[14];
    public fixed byte SerialNumber[14];
}
```

Reject protocol major other than `0` or minor below `2`. `Dispose()` closes only this client handle. `Remove()` accepts only caller-supplied app GUID.

- [ ] **Step 4: Run layout checks**

Run: `dotnet run --project src/VrPrivacy/VrPrivacy.csproj -- --self-test`
Expected: `Self-test passed.`

- [ ] **Step 5: Commit**

```powershell
git add src/VrPrivacy/SudoVdaClient.cs src/VrPrivacy/SelfTest.cs
git commit -m "feat: add SudoVDA protocol client"
```

### Task 3: Display state and primary control

**Files:**
- Create: `src/VrPrivacy/DisplayController.cs`
- Modify: `src/VrPrivacy/SelfTest.cs`

**Interfaces:**
- Consumes: `DisplayMode`, `DisplaySnapshot`, `AddedDisplay`.
- Produces: `Capture()`, `GetPrimaryMode()`, `GetModeChoices()`, `WaitForDisplayAsync(AddedDisplay, ...)`, `PlaceAndSetPrimary(...)`, `Restore(DisplaySnapshot)`.

- [ ] **Step 1: Add failing display-mode checks**

```csharp
var modes = DisplayController.DistinctModes([
    new(1920, 1080, 60), new(1920, 1080, 60), new(2560, 1440, 120)]);
Check(modes.Count == 2, "mode deduplication");
Check(modes[0].Width == 1920 && modes[1].Width == 2560, "mode ordering");
```

- [ ] **Step 2: Run self-test and verify compile failure**

Run: `dotnet run --project src/VrPrivacy/VrPrivacy.csproj -- --self-test`
Expected: FAIL because `DisplayController` is undefined.

- [ ] **Step 3: Implement display controller**

Enumerate active displays with `EnumDisplayDevicesW` and `EnumDisplaySettingsW`. Snapshot device name, position, dimensions, refresh, and primary flag. Resolve the added monitor through `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` by matching adapter LUID and target ID, then `DisplayConfigGetDeviceInfo(GET_SOURCE_NAME)`.

Place the virtual display immediately right of the current desktop bounds. Apply every position using `ChangeDisplaySettingsExW(..., CDS_UPDATEREGISTRY | CDS_NORESET)` and apply once with a null device. For primary selection, offset every active display by the selected display position and apply `CDS_SET_PRIMARY` to it. Restore the captured display modes/positions using the same batched operation.

- [ ] **Step 4: Run pure checks and build**

Run: `dotnet run --project src/VrPrivacy/VrPrivacy.csproj -- --self-test`
Expected: `Self-test passed.`

- [ ] **Step 5: Commit**

```powershell
git add src/VrPrivacy/DisplayController.cs src/VrPrivacy/SelfTest.cs
git commit -m "feat: manage Windows display state"
```

### Task 4: New-window routing

**Files:**
- Create: `src/VrPrivacy/WindowRouter.cs`
- Modify: `src/VrPrivacy/SelfTest.cs`

**Interfaces:**
- Produces: `WindowRouter.Start(Rectangle targetBounds)`, `Dispose()`.
- Produces: pure `WindowCandidate.IsEligible` decision.

- [ ] **Step 1: Add eligibility checks**

```csharp
Check(WindowRouter.IsEligible(new(true, false, false, false, 42), 7, 99), "normal window");
Check(!WindowRouter.IsEligible(new(true, false, false, false, 7), 7, 99), "own process");
Check(!WindowRouter.IsEligible(new(true, true, false, false, 42), 7, 99), "cloaked");
Check(!WindowRouter.IsEligible(new(true, false, true, false, 42), 7, 99), "tool window");
Check(!WindowRouter.IsEligible(new(true, false, false, false, 99), 7, 99), "shell process");
```

- [ ] **Step 2: Run self-test and verify failure**

Run: `dotnet run --project src/VrPrivacy/VrPrivacy.csproj -- --self-test`
Expected: FAIL because `WindowRouter` is undefined.

- [ ] **Step 3: Implement hook and routing worker**

Install `SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, ..., WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS)`. Accept only `OBJID_WINDOW`, `CHILDID_SELF`, root HWNDs. Read visibility, DWM cloaking, extended styles, owner process, and shell process. Queue each HWND once through `Channel<nint>`; move it with `SetWindowPos` inside target bounds using existing window size clamped to the target.

Dispose order: unhook, complete channel, cancel worker, await worker, clear HWND set.

- [ ] **Step 4: Run self-test**

Run: `dotnet run --project src/VrPrivacy/VrPrivacy.csproj -- --self-test`
Expected: `Self-test passed.`

- [ ] **Step 5: Commit**

```powershell
git add src/VrPrivacy/WindowRouter.cs src/VrPrivacy/SelfTest.cs
git commit -m "feat: route newly shown windows"
```

### Task 5: GUI and owned session lifecycle

**Files:**
- Create: `src/VrPrivacy/MainForm.cs`
- Modify: `src/VrPrivacy/Models.cs`
- Modify: `src/VrPrivacy/Program.cs`

**Interfaces:**
- Consumes all previous components.
- Produces working `Start`, `Stop`, and close behavior.

- [ ] **Step 1: Add session ownership model**

```csharp
internal sealed class MonitorSession : IAsyncDisposable
{
    public required Guid MonitorGuid { get; init; }
    public required SudoVdaClient Driver { get; init; }
    public required DisplaySnapshot Snapshot { get; init; }
    public required string DeviceName { get; init; }
    public required CancellationTokenSource WatchdogCancellation { get; init; }
    public required Task WatchdogTask { get; init; }
    public WindowRouter? Router { get; set; }
}
```

- [ ] **Step 2: Build and verify missing form behavior**

Run: `dotnet build src/VrPrivacy/VrPrivacy.csproj`
Expected: FAIL until `MainForm` is implemented.

- [ ] **Step 3: Implement programmatic WinForms UI**

Create fixed form with mode combo, primary/routing checkboxes, start/stop button, and status label. Use stable GUID `8d6a8a70-67e9-4af0-9e57-0fcb401ca31b`, device name `VRPrivacy`, serial `VRP0001`.

Start performs: capture, open/validate driver, add, begin watchdog, resolve display with 5-second timeout, place/set primary, optionally start router, publish session. Catch failure, restore snapshot when captured, remove owned GUID when added, dispose driver, then show exact exception.

Stop performs: detach session field, dispose router, restore snapshot, cancel/await watchdog, remove owned GUID, dispose driver. Continue after each cleanup failure and report combined messages.

`FormClosing` cancels close once, awaits stop, then closes again. Disable mutable controls while active or transitioning.

- [ ] **Step 4: Build and run self-test**

Run: `dotnet build src/VrPrivacy/VrPrivacy.csproj`
Expected: build succeeds with zero errors.

Run: `dotnet run --project src/VrPrivacy/VrPrivacy.csproj -- --self-test`
Expected: `Self-test passed.`

- [ ] **Step 5: Commit**

```powershell
git add src/VrPrivacy
git commit -m "feat: add SudoVDA GUI lifecycle"
```

### Task 6: Documentation and live smoke test

**Files:**
- Create: `README.md`
- Modify only files exposed by live defects.

**Interfaces:**
- Produces: documented build/run flow and verified app binary.

- [ ] **Step 1: Document exact usage and limits**

Include:

```powershell
dotnet build src/VrPrivacy/VrPrivacy.csproj -c Release
dotnet run --project src/VrPrivacy/VrPrivacy.csproj
dotnet run --project src/VrPrivacy/VrPrivacy.csproj -- --self-test
```

State SudoVDA prerequisite, Apollo coexistence boundary, non-admin elevated-window limitation, crash-watchdog behavior, and one-monitor POC scope.

- [ ] **Step 2: Run static verification**

Run: `dotnet build src/VrPrivacy/VrPrivacy.csproj -c Release`
Expected: zero errors.

Run: `dotnet run --project src/VrPrivacy/VrPrivacy.csproj -- --self-test`
Expected: `Self-test passed.`

- [ ] **Step 3: Run live lifecycle smoke**

With Apollo service still running: start copied-mode monitor, verify monitor and primary, launch Notepad and verify its rectangle lies on virtual display, stop, verify virtual display removed and original primary/positions restored. Repeat with one explicit mode. If any check fails, fix only the failing boundary and rerun full smoke.

- [ ] **Step 4: Inspect final diff and commit**

```powershell
git diff --check
git status --short
git add README.md src/VrPrivacy
git commit -m "docs: add SudoVDA GUI usage"
```
