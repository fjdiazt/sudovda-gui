# Pure WPF Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace SudoVDA.GUI's Windows Forms UI with a pure WPF UI using one default dark theme while preserving all current behavior and command-line checks.

**Architecture:** Keep the native driver, display, routing, settings, and resolution types unchanged. Replace only the application host, main window, UI-facing self-tests, and smoke-test window/monitor lookup. Use XAML for structure/theme and direct code-behind for the existing one-window state machine.

**Tech Stack:** C# 14, .NET 10 WPF, XAML, User32 P/Invoke, existing executable self-test.

## Global Constraints

- Work only on branch `codex/wpf-migration` in `.worktrees/wpf-migration`.
- Remove `UseWindowsForms` and every Windows Forms type/reference; do not leave a hybrid compatibility path.
- Provide one default dark theme with no selector or system-theme branch.
- Preserve all labels, control order, settings, aspect filtering, aspect lock, lifecycle, cleanup, and CLI switches.
- Keep the assembly name `SudoVDA-GUI`, registry schema, monitor GUID, driver protocol, and native controller behavior unchanged.
- Run Release `--self-test` and Release build. Do not run `--smoke-test` automatically.

---

### Task 1: WPF application host and dark window

**Files:**
- Modify: `src/SudoVDA.GUI/SudoVDA.GUI.csproj`
- Delete: `src/SudoVDA.GUI/Program.cs`
- Create: `src/SudoVDA.GUI/App.xaml`
- Create: `src/SudoVDA.GUI/App.xaml.cs`
- Create: `src/SudoVDA.GUI/MainWindow.xaml`
- Rename/Modify: `src/SudoVDA.GUI/MainForm.cs` → `src/SudoVDA.GUI/MainWindow.xaml.cs`
- Modify: `src/SudoVDA.GUI/SmokeTest.cs`
- Modify: `src/SudoVDA.GUI/SelfTest.cs`

**Interfaces:**
- Consumes: `DisplayController`, `UserSettingsStore`, `ResolutionOptions`, `SudoVdaClient`, `WindowRouter`.
- Produces: `App : Application`, `MainWindow : Window`, and named WPF controls matching the existing logical names.
- Preserves: `MainWindow(DisplayMode?, UserSettings?, IReadOnlyList<DisplayMode>?, Action<UserSettings>?)`, `PersistSettings()`, and `SetUiState(...)` for tests.

- [ ] **Step 1: Add a failing pure-WPF assembly check**

At the start of `SelfTest.Run()`, add:

```csharp
Check(!Assembly.GetExecutingAssembly().GetReferencedAssemblies()
    .Any(reference => reference.Name == "System.Windows.Forms"),
    "no Windows Forms assembly reference");
```

- [ ] **Step 2: Run the self-test and verify RED**

Run:

```powershell
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release -- --self-test
```

Expected: `FAIL: no Windows Forms assembly reference` and a nonzero exit code.

- [ ] **Step 3: Switch the project SDK UI flag**

Replace:

```xml
<UseWindowsForms>true</UseWindowsForms>
```

with:

```xml
<UseWPF>true</UseWPF>
```

Delete `Program.cs`; WPF's generated entry point will call `App.OnStartup`.

- [ ] **Step 4: Add the WPF host and CLI dispatch**

Create `App.xaml` with `x:Class="SudoVDA.GUI.App"`, no `StartupUri`, and dark brush/control resources. Required brush keys:

```xml
<SolidColorBrush x:Key="WindowBackgroundBrush" Color="#181A1F" />
<SolidColorBrush x:Key="PanelBackgroundBrush" Color="#23262D" />
<SolidColorBrush x:Key="ControlBackgroundBrush" Color="#30343D" />
<SolidColorBrush x:Key="ForegroundBrush" Color="#F2F3F5" />
<SolidColorBrush x:Key="MutedForegroundBrush" Color="#B7BCC6" />
<SolidColorBrush x:Key="AccentBrush" Color="#4C9AFF" />
<SolidColorBrush x:Key="ErrorBrush" Color="#FF6B6B" />
<SolidColorBrush x:Key="BusyBrush" Color="#F5A623" />
<SolidColorBrush x:Key="ActiveBrush" Color="#45C46A" />
```

Create `App.xaml.cs`:

```csharp
namespace SudoVDA.GUI;

internal partial class App : Application
{
    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        if (eventArgs.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(SelfTest.Run());
            return;
        }
        if (eventArgs.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(SmokeTest.Run());
            return;
        }
        if (eventArgs.Args.Contains("--smoke-window", StringComparer.OrdinalIgnoreCase))
        {
            MainWindow = SmokeTest.CreateTestWindow();
            MainWindow.Show();
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
```

- [ ] **Step 5: Declare the complete dark WPF visual tree**

Create `MainWindow.xaml` as a fixed-size centered WPF window. Use a root `Grid` containing two `GroupBox` controls and a footer. Declare these exact names and types:

```xml
<ComboBox x:Name="aspectCombo" Grid.Row="1" Grid.ColumnSpan="4" />
<ComboBox x:Name="presetCombo" Grid.Row="3" Grid.ColumnSpan="4" />
<TextBox x:Name="widthText" Grid.Row="5" Grid.Column="0" />
<TextBox x:Name="heightText" Grid.Row="5" Grid.Column="1" />
<ToggleButton x:Name="aspectLockButton" Grid.Row="5" Grid.Column="2"
              Content="🔓" ToolTip="Lock aspect ratio"
              AutomationProperties.Name="Lock aspect ratio" />
<ComboBox x:Name="refreshCombo" Grid.Row="5" Grid.Column="3" />
<CheckBox x:Name="primaryCheck" Content="Make primary" />
<CheckBox x:Name="routingCheck" Content="Route new windows" />
<TextBlock x:Name="statusIndicator" Text="●" />
<TextBlock x:Name="statusLabel" Text="Stopped" />
<Button x:Name="startStopButton" Content="Start" IsDefault="True" />
```

Use `Label` elements associated through `Target` for Aspect ratio, Resolution preset, Width, Height, and Refresh rate. Apply the dark resources through local implicit WPF styles; include visible hover, focus, disabled, and checked states.

- [ ] **Step 6: Port the main-window code-behind**

Rename the class to `MainWindow : Window`, call `InitializeComponent()` first, and replace WinForms properties/events with WPF equivalents:

```csharp
aspectCombo.SelectionChanged += (_, _) => OnAspectChanged();
presetCombo.SelectionChanged += (_, _) => OnPresetChanged();
widthText.TextChanged += (_, _) => OnDimensionChanged(true);
heightText.TextChanged += (_, _) => OnDimensionChanged(false);
aspectLockButton.Checked += (_, _) => OnAspectLockChanged();
aspectLockButton.Unchecked += (_, _) => OnAspectLockChanged();
refreshCombo.SelectionChanged += (_, _) => ValidateResolution();
primaryCheck.Checked += (_, _) => UpdatePersistedChecks();
primaryCheck.Unchecked += (_, _) => UpdatePersistedChecks();
routingCheck.Checked += (_, _) => UpdatePersistedChecks();
routingCheck.Unchecked += (_, _) => UpdatePersistedChecks();
startStopButton.Click += async (_, _) => await ToggleAsync();
Closing += OnWindowClosing;
```

Use `Items.Add`, `Items.Clear`, `SelectedItem`, `SelectedIndex`, `IsChecked == true`, `IsEnabled`, and `Content` in place of their WinForms counterparts. Set validation directly:

```csharp
private void SetValidation(TextBox textBox, string? message)
{
    textBox.ToolTip = message;
    textBox.BorderBrush = string.IsNullOrEmpty(message)
        ? (Brush)FindResource("ControlBorderBrush")
        : (Brush)FindResource("ErrorBrush");
}
```

Marshal background UI updates with:

```csharp
if (!Dispatcher.CheckAccess())
{
    Dispatcher.BeginInvoke(() => ReportBackgroundError(message));
    return;
}
```

Handle closing with `CancelEventArgs`, `Dispatcher.BeginInvoke`, and `Close()`. Remove WinForms disposal overrides because WPF controls/resources need no manual disposal.

- [ ] **Step 7: Replace WinForms smoke-test support**

Return a WPF window:

```csharp
internal static Window CreateTestWindow() => new()
{
    Title = "SudoVDA Smoke Window",
    Width = 640,
    Height = 480,
    WindowStartupLocation = WindowStartupLocation.Manual,
    Left = 40,
    Top = 40,
    Background = Brushes.Black
};
```

Replace `Screen.FromHandle(window).DeviceName` with `MonitorFromWindow` and `GetMonitorInfoW` using a `MONITORINFOEX` structure whose `DeviceName` field has `SizeConst = 32`. Pump queued WPF work during polling with:

```csharp
Dispatcher.CurrentDispatcher.Invoke(
    DispatcherPriority.Background,
    new Action(() => { }));
```

- [ ] **Step 8: Rewrite UI self-checks for WPF**

Use `FindName` and WPF types:

```csharp
var aspect = (ComboBox)window.FindName("aspectCombo");
var width = (TextBox)window.FindName("widthText");
var aspectLock = (ToggleButton)window.FindName("aspectLockButton");
var statusIndicator = (TextBlock)window.FindName("statusIndicator");
```

Retain every existing behavior assertion. Replace WinForms layout assertions with `Grid.GetRow(...)` / `Grid.GetColumn(...)`; replace `Text`, `Checked`, `Enabled`, and `ForeColor` assertions with `Content`, `IsChecked`, `IsEnabled`, and `Foreground`. Add checks that the window background is `WindowBackgroundBrush` and control backgrounds use dark resources.

- [ ] **Step 9: Run GREEN verification and commit**

Run:

```powershell
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release -- --self-test
dotnet build src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release
rg -n "UseWindowsForms|System\.Windows\.Forms|MainForm|ApplicationConfiguration" src\SudoVDA.GUI
```

Expected: self-test passes; build has zero warnings/errors; `rg` returns no matches.

Commit:

```powershell
git add -- src/SudoVDA.GUI
git commit -m "feat: migrate main window to WPF"
```

---

### Task 2: Documentation and completion audit

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: completed pure-WPF application and verification commands.
- Produces: user-facing WPF and dark-theme documentation.

- [ ] **Step 1: Update README**

Change the introduction to identify the app as a WPF GUI. Add:

```markdown
The interface uses one built-in dark theme.
```

Keep all existing control and behavioral documentation unchanged.

- [ ] **Step 2: Run complete non-display verification**

Run:

```powershell
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release -- --self-test
dotnet build src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release
rg -n "UseWindowsForms|System\.Windows\.Forms|MainForm|ApplicationConfiguration" . --glob '!docs/superpowers/**'
git diff --check
git status --short
```

Expected: self-test passes; build has zero warnings/errors; source scan has no matches; diff check is clean; only intended README change remains.

- [ ] **Step 3: Commit documentation**

```powershell
git add -- README.md
git commit -m "docs: document WPF dark interface"
```

- [ ] **Step 4: Audit final branch**

Run:

```powershell
git status --short --branch
git diff master...HEAD --stat
git log --oneline master..HEAD
```

Expected: clean `codex/wpf-migration` branch containing design, plan, implementation, and documentation commits.
