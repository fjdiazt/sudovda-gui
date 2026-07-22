# Aspect-Ratio Resolution Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an aspect-ratio filter, aspect-first preset ordering, ratio annotations in the unfiltered list, and user-friendly `Match primary display` copy.

**Architecture:** Keep classification and ordering pure in `ResolutionOptions`. Cache one sorted size list in `MainForm`; rebuild only the preset dropdown when the view-only aspect filter changes. Existing resolution selection, validation, persistence, and driver boundaries remain authoritative.

**Tech Stack:** C# 14, .NET 10 Windows Forms, existing executable `--self-test`; no new dependencies.

## Global Constraints

- Visible copy text is `Match primary display`; persisted key remains `CopyPrimary`.
- Aspect filter is not persisted and adds no resolutions.
- `Match primary display` remains first; `Custom` remains last.
- All five resolution controls lock while display is active or transitioning.
- Run Release build and `--self-test` only. Never run `--smoke-test`.

---

### Task 1: Pure aspect-ratio classification and ordering

**Files:**
- Modify: `src/SudoVDA.GUI/SelfTest.cs`
- Modify: `src/SudoVDA.GUI/ResolutionSettings.cs`

**Interfaces:**
- Produces: `ResolutionAspectRatio(uint Numerator, uint Denominator, string? Label)` with `Value`, `Key`, `FilterLabel`, and `ResolutionLabel`.
- Produces: `ResolutionOptions.AspectRatio(ResolutionSize)` and `ResolutionOptions.AspectRatios(IEnumerable<ResolutionSize>)`.
- Changes: `ResolutionOptions.DistinctSizes` orders by classified aspect ratio, width, then height.

- [ ] **Step 1: Write failing pure-logic checks**

Add these checks in `CheckResolutionSettings()` after refresh-rate checks:

```csharp
var square = ResolutionOptions.AspectRatio(new ResolutionSize(1000, 1000));
Check(square == new ResolutionAspectRatio(1, 1, "Square"), "square aspect classification");
var ultrawide = ResolutionOptions.AspectRatio(new ResolutionSize(3440, 1440));
Check(ultrawide == new ResolutionAspectRatio(21, 9, "Ultrawide"),
    "ultrawide aspect classification");
var unknown = ResolutionOptions.AspectRatio(new ResolutionSize(1000, 700));
Check(unknown == new ResolutionAspectRatio(10, 7, null), "unknown exact aspect classification");

var aspectSortedSizes = ResolutionOptions.DistinctSizes(
[
    new DisplayMode(1920, 1080, 60),
    new DisplayMode(1920, 1200, 60),
    new DisplayMode(1024, 768, 60),
    new DisplayMode(1280, 1024, 60),
    new DisplayMode(1000, 1000, 60)
]);
Check(aspectSortedSizes.SequenceEqual(
[
    new ResolutionSize(1000, 1000),
    new ResolutionSize(1280, 1024),
    new ResolutionSize(1024, 768),
    new ResolutionSize(1920, 1200),
    new ResolutionSize(1920, 1080)
]), "resolution aspect-first ordering");
var aspects = ResolutionOptions.AspectRatios(aspectSortedSizes);
Check(aspects.Select(value => value.FilterLabel).SequenceEqual(
[
    "1:1 (Square)",
    "5:4 (Standard)",
    "4:3 (Standard)",
    "16:10 (Wide)",
    "16:9 (Wide)"
]), "aspect filter ordering and labels");
```

- [ ] **Step 2: Run self-test and verify RED**

Run:

```powershell
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release -- --self-test
```

Expected: compiler failure naming missing `ResolutionAspectRatio`, `AspectRatio`, or `AspectRatios`. Failure must come from absent feature code.

- [ ] **Step 3: Add minimal ratio model and classification**

Add after `ResolutionSize` in `ResolutionSettings.cs`:

```csharp
internal readonly record struct ResolutionAspectRatio(
    uint Numerator,
    uint Denominator,
    string? Label)
{
    internal double Value => (double)Numerator / Denominator;
    internal string Key => $"{Numerator}:{Denominator}";
    internal string FilterLabel => Label is null ? Key : $"{Key} ({Label})";
    internal string ResolutionLabel => Label is null ? Key : $"{Key} {Label}";
}
```

Add known ratios inside `ResolutionOptions`:

```csharp
private static readonly ResolutionAspectRatio[] KnownAspectRatios =
[
    new(1, 1, "Square"),
    new(5, 4, "Standard"),
    new(4, 3, "Standard"),
    new(3, 2, "Classic"),
    new(16, 10, "Wide"),
    new(16, 9, "Wide"),
    new(21, 9, "Ultrawide"),
    new(32, 9, "Super ultrawide")
];
```

Replace `DistinctSizes` and add the pure helpers:

```csharp
internal static IReadOnlyList<ResolutionSize> DistinctSizes(IEnumerable<DisplayMode> modes) =>
    modes
        .Select(mode => new ResolutionSize(mode.Width, mode.Height))
        .Distinct()
        .OrderBy(size => AspectRatio(size).Value)
        .ThenBy(size => size.Width)
        .ThenBy(size => size.Height)
        .ToArray();

internal static ResolutionAspectRatio AspectRatio(ResolutionSize size)
{
    var divisor = GreatestCommonDivisor(size.Width, size.Height);
    var exact = new ResolutionAspectRatio(size.Width / divisor, size.Height / divisor, null);
    var nearest = KnownAspectRatios.MinBy(candidate =>
        Math.Abs(exact.Value - candidate.Value) / candidate.Value);
    return Math.Abs(exact.Value - nearest.Value) / nearest.Value <= 0.03
        ? nearest
        : exact;
}

internal static IReadOnlyList<ResolutionAspectRatio> AspectRatios(
    IEnumerable<ResolutionSize> sizes) =>
    sizes
        .Select(AspectRatio)
        .Distinct()
        .OrderBy(value => value.Value)
        .ThenBy(value => value.Numerator)
        .ThenBy(value => value.Denominator)
        .ToArray();

private static uint GreatestCommonDivisor(uint left, uint right)
{
    while (right != 0)
        (left, right) = (right, left % right);
    return left;
}
```

- [ ] **Step 4: Run self-test and verify GREEN**

Run the same `dotnet run ... --self-test` command.

Expected: `Self-test passed.` Existing checks remain green.

- [ ] **Step 5: Commit pure logic**

```powershell
git add -- src/SudoVDA.GUI/ResolutionSettings.cs src/SudoVDA.GUI/SelfTest.cs
git commit -m "feat: classify resolution aspect ratios"
```

---

### Task 2: Aspect filter UI and friendly primary wording

**Files:**
- Modify: `src/SudoVDA.GUI/SelfTest.cs`
- Modify: `src/SudoVDA.GUI/MainForm.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `ResolutionOptions.AspectRatio`, `ResolutionOptions.AspectRatios`, and aspect-first `DistinctSizes` from Task 1.
- Produces: named WinForms control `aspectCombo` and view-only filter behavior.
- Preserves: `PresetChoice.Key`, `UserSettings.CopyPrimary`, validation, registry schema, and driver behavior.

- [ ] **Step 1: Write failing form checks**

Expand `CheckResolutionForm()` modes to:

```csharp
[
    primary,
    new DisplayMode(1920, 1080, 60),
    new DisplayMode(1920, 1200, 60),
    new DisplayMode(1024, 768, 60),
    new DisplayMode(1280, 1024, 60),
    new DisplayMode(1000, 1000, 60)
]
```

Find the new control:

```csharp
var aspect = form.Controls.Find("aspectCombo", true).OfType<ComboBox>().Single();
```

Replace the copy-primary text assertion and add filter assertions:

```csharp
Check(aspect.SelectedItem?.ToString() == "All aspect ratios", "all-aspects default");
Check(preset.SelectedItem?.ToString() == "Match primary display", "match-primary default");
Check(aspect.Items.Cast<object>().Select(item => item.ToString()).SequenceEqual(
[
    "All aspect ratios",
    "1:1 (Square)",
    "5:4 (Standard)",
    "4:3 (Standard)",
    "16:10 (Wide)",
    "16:9 (Wide)",
    "21:9 (Ultrawide)"
]), "aspect dropdown contents");
Check(preset.Items.Cast<object>().Any(item => item.ToString() ==
    "1920 x 1080 (16:9 Wide)"), "all-aspects preset annotation");

aspect.SelectedItem = aspect.Items.Cast<object>()
    .Single(item => item.ToString() == "16:9 (Wide)");
Check(preset.Items.Cast<object>().Select(item => item.ToString()).SequenceEqual(
[
    "Match primary display",
    "1920 x 1080",
    "Custom"
]), "filtered preset contents");
Check(preset.SelectedItem?.ToString() == "1920 x 1080",
    "filter selects first matching preset");
aspect.SelectedIndex = 0;
Check(preset.SelectedItem?.ToString() == "1920 x 1080 (16:9 Wide)",
    "all-aspects preserves selected preset");
```

Update the later preset lookup to `1920 x 1080 (16:9 Wide)`. Extend active/stopped locking checks to include `aspect.Enabled`.

Update layout assertions for the two inserted rows:

```csharp
Check(resolutionLayout.GetPositionFromControl(width).Row == 5 &&
      resolutionLayout.GetPositionFromControl(height).Row == 5 &&
      resolutionLayout.GetPositionFromControl(refresh).Row == 5,
    "dimension controls share one row");
Check(widthLabel is not null && heightLabel is not null && refreshLabel is not null &&
      resolutionLayout.GetPositionFromControl(widthLabel).Row == 4 &&
      resolutionLayout.GetPositionFromControl(heightLabel).Row == 4 &&
      resolutionLayout.GetPositionFromControl(refreshLabel).Row == 4,
    "dimension labels share row above controls");
```

Keep the existing column assertions (`0`, `1`, `2`) in both checks.

- [ ] **Step 2: Run self-test and verify RED**

Run:

```powershell
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release -- --self-test
```

Expected: runtime failure because `aspectCombo` is absent. Failure must occur before production UI changes.

- [ ] **Step 3: Add aspect dropdown and cached sizes**

Add `_aspectCombo` beside `_presetCombo` and `_availableSizes` beside other form state:

```csharp
private readonly ComboBox _aspectCombo = new()
{
    Name = "aspectCombo",
    Dock = DockStyle.Fill,
    DropDownStyle = ComboBoxStyle.DropDownList
};

private IReadOnlyList<ResolutionSize> _availableSizes = [];
```

Increase `ClientSize` height to `330`. Change `resolutionLayout.RowCount` to `6`, then lay out controls as:

```csharp
var aspectLabel = CreateFieldLabel("aspectLabel", "Aspect ratio");
resolutionLayout.Controls.Add(aspectLabel, 0, 0);
resolutionLayout.SetColumnSpan(aspectLabel, 3);
resolutionLayout.Controls.Add(_aspectCombo, 0, 1);
resolutionLayout.SetColumnSpan(_aspectCombo, 3);
var presetLabel = CreateFieldLabel("presetLabel", "Resolution preset");
resolutionLayout.Controls.Add(presetLabel, 0, 2);
resolutionLayout.SetColumnSpan(presetLabel, 3);
resolutionLayout.Controls.Add(_presetCombo, 0, 3);
resolutionLayout.SetColumnSpan(_presetCombo, 3);
resolutionLayout.Controls.Add(CreateFieldLabel("widthLabel", "Width"), 0, 4);
resolutionLayout.Controls.Add(CreateFieldLabel("heightLabel", "Height"), 1, 4);
resolutionLayout.Controls.Add(CreateFieldLabel("refreshLabel", "Refresh rate"), 2, 4);
resolutionLayout.Controls.Add(_widthText, 0, 5);
resolutionLayout.Controls.Add(_heightText, 1, 5);
resolutionLayout.Controls.Add(_refreshCombo, 2, 5);
```

Subscribe after loading:

```csharp
_aspectCombo.SelectedIndexChanged += (_, _) => OnAspectChanged();
```

- [ ] **Step 4: Rebuild presets through selected ratio**

Replace preset population in `LoadResolutionControls` with cached sizes, aspect items, and an initial rebuild:

```csharp
_availableSizes = ResolutionOptions.DistinctSizes(modes);
_aspectCombo.Items.Add(new AspectFilterChoice("All aspect ratios", null));
foreach (var ratio in ResolutionOptions.AspectRatios(_availableSizes))
    _aspectCombo.Items.Add(new AspectFilterChoice(ratio.FilterLabel, ratio));
_aspectCombo.SelectedIndex = 0;
RebuildPresetChoices(settings.Preset, false);
```

Add these methods:

```csharp
private void OnAspectChanged()
{
    if (_suppressResolutionEvents)
        return;

    var preferredKey = (_presetCombo.SelectedItem as PresetChoice)?.Size is not null
        ? ((PresetChoice)_presetCombo.SelectedItem!).Key
        : null;
    RebuildPresetChoices(preferredKey, true);
    OnPresetChanged();
}

private void RebuildPresetChoices(string? preferredKey, bool selectFirstNormal)
{
    var suppressed = _suppressResolutionEvents;
    _suppressResolutionEvents = true;
    var ratio = (_aspectCombo.SelectedItem as AspectFilterChoice)?.Ratio;
    var sizes = ratio is null
        ? _availableSizes
        : _availableSizes.Where(size => ResolutionOptions.AspectRatio(size) == ratio).ToArray();

    _presetCombo.BeginUpdate();
    _presetCombo.Items.Clear();
    _presetCombo.Items.Add(new PresetChoice(
        "Match primary display", UserSettings.CopyPrimary, null));
    foreach (var size in sizes)
    {
        var aspect = ResolutionOptions.AspectRatio(size);
        var label = ratio is null
            ? $"{size} ({aspect.ResolutionLabel})"
            : size.ToString();
        _presetCombo.Items.Add(new PresetChoice(label, size.Key, size));
    }
    _presetCombo.Items.Add(new PresetChoice("Custom", UserSettings.Custom, null));

    var choice = _presetCombo.Items.Cast<PresetChoice>()
        .SingleOrDefault(item => item.Key == preferredKey);
    if (selectFirstNormal && choice?.Size is null)
        choice = _presetCombo.Items.Cast<PresetChoice>().FirstOrDefault(item => item.Size is not null);
    choice ??= _presetCombo.Items.Cast<PresetChoice>()
        .FirstOrDefault(item => item.Size is not null);
    choice ??= _presetCombo.Items.Cast<PresetChoice>()
        .Single(item => item.Key == UserSettings.CopyPrimary);
    _presetCombo.SelectedItem = choice;
    _presetCombo.EndUpdate();
    _suppressResolutionEvents = suppressed;
}
```

Add beside `PresetChoice`:

```csharp
private sealed record AspectFilterChoice(string Label, ResolutionAspectRatio? Ratio)
{
    public override string ToString() => Label;
}
```

Remove the old direct `Copy primary` and size-item additions from `LoadResolutionControls`. Keep refresh population and the existing initial mode/settings logic.

- [ ] **Step 5: Lock filter and update documentation**

Add `_aspectCombo.Enabled = resolutionEnabled;` in `SetUiState`.

Update README controls to:

```markdown
- `Aspect ratio`: filter discovered/common resolutions. `All aspect ratios` groups and annotates presets by ratio.
- `Resolution preset`: choose `Match primary display`, a discovered/common resolution, or `Custom`.
```

Replace the behavior paragraph with:

```markdown
Selecting a resolution preset populates Width and Height. `Match primary display` also populates Refresh from the current primary display. Editing either dimension selects `Custom`. Form choices persist per user in `HKCU\Software\VRPrivacy`; the internal `CopyPrimary` setting rereads the primary mode on the next launch.
```

- [ ] **Step 6: Run logic/form verification and Release build**

Run:

```powershell
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release -- --self-test
dotnet build src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release
```

Expected: `Self-test passed.` then build with `0 Warning(s)` and `0 Error(s)`. Do not run `--smoke-test`.

- [ ] **Step 7: Commit UI feature**

```powershell
git add -- src/SudoVDA.GUI/MainForm.cs src/SudoVDA.GUI/SelfTest.cs README.md
git commit -m "feat: filter resolutions by aspect ratio"
```
