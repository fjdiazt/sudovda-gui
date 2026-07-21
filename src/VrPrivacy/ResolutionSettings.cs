using Microsoft.Win32;
using System.Globalization;

namespace VrPrivacy;

internal readonly record struct ResolutionSize(uint Width, uint Height)
{
    internal string Key => $"{Width}x{Height}";

    public override string ToString() => $"{Width} x {Height}";
}

internal sealed record UserSettings(
    string Preset,
    uint Width,
    uint Height,
    uint RefreshHz,
    bool MakePrimary,
    bool RouteNewWindows)
{
    internal const string CopyPrimary = "CopyPrimary";
    internal const string Custom = "Custom";

    internal static UserSettings Defaults(DisplayMode primary)
    {
        var refresh = primary.RefreshHz is >= 1 and <= 500 ? primary.RefreshHz : 60;
        return new(CopyPrimary, primary.Width, primary.Height, refresh, true, true);
    }
}

internal static class ResolutionOptions
{
    private static readonly uint[] StandardRates =
    [
        24, 25, 30, 48, 50, 60, 72, 75, 90, 100, 120, 144, 165, 240, 360, 480, 500
    ];

    internal static IReadOnlyList<ResolutionSize> DistinctSizes(IEnumerable<DisplayMode> modes) =>
        modes
            .Select(mode => new ResolutionSize(mode.Width, mode.Height))
            .Distinct()
            .OrderBy(size => size.Width)
            .ThenBy(size => size.Height)
            .ToArray();

    internal static IReadOnlyList<uint> RefreshRates(uint primaryRefresh) =>
        StandardRates
            .Append(primaryRefresh)
            .Where(rate => rate is >= 1 and <= 500)
            .Distinct()
            .Order()
            .ToArray();

    internal static bool TryParseMode(
        string widthText,
        string heightText,
        uint refreshHz,
        out DisplayMode mode,
        out string widthError,
        out string heightError)
    {
        var widthValid = TryParseDimension(
            widthText, 640, 7680, "Width", out var width, out widthError);
        var heightValid = TryParseDimension(
            heightText, 480, 4320, "Height", out var height, out heightError);
        mode = new DisplayMode(width, height, refreshHz);
        return widthValid && heightValid && DisplayController.IsSupported(mode);
    }

    internal static bool TryParsePreset(string value, out ResolutionSize size)
    {
        var parts = value.Split('x');
        if (parts.Length == 2 &&
            uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width) &&
            uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height))
        {
            size = new ResolutionSize(width, height);
            return true;
        }

        size = default;
        return false;
    }

    private static bool TryParseDimension(
        string text,
        uint minimum,
        uint maximum,
        string name,
        out uint value,
        out string error)
    {
        var valid = uint.TryParse(
                        text.Trim(),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out value) &&
                    value >= minimum &&
                    value <= maximum;
        error = valid ? string.Empty : $"{name} must be {minimum}–{maximum}.";
        return valid;
    }
}

internal static class UserSettingsStore
{
    internal const string DefaultPath = @"Software\VRPrivacy";

    internal static UserSettings Load(DisplayMode primary, string path = DefaultPath)
    {
        var defaults = UserSettings.Defaults(primary);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            if (key is null)
                return defaults;

            var makePrimary = ReadBoolean(key, "MakePrimary", true);
            var routeNewWindows = ReadBoolean(key, "RouteNewWindows", true);
            var preset = key.GetValue("Preset") as string ?? UserSettings.CopyPrimary;
            var width = ReadUInt(key, "Width", defaults.Width);
            var height = ReadUInt(key, "Height", defaults.Height);
            var refresh = ReadUInt(key, "RefreshHz", defaults.RefreshHz);

            var presetValid = preset is UserSettings.CopyPrimary or UserSettings.Custom ||
                              ResolutionOptions.TryParsePreset(preset, out _);
            var modeValid = ResolutionOptions.TryParseMode(
                width.ToString(CultureInfo.InvariantCulture),
                height.ToString(CultureInfo.InvariantCulture),
                refresh,
                out _,
                out _,
                out _);
            if (!presetValid ||
                !modeValid ||
                !ResolutionOptions.RefreshRates(primary.RefreshHz).Contains(refresh))
            {
                return defaults with
                {
                    MakePrimary = makePrimary,
                    RouteNewWindows = routeNewWindows
                };
            }

            return new(preset, width, height, refresh, makePrimary, routeNewWindows);
        }
        catch
        {
            return defaults;
        }
    }

    internal static void Save(UserSettings settings, string path = DefaultPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        key.SetValue("Preset", settings.Preset, RegistryValueKind.String);
        key.SetValue("Width", checked((int)settings.Width), RegistryValueKind.DWord);
        key.SetValue("Height", checked((int)settings.Height), RegistryValueKind.DWord);
        key.SetValue("RefreshHz", checked((int)settings.RefreshHz), RegistryValueKind.DWord);
        key.SetValue("MakePrimary", settings.MakePrimary ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("RouteNewWindows", settings.RouteNewWindows ? 1 : 0, RegistryValueKind.DWord);
    }

    private static uint ReadUInt(RegistryKey key, string name, uint fallback) =>
        key.GetValue(name) is int value && value >= 0 ? (uint)value : fallback;

    private static bool ReadBoolean(RegistryKey key, string name, bool fallback) =>
        key.GetValue(name) is int value ? value != 0 : fallback;
}
