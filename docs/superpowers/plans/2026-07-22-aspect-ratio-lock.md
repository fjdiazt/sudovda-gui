# Aspect-Ratio Lock Button Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a compact lock-icon toggle that keeps manually edited Width and Height proportional.

**Architecture:** Extend the existing WinForms dimension row with a button-appearance `CheckBox`. `MainForm` captures the current dimensions when locked, scales the opposite field during one manual edit, and uses existing event suppression and validation to avoid recursion or silent clamping.

**Tech Stack:** C# 14, .NET 10 Windows Forms, existing executable `--self-test`; no new dependency or image asset.

## Global Constraints

- Visible control is one icon-only toggle button after Height and before Refresh rate.
- Unlocked/default icon is `🔓`; locked/pressed icon is `🔒`.
- Accessible name and tooltip are `Lock aspect ratio` or `Unlock aspect ratio`.
- Lock state is not persisted.
- Existing registry schema, preset keys, validation bounds, and driver behavior stay unchanged.
- Run Release `--self-test` and Release build only. Never run `--smoke-test`.

---

### Task 1: Lock-button layout and proportional editing

**Files:**
- Modify: `src/SudoVDA.GUI/SelfTest.cs`
- Modify: `src/SudoVDA.GUI/MainForm.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: existing `ResolutionSize`, `SetModeText`, `OnDimensionChanged`, `TryReadMode`, and `SetUiState`.
- Produces: named WinForms toggle `aspectLockButton`, `OnAspectLockChanged()`, `TryScaleLockedDimension(...)`, and `UpdateAspectLockButton()`.
- Preserves: `UserSettings`, `PresetChoice`, refresh handling, and display lifecycle.

- [ ] **Step 1: Write failing form checks**

Call a new method after `CheckResolutionForm();` in `SelfTest.Run()`:

```csharp
CheckAspectLockForm();
```

Add:

```csharp
private static void CheckAspectLockForm()
{
    var primary = new DisplayMode(1920, 1080, 60);
    using var form = new MainForm(
        primary,
        UserSettings.Defaults(primary),
        [primary, new DisplayMode(1920, 1200, 60)],
        _ => { });

    var preset = form.Controls.Find("presetCombo", true).OfType<ComboBox>().Single();
    var width = form.Controls.Find("widthText", true).OfType<TextBox>().Single();
    var height = form.Controls.Find("heightText", true).OfType<TextBox>().Single();
    var aspectLock = form.Controls.Find("aspectLockButton", true).OfType<CheckBox>().Single();
    var layout = form.Controls.Find("resolutionLayout", true).OfType<TableLayoutPanel>().Single();

    Check(!aspectLock.Checked && aspectLock.Text == "🔓" &&
          aspectLock.AccessibleName == "Lock aspect ratio",
        "aspect lock default");
    Check(layout.ColumnCount == 4 &&
          layout.GetPositionFromControl(width).Column == 0 &&
          layout.GetPositionFromControl(height).Column == 1 &&
          layout.GetPositionFromControl(aspectLock).Column == 2,
        "aspect lock layout");

    aspectLock.Checked = true;
    Check(aspectLock.Text == "🔒" &&
          aspectLock.AccessibleName == "Unlock aspect ratio",
        "aspect lock enabled");

    width.Text = "2000";
    Check(height.Text == "1125" && preset.SelectedItem?.ToString() == "Custom",
        "locked width updates height");
    height.Text = "1200";
    Check(width.Text == "2133", "locked height updates width");

    preset.SelectedItem = preset.Items.Cast<object>()
        .Single(item => item.ToString() == "1920 x 1200 (16:10 Wide)");
    width.Text = "2000";
    Check(height.Text == "1250", "preset refreshes locked ratio");

    aspectLock.Checked = false;
    width.Text = "invalid";
    aspectLock.Checked = true;
    Check(!aspectLock.Checked && aspectLock.Text == "🔓",
        "invalid dimensions refuse aspect lock");

    form.SetUiState("Active", false, true);
    Check(!aspectLock.Enabled, "active display locks aspect button");
    form.SetUiState("Stopped", false, false);
    Check(aspectLock.Enabled, "stopped display unlocks aspect button");
}
```

In the existing layout checks, change the refresh control and label expected column from `2` to `3`.

- [ ] **Step 2: Run self-test and verify RED**

Run:

```powershell
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release -- --self-test
```

Expected: runtime failure from `Single()` because `aspectLockButton` does not exist. This is a non-display test; temporary HKCU access may require sandbox escalation.

- [ ] **Step 3: Add native icon toggle and four-column layout**

Add fields beside the other resolution controls:

```csharp
private readonly CheckBox _aspectLockButton = new()
{
    Name = "aspectLockButton",
    Appearance = Appearance.Button,
    Text = "🔓",
    AccessibleName = "Lock aspect ratio",
    TextAlign = ContentAlignment.MiddleCenter,
    Dock = DockStyle.Fill,
    AutoSize = true
};
private readonly ToolTip _toolTip = new();
```

Add form state:

```csharp
private ResolutionSize? _lockedAspectRatio;
```

Change `resolutionLayout.ColumnCount` to `4`. Replace the three-column style loop with:

```csharp
resolutionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3f));
resolutionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3f));
resolutionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
resolutionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3f));
```

Change both full-row spans from `3` to `4`. Put Width in column `0`, Height in `1`, the lock button in `2`, and Refresh rate in `3`:

```csharp
resolutionLayout.Controls.Add(CreateFieldLabel("widthLabel", "Width"), 0, 4);
resolutionLayout.Controls.Add(CreateFieldLabel("heightLabel", "Height"), 1, 4);
resolutionLayout.Controls.Add(CreateFieldLabel("refreshLabel", "Refresh rate"), 3, 4);
resolutionLayout.Controls.Add(_widthText, 0, 5);
resolutionLayout.Controls.Add(_heightText, 1, 5);
resolutionLayout.Controls.Add(_aspectLockButton, 2, 5);
resolutionLayout.Controls.Add(_refreshCombo, 3, 5);
```

- [ ] **Step 4: Wire lock state and proportional edits**

Replace the two dimension subscriptions and add the lock subscription:

```csharp
_widthText.TextChanged += (_, _) => OnDimensionChanged(true);
_heightText.TextChanged += (_, _) => OnDimensionChanged(false);
_aspectLockButton.CheckedChanged += (_, _) => OnAspectLockChanged();
```

Call `UpdateAspectLockButton();` before the constructor's final `ValidateResolution();`.

Replace `OnDimensionChanged()` with:

```csharp
private void OnDimensionChanged(bool widthChanged)
{
    if (_suppressResolutionEvents)
        return;

    if (_aspectLockButton.Checked &&
        _lockedAspectRatio is { } ratio &&
        TryScaleLockedDimension(
            widthChanged ? _widthText.Text : _heightText.Text,
            ratio,
            widthChanged,
            out var scaled))
    {
        var suppressed = _suppressResolutionEvents;
        _suppressResolutionEvents = true;
        if (widthChanged)
            _heightText.Text = scaled.ToString();
        else
            _widthText.Text = scaled.ToString();
        _suppressResolutionEvents = suppressed;
    }

    SelectPreset(UserSettings.Custom);
    ValidateResolution();
}
```

Add:

```csharp
private void OnAspectLockChanged()
{
    if (_suppressResolutionEvents)
        return;

    if (_aspectLockButton.Checked && TryReadMode(out var mode))
    {
        _lockedAspectRatio = new ResolutionSize(mode.Width, mode.Height);
    }
    else
    {
        _lockedAspectRatio = null;
        if (_aspectLockButton.Checked)
        {
            _suppressResolutionEvents = true;
            _aspectLockButton.Checked = false;
            _suppressResolutionEvents = false;
        }
    }

    UpdateAspectLockButton();
}

private static bool TryScaleLockedDimension(
    string text,
    ResolutionSize ratio,
    bool widthChanged,
    out uint scaled)
{
    scaled = 0;
    if (!uint.TryParse(text.Trim(), out var input))
        return false;

    var value = widthChanged
        ? input * (double)ratio.Height / ratio.Width
        : input * (double)ratio.Width / ratio.Height;
    if (value < 1 || value > uint.MaxValue)
        return false;

    scaled = (uint)Math.Round(value, MidpointRounding.AwayFromZero);
    return true;
}

private void UpdateAspectLockButton()
{
    var locked = _aspectLockButton.Checked && _lockedAspectRatio is not null;
    _aspectLockButton.Text = locked ? "🔒" : "🔓";
    _aspectLockButton.AccessibleName = locked
        ? "Unlock aspect ratio"
        : "Lock aspect ratio";
    _toolTip.SetToolTip(_aspectLockButton, _aspectLockButton.AccessibleName);
}
```

At the end of `SetModeText`, before restoring event suppression, refresh the captured ratio when locked:

```csharp
if (_aspectLockButton.Checked)
    _lockedAspectRatio = new ResolutionSize(mode.Width, mode.Height);
```

- [ ] **Step 5: Integrate lifecycle and disposal**

In `SetUiState`, add:

```csharp
_aspectLockButton.Enabled = resolutionEnabled;
```

Dispose the tooltip with the existing error provider:

```csharp
if (disposing)
{
    _toolTip.Dispose();
    _resolutionErrors.Dispose();
}
```

- [ ] **Step 6: Update user documentation**

Add this UI-control bullet after Width/Height:

```markdown
- `🔓` / `🔒`: toggle proportional Width/Height editing. Lock state is not saved.
```

Add this behavior sentence:

```markdown
When aspect-ratio lock is enabled, editing either dimension rounds the other to the nearest whole pixel while preserving the captured ratio.
```

- [ ] **Step 7: Run GREEN verification**

Run:

```powershell
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release -- --self-test
dotnet build src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release
```

Expected: `Self-test passed.`; build reports `0 Warning(s)` and `0 Error(s)`. Do not run `--smoke-test`.

- [ ] **Step 8: Commit**

```powershell
git add -- README.md src/SudoVDA.GUI/MainForm.cs src/SudoVDA.GUI/SelfTest.cs
git commit -m "feat: lock manual resolution aspect ratio"
```

