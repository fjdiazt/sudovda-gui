# SudoVDA GUI Layout and Rename Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Improve the compact WinForms layout and consistently rename the application, project, assembly, executable, source folder, and namespaces to SudoVDA GUI.

**Architecture:** Keep the existing programmatic WinForms form and lifecycle code. Recompose only its native controls into two group boxes plus a footer, and add one native colored-label status dot. Rename project identity in place without migrating the existing registry settings key.

**Tech Stack:** .NET 10, Windows Forms, C#, existing executable `--self-test` harness, Git.

## Global Constraints

- Window title is `SudoVDA`.
- Project is `src/SudoVDA.GUI/SudoVDA.GUI.csproj`.
- Assembly and executable basename is `SudoVDA-GUI`.
- Root namespace and every source namespace is `SudoVDA.GUI`.
- Preserve display lifecycle, routing, resolution validation, presets, and persistence behavior.
- Preserve the registry key `HKCU\Software\VRPrivacy` so existing settings survive the rename.
- Use only native WinForms controls and existing dependencies.
- Do not run `--smoke-test` or mutate display settings.
- Rename `C:\src\vr-privacy` to `C:\src\SudoVDA-GUI` only after this branch is integrated and active worktree/process locks are removed.

---

### Task 1: Rename Application Identity

**Files:**
- Move: `src/VrPrivacy` to `src/SudoVDA.GUI`
- Move: `src/SudoVDA.GUI/VrPrivacy.csproj` to `src/SudoVDA.GUI/SudoVDA.GUI.csproj`
- Modify: every `*.cs` file under `src/SudoVDA.GUI`
- Modify: `src/SudoVDA.GUI/SudoVDA.GUI.csproj`
- Test: `src/SudoVDA.GUI/SelfTest.cs`

**Interfaces:**
- Consumes: existing `SelfTest.Run()` executable test entry point.
- Produces: `SudoVDA.GUI` namespace, `SudoVDA.GUI.csproj`, and `SudoVDA-GUI.exe`.

- [x] **Step 1: Write failing identity checks before moving files**

In `src/VrPrivacy/SelfTest.cs`, add `using System.Reflection;`, then add this check at the start of `Run()`:

```csharp
Check(Assembly.GetExecutingAssembly().GetName().Name == "SudoVDA-GUI", "assembly name");
```

Change the existing form-title expectation to:

```csharp
Check(form.Text == "SudoVDA", "main window title");
```

- [x] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet run --project src\VrPrivacy\VrPrivacy.csproj -c Release -- --self-test
```

Expected: exit code 1 with `FAIL: assembly name` and `FAIL: main window title`; no display topology changes.

- [x] **Step 3: Rename files, namespaces, assembly, and titles**

Move the source directory and project file with Git:

```powershell
git mv src\VrPrivacy src\SudoVDA.GUI
git mv src\SudoVDA.GUI\VrPrivacy.csproj src\SudoVDA.GUI\SudoVDA.GUI.csproj
```

In every source file under `src/SudoVDA.GUI`, replace:

```csharp
namespace VrPrivacy;
```

with:

```csharp
namespace SudoVDA.GUI;
```

In `src/SudoVDA.GUI/SudoVDA.GUI.csproj`, add inside `<PropertyGroup>`:

```xml
<AssemblyName>SudoVDA-GUI</AssemblyName>
<RootNamespace>SudoVDA.GUI</RootNamespace>
```

In `MainForm.cs`, set:

```csharp
Text = "SudoVDA";
```

In `SmokeTest.cs`, set the test-window title to:

```csharp
Text = "SudoVDA Smoke Window",
```

- [x] **Step 4: Run identity checks and verify GREEN**

Run:

```powershell
dotnet build src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release --no-build -- --self-test
Test-Path src\SudoVDA.GUI\bin\Release\net10.0-windows\SudoVDA-GUI.exe
```

Expected: build succeeds with zero warnings and errors, self-test passes, and `Test-Path` returns `True`.

- [x] **Step 5: Commit identity rename**

```powershell
git add src
git commit -m "refactor: rename app to SudoVDA GUI"
```

---

### Task 2: Recompose the Main Form

**Files:**
- Modify: `src/SudoVDA.GUI/MainForm.cs`
- Test: `src/SudoVDA.GUI/SelfTest.cs`

**Interfaces:**
- Consumes: existing form controls and `SetUiState(string status, bool busy, bool active)` lifecycle seam.
- Produces: named `displayGroup`, `behaviorGroup`, `resolutionLayout`, `widthLabel`, `heightLabel`, `refreshLabel`, and `statusIndicator` controls; `SetUiState(string, bool, bool, bool error = false)`.

- [x] **Step 1: Write failing layout and color checks**

In `CheckResolutionForm()`, find the new controls:

```csharp
var displayGroup = form.Controls.Find("displayGroup", true).OfType<GroupBox>().SingleOrDefault();
var behaviorGroup = form.Controls.Find("behaviorGroup", true).OfType<GroupBox>().SingleOrDefault();
var resolutionLayout = form.Controls.Find("resolutionLayout", true).OfType<TableLayoutPanel>().SingleOrDefault();
var widthLabel = form.Controls.Find("widthLabel", true).OfType<Label>().SingleOrDefault();
var heightLabel = form.Controls.Find("heightLabel", true).OfType<Label>().SingleOrDefault();
var refreshLabel = form.Controls.Find("refreshLabel", true).OfType<Label>().SingleOrDefault();
var statusIndicator = form.Controls.Find("statusIndicator", true).OfType<Label>().SingleOrDefault();
```

After the default checks, add:

```csharp
Check(displayGroup?.Text == "Display", "display group");
Check(behaviorGroup?.Text == "Behavior", "behavior group");
Check(resolutionLayout is not null, "resolution row layout");
if (resolutionLayout is not null)
{
    Check(resolutionLayout.GetPositionFromControl(width).Column == 0 &&
          resolutionLayout.GetPositionFromControl(width).Row == 3 &&
          resolutionLayout.GetPositionFromControl(height).Column == 1 &&
          resolutionLayout.GetPositionFromControl(height).Row == 3 &&
          resolutionLayout.GetPositionFromControl(refresh).Column == 2 &&
          resolutionLayout.GetPositionFromControl(refresh).Row == 3,
        "dimension controls share one row");
    Check(widthLabel is not null && heightLabel is not null && refreshLabel is not null &&
          resolutionLayout.GetPositionFromControl(widthLabel).Row == 2 &&
          resolutionLayout.GetPositionFromControl(heightLabel).Row == 2 &&
          resolutionLayout.GetPositionFromControl(refreshLabel).Row == 2,
        "dimension labels share row above controls");
}
Check(statusIndicator?.ForeColor == Color.Firebrick, "stopped status color");
form.SetUiState("Starting...", true, false);
Check(statusIndicator?.ForeColor == Color.DarkOrange, "busy status color");
form.SetUiState("Active", false, true);
Check(statusIndicator?.ForeColor == Color.ForestGreen, "active status color");
form.SetUiState("Stop failed", false, true, true);
Check(statusIndicator?.ForeColor == Color.Firebrick, "error status color");
```

- [x] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet build src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release
```

Expected: compilation fails because the four-argument `SetUiState` overload does not exist. This proves the error-state contract is absent.

- [x] **Step 3: Add the native status indicator**

In `MainForm.cs`, add beside `_statusLabel`:

```csharp
private readonly Label _statusIndicator = new()
{
    Name = "statusIndicator",
    Text = "●",
    AutoSize = true,
    ForeColor = Color.Firebrick,
    Anchor = AnchorStyles.Left
};
```

Change `SetUiState` to:

```csharp
internal void SetUiState(string status, bool busy, bool active, bool error = false)
{
    _statusLabel.Text = status;
    _statusIndicator.ForeColor = error
        ? Color.Firebrick
        : busy
            ? Color.DarkOrange
            : active
                ? Color.ForestGreen
                : Color.Firebrick;
    _busy = busy;
    var resolutionEnabled = !busy && !active;
    _presetCombo.Enabled = resolutionEnabled;
    _widthText.Enabled = resolutionEnabled;
    _heightText.Enabled = resolutionEnabled;
    _refreshCombo.Enabled = resolutionEnabled;
    _primaryCheck.Enabled = resolutionEnabled;
    _routingCheck.Enabled = resolutionEnabled;
    _startStopButton.Text = active ? "Stop" : "Start";
    UpdateStartStopEnabled();
}
```

Pass `error: true` from the `Stop failed:` state. Set the indicator to `Color.Firebrick` when `PersistSettings()` or `ReportBackgroundError()` reports an error.

- [x] **Step 4: Replace the constructor layout with two groups and footer**

Keep all existing control instances and event handlers. Replace only the constructor's layout composition with native `TableLayoutPanel`, `GroupBox`, and `FlowLayoutPanel` controls:

```csharp
ClientSize = new Size(500, 280);

var resolutionLayout = new TableLayoutPanel
{
    Name = "resolutionLayout",
    Dock = DockStyle.Fill,
    AutoSize = true,
    ColumnCount = 3,
    RowCount = 4,
    Padding = new Padding(8)
};
for (var column = 0; column < 3; column++)
    resolutionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3f));
var presetLabel = CreateFieldLabel("presetLabel", "Resolution preset");
resolutionLayout.Controls.Add(presetLabel, 0, 0);
resolutionLayout.SetColumnSpan(presetLabel, 3);
resolutionLayout.Controls.Add(_presetCombo, 0, 1);
resolutionLayout.SetColumnSpan(_presetCombo, 3);
resolutionLayout.Controls.Add(CreateFieldLabel("widthLabel", "Width"), 0, 2);
resolutionLayout.Controls.Add(CreateFieldLabel("heightLabel", "Height"), 1, 2);
resolutionLayout.Controls.Add(CreateFieldLabel("refreshLabel", "Refresh rate"), 2, 2);
resolutionLayout.Controls.Add(_widthText, 0, 3);
resolutionLayout.Controls.Add(_heightText, 1, 3);
resolutionLayout.Controls.Add(_refreshCombo, 2, 3);

var displayGroup = new GroupBox
{
    Name = "displayGroup",
    Text = "Display",
    Dock = DockStyle.Fill,
    AutoSize = true
};
displayGroup.Controls.Add(resolutionLayout);

var behaviorFlow = new FlowLayoutPanel
{
    Dock = DockStyle.Fill,
    AutoSize = true,
    FlowDirection = FlowDirection.LeftToRight,
    Padding = new Padding(8, 4, 8, 4)
};
behaviorFlow.Controls.Add(_primaryCheck);
behaviorFlow.Controls.Add(_routingCheck);
var behaviorGroup = new GroupBox
{
    Name = "behaviorGroup",
    Text = "Behavior",
    Dock = DockStyle.Fill,
    AutoSize = true
};
behaviorGroup.Controls.Add(behaviorFlow);

var footer = new TableLayoutPanel
{
    Name = "footerLayout",
    Dock = DockStyle.Fill,
    AutoSize = true,
    ColumnCount = 3
};
footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
footer.Controls.Add(_statusIndicator, 0, 0);
footer.Controls.Add(_statusLabel, 1, 0);
footer.Controls.Add(_startStopButton, 2, 0);

var layout = new TableLayoutPanel
{
    Dock = DockStyle.Fill,
    Padding = new Padding(12),
    ColumnCount = 1,
    RowCount = 4
};
layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
layout.Controls.Add(displayGroup, 0, 0);
layout.Controls.Add(behaviorGroup, 0, 1);
layout.Controls.Add(footer, 0, 3);
Controls.Add(layout);
```

Replace the obsolete `AddRow` helper with:

```csharp
private static Label CreateFieldLabel(string name, string text) => new()
{
    Name = name,
    Text = text,
    AutoSize = true,
    Anchor = AnchorStyles.Left
};
```

- [x] **Step 5: Run tests and verify GREEN**

Run:

```powershell
dotnet build src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release --no-build -- --self-test
```

Expected: zero build warnings/errors and `Self-test passed.` No display topology changes.

- [x] **Step 6: Commit layout**

```powershell
git add src\SudoVDA.GUI\MainForm.cs src\SudoVDA.GUI\SelfTest.cs
git commit -m "feat: improve SudoVDA GUI layout"
```

---

### Task 3: Update User Documentation and Verify Deliverables

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: final project path, executable name, title, and existing command-line test switches.
- Produces: current build/run/test instructions and UI description.

- [x] **Step 1: Update README identity and commands**

Change the heading to:

```markdown
# SudoVDA GUI
```

Replace every `src\VrPrivacy\VrPrivacy.csproj` command path with:

```text
src\SudoVDA.GUI\SudoVDA.GUI.csproj
```

Describe the UI grouping and status colors under `UI controls`, while retaining the existing validation, persistence, and smoke-test warning text.

- [x] **Step 2: Verify no stale live-code identity remains**

Run:

```powershell
rg -n "namespace VrPrivacy|VR Privacy|src\\VrPrivacy\\VrPrivacy.csproj" src README.md
```

Expected: no matches. Historical documents under `docs/superpowers` are intentionally excluded.

- [x] **Step 3: Run final non-display verification**

Run:

```powershell
dotnet build src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release --no-build -- --self-test
Test-Path src\SudoVDA.GUI\bin\Release\net10.0-windows\SudoVDA-GUI.exe
git diff --check
git status --short
```

Expected: zero warnings/errors, `Self-test passed.`, executable path `True`, no whitespace errors, and only intended README/plan changes before commit.

- [x] **Step 4: Commit documentation and plan**

```powershell
git add README.md docs\superpowers\plans\2026-07-20-sudovda-gui-layout-and-rename.md
git commit -m "docs: update SudoVDA GUI usage"
```

---

## Post-Implementation Integration

After all tasks pass, use `superpowers:finishing-a-development-branch`. Once the feature branch is integrated and the linked worktree is removed, verify no process has its current directory inside `C:\src\vr-privacy`, then rename the repository root to `C:\src\SudoVDA-GUI`. Re-run the Release build and `--self-test` from the renamed root. Do not run `--smoke-test`.
