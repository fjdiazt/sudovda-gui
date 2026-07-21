# Custom Resolution UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add preset-backed custom Width/Height inputs, a validated Refresh dropdown, and per-user form persistence without mutating display topology during verification.

**Architecture:** Add one focused `ResolutionSettings.cs` unit for pure resolution logic, the settings record, and HKCU persistence. Rework `MainForm` to render and validate the editor while keeping `DisplayController.IsSupported` as the final driver boundary. Extend the executable self-test with pure, form-only, and temporary-registry checks.

**Tech Stack:** .NET 10, WinForms, `Microsoft.Win32.Registry`, existing assertion-style `SelfTest`; no packages.

## Global Constraints

- Width `640–7680`; Height `480–4320`.
- Refresh list: `24, 25, 30, 48, 50, 60, 72, 75, 90, 100, 120, 144, 165, 240, 360, 480, 500`, plus a current-primary integer rate within `1–500`.
- Preset selection fills Width/Height; `Copy primary` fills Width/Height/Refresh.
- Any manual Width/Height edit selects `Custom`.
- Invalid text stays visible, shows inline error, and disables `Start`; never clamp.
- Persist all controls under `HKCU\Software\VRPrivacy`; persist `Copy primary` as intent.
- Never run `--smoke-test` or mutate display topology unless separately requested.
- Use strict red-green-refactor for every behavior.

---

### Task 1: Resolution logic and settings persistence

**Files:**
- Create: `src/VrPrivacy/ResolutionSettings.cs`
- Modify: `src/VrPrivacy/SelfTest.cs`

**Interfaces:**
- Produces: `ResolutionSize`, `UserSettings`, `ResolutionOptions`, `UserSettingsStore`.
- Consumes: existing `DisplayMode` and `DisplayController.IsSupported`.

- [x] **Step 1: Write failing checks**

Add `using Microsoft.Win32;`, call `CheckResolutionSettings();` from `SelfTest.Run`, and add:

```csharp
private static void CheckResolutionSettings()
{
    var primary = new DisplayMode(3440, 1440, 119);
    var rates = ResolutionOptions.RefreshRates(primary.RefreshHz);
    Check(rates.Contains(24) && rates.Contains(500), "standard refresh rates");
    Check(rates.Contains(119), "primary refresh inclusion");

    var sizes = ResolutionOptions.DistinctSizes(
    [
        new DisplayMode(1920, 1080, 60),
        new DisplayMode(1920, 1080, 120),
        new DisplayMode(2560, 1440, 120)
    ]);
    Check(sizes.Count == 2, "resolution size deduplication");
    Check(ResolutionOptions.TryParseMode("640", "480", 60, out var minimum, out _, out _) &&
          minimum == new DisplayMode(640, 480, 60), "minimum custom mode");
    Check(!ResolutionOptions.TryParseMode("639", "480", 60, out _, out var widthError, out _) &&
          widthError == "Width must be 640–7680.", "width lower bound");
    Check(!ResolutionOptions.TryParseMode("1920", "nope", 60, out _, out _, out var heightError) &&
          heightError == "Height must be 480–4320.", "nonnumeric height");

    var path = $@"Software\VRPrivacy\Tests\{Guid.NewGuid():N}";
    try
    {
        var expected = new UserSettings("Custom", 2000, 1000, 144, false, true);
        UserSettingsStore.Save(expected, path);
        Check(UserSettingsStore.Load(primary, path) == expected, "settings registry round-trip");
        using (var key = Registry.CurrentUser.CreateSubKey(path))
            key.SetValue("Width", 639, RegistryValueKind.DWord);
        var fallback = UserSettingsStore.Load(primary, path);
        Check(fallback.Preset == UserSettings.CopyPrimary && fallback.Width == primary.Width,
            "invalid settings fallback");
    }
    finally
    {
        Registry.CurrentUser.DeleteSubKeyTree(path, false);
    }
}
```

- [x] **Step 2: Verify RED**

Run `dotnet run --project src\VrPrivacy\VrPrivacy.csproj -- --self-test`.

Expected: compiler failure naming missing `ResolutionOptions`/`UserSettingsStore`; failure comes from the absent feature.

- [x] **Step 3: Implement minimum production API**

Create `ResolutionSettings.cs` with these exact types and signatures:

```csharp
internal readonly record struct ResolutionSize(uint Width, uint Height)
{
    internal string Key => $"{Width}x{Height}";
    public override string ToString() => $"{Width} x {Height}";
}

internal sealed record UserSettings(
    string Preset, uint Width, uint Height, uint RefreshHz,
    bool MakePrimary, bool RouteNewWindows)
{
    internal const string CopyPrimary = "CopyPrimary";
    internal const string Custom = "Custom";
    internal static UserSettings Defaults(DisplayMode primary);
}

internal static class ResolutionOptions
{
    internal static IReadOnlyList<ResolutionSize> DistinctSizes(IEnumerable<DisplayMode> modes);
    internal static IReadOnlyList<uint> RefreshRates(uint primaryRefresh);
    internal static bool TryParseMode(
        string widthText, string heightText, uint refreshHz,
        out DisplayMode mode, out string widthError, out string heightError);
    internal static bool TryParsePreset(string value, out ResolutionSize size);
}

internal static class UserSettingsStore
{
    internal const string DefaultPath = @"Software\VRPrivacy";
    internal static UserSettings Load(DisplayMode primary, string path = DefaultPath);
    internal static void Save(UserSettings settings, string path = DefaultPath);
}
```

Implementation rules:

- `DistinctSizes`: project Width/Height, `Distinct`, sort Width then Height.
- `RefreshRates`: union fixed list and primary rate, retain `1–500`, distinct and sort.
- `TryParseMode`: invariant `uint` parsing after trimming; return exact range errors; call `DisplayController.IsSupported` only after both dimensions parse.
- `TryParsePreset`: accept only exact invariant `WIDTHxHEIGHT`.
- `Load`: return `UserSettings.Defaults(primary)` for missing/unreadable keys; restore booleans independently; reject invalid preset/mode/refresh as CopyPrimary defaults.
- `Save`: write `Preset` as `String`, numeric and boolean values as `DWord`.

- [x] **Step 4: Verify GREEN**

Run `dotnet run --project src\VrPrivacy\VrPrivacy.csproj -- --self-test`.

Expected: `Self-test passed.` and temporary registry key removed.

- [x] **Step 5: Commit**

```powershell
git add src\VrPrivacy\ResolutionSettings.cs src\VrPrivacy\SelfTest.cs
git commit -m "feat: add custom resolution settings"
```

---

### Task 2: Preset-backed form editor

**Files:**
- Modify: `src/VrPrivacy/MainForm.cs`
- Modify: `src/VrPrivacy/SelfTest.cs`

**Interfaces:**
- Consumes: Task 1 settings/options APIs.
- Produces: `presetCombo`, `widthText`, `heightText`, `refreshCombo`, and a validated `DisplayMode` for existing lifecycle code.

- [x] **Step 1: Write failing form checks**

Replace old `modeCombo` assertions with `CheckResolutionForm();` and add:

```csharp
private static void CheckResolutionForm()
{
    var primary = new DisplayMode(3440, 1440, 119);
    UserSettings? saved = null;
    using var form = new MainForm(
        primary,
        UserSettings.Defaults(primary),
        [primary, new DisplayMode(1920, 1080, 60)],
        value => saved = value);

    var preset = form.Controls.Find("presetCombo", true).OfType<ComboBox>().Single();
    var width = form.Controls.Find("widthText", true).OfType<TextBox>().Single();
    var height = form.Controls.Find("heightText", true).OfType<TextBox>().Single();
    var refresh = form.Controls.Find("refreshCombo", true).OfType<ComboBox>().Single();
    var primaryCheck = form.Controls.Find("primaryCheck", true).OfType<CheckBox>().Single();
    var routingCheck = form.Controls.Find("routingCheck", true).OfType<CheckBox>().Single();
    var start = form.Controls.Find("startStopButton", true).OfType<Button>().Single();

    Check(preset.SelectedItem?.ToString() == "Copy primary", "copy-primary default");
    Check(width.Text == "3440" && height.Text == "1440", "copy-primary dimensions");
    Check((uint)refresh.SelectedItem! == 119, "copy-primary refresh");
    preset.SelectedItem = preset.Items.Cast<object>().Single(item => item.ToString() == "1920 x 1080");
    Check(width.Text == "1920" && height.Text == "1080", "preset populates dimensions");
    width.Text = "2000";
    Check(preset.SelectedItem?.ToString() == "Custom", "manual edit selects custom");
    width.Text = "invalid";
    Check(!start.Enabled, "invalid width disables start");
    width.Text = "2000";
    Check(start.Enabled, "valid width enables start");
    primaryCheck.Checked = false;
    routingCheck.Checked = false;
    form.PersistSettings();
    Check(saved == new UserSettings("Custom", 2000, 1080, 119, false, false),
        "form settings persistence");
}
```

- [x] **Step 2: Verify RED**

Run `dotnet run --project src\VrPrivacy\VrPrivacy.csproj -- --self-test`.

Expected: compiler failure for missing injectable `MainForm` constructor and `PersistSettings`.

- [x] **Step 3: Replace combined mode selector**

Add always-visible controls with exact names and native WinForms types:

```csharp
private readonly ComboBox _presetCombo = new()
{
    Name = "presetCombo", Dock = DockStyle.Fill,
    DropDownStyle = ComboBoxStyle.DropDownList
};
private readonly TextBox _widthText = new() { Name = "widthText", Dock = DockStyle.Fill };
private readonly TextBox _heightText = new() { Name = "heightText", Dock = DockStyle.Fill };
private readonly ComboBox _refreshCombo = new()
{
    Name = "refreshCombo", Dock = DockStyle.Fill,
    DropDownStyle = ComboBoxStyle.DropDownList
};
private readonly ErrorProvider _resolutionErrors = new()
{
    BlinkStyle = ErrorBlinkStyle.NeverBlink
};
```

Use a 2-column, 8-row table: Preset, Width, Height, Refresh, Make primary, Route new windows, status, Start/Stop. Set client size to `460 x 300`.

Add constructor:

```csharp
internal MainForm() : this(null, null, null, null) { }

internal MainForm(
    DisplayMode? primaryMode,
    UserSettings? settings,
    IReadOnlyList<DisplayMode>? availableModes,
    Action<UserSettings>? saveSettings)
```

Default nulls resolve through `DisplayController.GetPrimaryMode`, `UserSettingsStore.Load`, `DisplayController.GetModeChoices`, and `UserSettingsStore.Save`. The injected path performs no driver or registry write.

- [x] **Step 4: Implement editor state transitions**

Add a private `PresetChoice(string Label, string Key, ResolutionSize? Size)` record. Populate choices in this order: Copy primary, `ResolutionOptions.DistinctSizes(availableModes)`, Custom. Populate refresh via `ResolutionOptions.RefreshRates(primary.RefreshHz)`.

Event behavior must be exact:

```csharp
_presetCombo.SelectedIndexChanged += (_, _) => OnPresetChanged();
_widthText.TextChanged += (_, _) => OnDimensionChanged();
_heightText.TextChanged += (_, _) => OnDimensionChanged();
_refreshCombo.SelectedIndexChanged += (_, _) => ValidateResolution();
_primaryCheck.CheckedChanged += (_, _) => UpdatePersistedChecks();
_routingCheck.CheckedChanged += (_, _) => UpdatePersistedChecks();
```

Use `_suppressResolutionEvents` around programmatic field changes. `OnPresetChanged` copies all primary fields for CopyPrimary and only dimensions for normal presets. `OnDimensionChanged` selects Custom, then validates. `TryReadMode` calls `ResolutionOptions.TryParseMode`, applies both `ErrorProvider` messages, and retains invalid text. `ValidateResolution` updates `_lastValidSettings` only on success and disables Start otherwise.

`SetUiState` enables/disables all four resolution controls together. Start/Stop enabled rule:

```csharp
_startStopButton.Enabled = !_transitioning && (_session is not null || _modeValid);
```

- [x] **Step 5: Integrate lifecycle and persistence**

At the start of `StartAsync`, require `TryReadMode(out var mode)` before opening SudoVDA. Delete the old `ModeChoice` lookup; retain the final `DisplayController.IsSupported(mode)` guard.

Add:

```csharp
internal void PersistSettings()
{
    if (_settingsSaved)
        return;
    try
    {
        _saveSettings(_lastValidSettings with
        {
            MakePrimary = _primaryCheck.Checked,
            RouteNewWindows = _routingCheck.Checked
        });
        _settingsSaved = true;
    }
    catch (Exception exception)
    {
        _statusLabel.Text = $"Settings save failed: {exception.Message}";
    }
}
```

Call `PersistSettings()` first in `OnFormClosing`; last valid resolution persists even if current textbox text is invalid. Delete old `ModeChoice`.

- [x] **Step 6: Verify GREEN and refactor**

Run:

```powershell
dotnet run --project src\VrPrivacy\VrPrivacy.csproj -- --self-test
dotnet format src\VrPrivacy\VrPrivacy.csproj --no-restore
dotnet run --project src\VrPrivacy\VrPrivacy.csproj -- --self-test
```

Expected: both self-tests end `Self-test passed.`; formatter exits `0`; no display topology changes.

- [x] **Step 7: Commit**

```powershell
git add src\VrPrivacy\MainForm.cs src\VrPrivacy\SelfTest.cs
git commit -m "feat: add custom resolution editor"
```

---

### Task 3: Documentation and completion checks

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-20-custom-resolution-ui.md`

**Interfaces:**
- Consumes: completed UI/settings behavior.
- Produces: accurate usage and verification instructions.

- [x] **Step 1: Update README**

Document Preset, Width, Height, Refresh, automatic Custom selection, registry persistence, and Copy-primary intent. Remove `Settings are not persisted.` Replace `Mutation-free checks:` with `Form, protocol, and temporary-registry checks (no display topology mutation):`.

- [x] **Step 2: Run fresh permitted verification**

Run only:

```powershell
git diff --check
dotnet build src\VrPrivacy\VrPrivacy.csproj -c Release
dotnet run --project src\VrPrivacy\VrPrivacy.csproj -c Release --no-build -- --self-test
```

Expected: no whitespace errors; build has `0 Warning(s)` and `0 Error(s)`; test ends `Self-test passed.` Do not run `--smoke-test`.

- [x] **Step 3: Commit docs and checked plan**

Mark completed checkboxes in this plan, then:

```powershell
git add README.md docs\superpowers\plans\2026-07-20-custom-resolution-ui.md
git commit -m "docs: explain custom resolution controls"
```

- [x] **Step 4: Final branch audit**

Run:

```powershell
git diff master...HEAD --check
git status --short
git log --oneline --decorate -5
```

Expected: diff check exits `0`; status empty; feature commits at branch tip.
