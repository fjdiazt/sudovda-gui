# Relocate Windows on Stop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move visible normal top-level windows from the virtual display to the session's original primary display before stopping the virtual display.

**Architecture:** Extend the existing `WindowRouter` Win32 seam with pure selection/placement helpers and a best-effort `EnumWindows` relocation pass. Call that pass from `MainWindow.StopAsync` after routing stops and before topology restoration.

**Tech Stack:** .NET 10, C#, WPF, Win32 User32/DWM P/Invoke, existing CLI self-test.

## Global Constraints

- Use the current worktree on branch `codex/relocate-windows-on-stop`.
- Move every visible normal top-level window currently on the virtual display.
- Exclude desktop, taskbar, tool, cloaked, and non-activating windows.
- Preserve relative placement and size where possible; clamp to the original primary display.
- Relocation is best effort and must not prevent display shutdown.
- Add no dependencies, settings, persistent tracking, or per-application rules.
- Do not run a live display test; the user will manually verify runtime relocation.

---

### Task 1: Pure relocation selection and placement

**Files:**
- Modify: `src/SudoVDA.GUI/SelfTest.cs:82-88`
- Modify: `src/SudoVDA.GUI/WindowRouter.cs:82-90`

**Interfaces:**
- Produces: `WindowRouter.ShouldRelocate(WindowCandidate candidate, bool isRoot, bool isShellWindow, bool onSourceMonitor) -> bool`
- Produces: `WindowRouter.CalculateRelocatedBounds(Rectangle windowBounds, Rectangle sourceBounds, Rectangle destinationBounds) -> Rectangle`

- [ ] **Step 1: Write failing selection and placement checks**

Add after the current `WindowRouter.IsEligible` checks in `SelfTest.Run`:

```csharp
Check(WindowRouter.ShouldRelocate(
        new WindowCandidate(true, false, false, false, 42), true, false, true),
    "normal source-display window relocates");
Check(!WindowRouter.ShouldRelocate(
        new WindowCandidate(true, false, false, false, 42), true, true, true),
    "shell window does not relocate");
Check(!WindowRouter.ShouldRelocate(
        new WindowCandidate(true, false, false, false, 42), true, false, false),
    "other-display window does not relocate");

var sourceBounds = new Rectangle(0, -1080, 1920, 1080);
var destinationBounds = new Rectangle(100, 200, 1280, 720);
Check(WindowRouter.CalculateRelocatedBounds(
        new Rectangle(200, -980, 800, 600), sourceBounds, destinationBounds) ==
      new Rectangle(300, 300, 800, 600),
    "relocation preserves relative offset");
Check(WindowRouter.CalculateRelocatedBounds(
        new Rectangle(1500, -200, 900, 700), sourceBounds, destinationBounds) ==
      new Rectangle(480, 220, 900, 700),
    "relocation clamps inside destination");
Check(WindowRouter.CalculateRelocatedBounds(
        new Rectangle(-100, -1200, 2000, 1000), sourceBounds, destinationBounds) ==
      destinationBounds,
    "relocation shrinks oversized window");
```

- [ ] **Step 2: Run build and verify RED**

Run:

```powershell
dotnet build src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release
```

Expected: FAIL with `CS0117` because `WindowRouter.ShouldRelocate` and `WindowRouter.CalculateRelocatedBounds` do not exist.

- [ ] **Step 3: Implement the pure helpers**

Add after `WindowRouter.IsEligible`:

```csharp
internal static bool ShouldRelocate(
    WindowCandidate candidate,
    bool isRoot,
    bool isShellWindow,
    bool onSourceMonitor) =>
    isRoot &&
    !isShellWindow &&
    onSourceMonitor &&
    candidate.Visible &&
    !candidate.Cloaked &&
    !candidate.ToolWindow &&
    !candidate.NoActivate &&
    candidate.ProcessId != 0;

internal static Rectangle CalculateRelocatedBounds(
    Rectangle windowBounds,
    Rectangle sourceBounds,
    Rectangle destinationBounds)
{
    if (sourceBounds.Width <= 0 || sourceBounds.Height <= 0)
        throw new ArgumentOutOfRangeException(nameof(sourceBounds));
    if (destinationBounds.Width <= 0 || destinationBounds.Height <= 0)
        throw new ArgumentOutOfRangeException(nameof(destinationBounds));

    var width = Math.Clamp(windowBounds.Width, 1, destinationBounds.Width);
    var height = Math.Clamp(windowBounds.Height, 1, destinationBounds.Height);
    var x = destinationBounds.Left + windowBounds.Left - sourceBounds.Left;
    var y = destinationBounds.Top + windowBounds.Top - sourceBounds.Top;

    return new Rectangle(
        Math.Clamp(x, destinationBounds.Left, destinationBounds.Right - width),
        Math.Clamp(y, destinationBounds.Top, destinationBounds.Bottom - height),
        width,
        height);
}
```

- [ ] **Step 4: Run build and self-test to verify GREEN**

Run:

```powershell
dotnet build src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release --no-build -- --self-test
```

Expected: build succeeds with 0 warnings/errors; self-test prints `Self-test passed.`

- [ ] **Step 5: Commit the pure relocation logic**

```powershell
git add src/SudoVDA.GUI/SelfTest.cs src/SudoVDA.GUI/WindowRouter.cs
git commit -m "feat: calculate stop-time window relocation"
```

### Task 2: Enumerate and relocate source-display windows during Stop

**Files:**
- Modify: `src/SudoVDA.GUI/SelfTest.cs:82-110`
- Modify: `src/SudoVDA.GUI/WindowRouter.cs:90-310`
- Modify: `src/SudoVDA.GUI/MainWindow.xaml.cs:526-535`

**Interfaces:**
- Consumes: `WindowRouter.ShouldRelocate(...)`
- Consumes: `WindowRouter.CalculateRelocatedBounds(...)`
- Produces: `WindowRouter.RelocateWindows(Rectangle sourceBounds, Rectangle destinationBounds, Action<string>? reportError = null) -> void`

- [ ] **Step 1: Write a failing entry-point check**

Add after the placement checks in `SelfTest.Run`:

```csharp
Check(typeof(WindowRouter).GetMethod(
        "RelocateWindows",
        BindingFlags.Static | BindingFlags.NonPublic) is not null,
    "stop window relocation entry point");
```

- [ ] **Step 2: Run self-test and verify RED**

Run:

```powershell
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release -- --self-test
```

Expected: exit code 1 with `FAIL: stop window relocation entry point`.

- [ ] **Step 3: Implement best-effort top-level window relocation**

Add constants near the existing Win32 constants:

```csharp
private const uint MonitorDefaultToNull = 0;
```

Add this method after the pure helpers:

```csharp
internal static void RelocateWindows(
    Rectangle sourceBounds,
    Rectangle destinationBounds,
    Action<string>? reportError = null)
{
    if (sourceBounds.Width <= 0 || sourceBounds.Height <= 0)
        throw new ArgumentOutOfRangeException(nameof(sourceBounds));
    if (destinationBounds.Width <= 0 || destinationBounds.Height <= 0)
        throw new ArgumentOutOfRangeException(nameof(destinationBounds));

    var sourceRect = new NativeRect
    {
        Left = sourceBounds.Left,
        Top = sourceBounds.Top,
        Right = sourceBounds.Right,
        Bottom = sourceBounds.Bottom
    };
    var sourceMonitor = MonitorFromRect(ref sourceRect, MonitorDefaultToNull);
    if (sourceMonitor == IntPtr.Zero)
        throw new InvalidOperationException("Virtual display monitor could not be resolved.");

    var shellWindow = GetShellWindow();
    EnumWindowsDelegate callback = (window, _) =>
    {
        try
        {
            var candidate = ReadCandidate(window);
            if (!ShouldRelocate(
                    candidate,
                    GetAncestor(window, GaRoot) == window,
                    window == shellWindow,
                    MonitorFromWindow(window, MonitorDefaultToNull) == sourceMonitor) ||
                !GetWindowRect(window, out var current))
            {
                return true;
            }

            var target = CalculateRelocatedBounds(
                new Rectangle(
                    current.Left,
                    current.Top,
                    current.Right - current.Left,
                    current.Bottom - current.Top),
                sourceBounds,
                destinationBounds);

            if (!SetWindowPos(
                    window,
                    IntPtr.Zero,
                    target.Left,
                    target.Top,
                    target.Width,
                    target.Height,
                    SwpNoZOrder | SwpNoActivate))
            {
                reportError?.Invoke(
                    $"Could not relocate window 0x{window.ToInt64():X}: " +
                    new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
        }
        catch (Exception exception)
        {
            reportError?.Invoke(
                $"Could not relocate window 0x{window.ToInt64():X}: {exception.Message}");
        }

        return true;
    };

    if (!EnumWindows(callback, IntPtr.Zero))
        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate desktop windows.");

    GC.KeepAlive(callback);
}
```

Add the delegate beside `WinEventDelegate`:

```csharp
private delegate bool EnumWindowsDelegate(IntPtr window, IntPtr parameter);
```

Add these P/Invokes near the existing User32 imports:

```csharp
[DllImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
private static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr parameter);

[DllImport("user32.dll")]
private static extern IntPtr MonitorFromRect(ref NativeRect rectangle, uint flags);

[DllImport("user32.dll")]
private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
```

- [ ] **Step 4: Integrate relocation before topology restoration**

In `MainWindow.StopAsync`, after router disposal and before `DisplayController.Restore`, add:

```csharp
await TryCleanupAsync(() =>
{
    var originalPrimary = session.Snapshot.Displays.Single(display => display.Primary);
    WindowRouter.RelocateWindows(
        DisplayController.GetBounds(session.DeviceName),
        DisplayController.GetBounds(originalPrimary.DeviceName),
        errors.Add);
    return Task.CompletedTask;
}, "relocate windows", errors);
```

- [ ] **Step 5: Run build and self-test to verify GREEN**

Run:

```powershell
dotnet build src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release --no-build -- --self-test
```

Expected: build succeeds with 0 warnings/errors; self-test prints `Self-test passed.`

- [ ] **Step 6: Commit runtime relocation**

```powershell
git add src/SudoVDA.GUI/SelfTest.cs src/SudoVDA.GUI/WindowRouter.cs src/SudoVDA.GUI/MainWindow.xaml.cs
git commit -m "feat: relocate windows before stopping display"
```

### Task 3: End-user documentation and final verification

**Files:**
- Modify: `README.md:19`

**Interfaces:**
- Consumes: completed stop-time relocation behavior.
- Produces: end-user feature description.

- [ ] **Step 1: Update the feature summary**

Replace:

```markdown
- Remove the display and restore the previous layout when stopped.
```

With:

```markdown
- Move windows back to the original primary display, remove the virtual display, and restore the previous layout when stopped.
```

- [ ] **Step 2: Run final verification**

Run:

```powershell
dotnet build src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release --no-build -- --self-test
git diff --check
git status --short --branch
```

Expected: build succeeds with 0 warnings/errors; self-test passes; `git diff --check` returns no errors; only the README and this plan remain uncommitted.

- [ ] **Step 3: Commit documentation and plan**

```powershell
git add README.md docs/superpowers/plans/2026-07-23-relocate-windows-on-stop.md
git commit -m "docs: document stop-time window relocation"
```

- [ ] **Step 4: Confirm branch state**

Run:

```powershell
git status --short --branch
git log -4 --oneline
```

Expected: clean `codex/relocate-windows-on-stop` branch with design, implementation, and documentation commits.
