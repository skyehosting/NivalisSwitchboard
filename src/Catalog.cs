using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NivalisSwitchboard;

/// <summary>How a setting is shown in the menu.</summary>
public enum ControlKind
{
    /// <summary>A pill switch (on/off).</summary>
    Switch,
    /// <summary>A bar filled from the left, with a range and a step.</summary>
    Slider,
    /// <summary>One of a fixed list (a dropdown).</summary>
    Choice,
    /// <summary>Free text (typed, or with the on-screen keyboard).</summary>
    Text,
    /// <summary>A number typed in (a range too wide for a bar).</summary>
    Number,
    /// <summary>A key, password or header: shown only as "set / not set", typed in hidden. Never shown, never logged.</summary>
    Secret,
    /// <summary>A keyboard key name, caught by pressing the key.</summary>
    Key,
    /// <summary>Not shown.</summary>
    Hidden,
    /// <summary>A button (its label is the control's Unit).</summary>
    Button,
    /// <summary>A line of text only.</summary>
    Note,
    /// <summary>A heading that starts a part of the page (not a setting; the highlight passes over it).</summary>
    Section,
    /// <summary>A meter: how much of something is used, where it should be by now, and where it's going.</summary>
    Meter,
    /// <summary>A numbered step of a guided page: its number on a rail, a title, a line and a status chip.</summary>
    Step,
    /// <summary>A small heading inside a part of the page (a provider's limits), with a line above it.</summary>
    Subsection,
    /// <summary>A row of status tiles (each a name, a meter, a value and a line).</summary>
    Tiles,
    /// <summary>One change a plan makes: now -> recommended, with a pin.</summary>
    Change,
    /// <summary>The captions over a list of changes.</summary>
    ChangeHeader,
    /// <summary>A row of buttons, right-aligned to where the controls end, the main one last.</summary>
    Buttons,
}

/// <summary>One setting's control: its kind, range and step (bars), choices (dropdowns), and the unit shown.</summary>
public sealed record SettingControl(ControlKind Kind, double Min = 0, double Max = 0, double Step = 1, IReadOnlyList<string>? Choices = null, string Unit = "")
{
    /// <summary>A bar's value on its step and within its range.</summary>
    public double Snap(double value)
    {
        if (!double.IsFinite(value)) value = Min;
        double v = Math.Clamp(value, Min, Max);
        if (Step > 0) v = Min + Math.Round((v - Min) / Step, MidpointRounding.AwayFromZero) * Step;
        return Math.Round(Math.Clamp(v, Min, Max), 4);
    }

    /// <summary>A bar moved one step up (+1) or down (-1).</summary>
    public double Stepped(double value, int direction) => Snap(Snap(value) + Math.Sign(direction) * Step);

    /// <summary>The value as shown next to the bar: "15 m", "0.5", "35%".</summary>
    public string Format(double value)
    {
        string n = Snap(value).ToString(Step < 1 ? "0.##" : "0", CultureInfo.InvariantCulture);
        return Unit.Length == 0 ? n : Unit == "%" ? n + "%" : n + " " + Unit;
    }

    /// <summary>A dropdown's choices with the current value in them (a hand-edited value that isn't one of them stays choosable).</summary>
    public IReadOnlyList<string> ChoicesWith(string current)
    {
        var list = (Choices ?? Array.Empty<string>()).ToList();
        if (!list.Any(c => string.Equals(c, current, StringComparison.OrdinalIgnoreCase))) list.Insert(0, current);
        return list;
    }
}

/// <summary>
/// The plain control for a setting that its mod doesn't describe itself: from its type and what BepInEx knows of it (a range,
/// a list of values). No Unity, no BepInEx: the menu hands in what it read.
/// </summary>
public static class Catalog
{
    private static readonly string[] SecretWords = { "key", "password", "secret", "token", "apikey" };

    /// <summary>A setting whose name says it holds a secret (ApiKey, Password, SecretToken...): shown only as set / not set.</summary>
    public static bool LooksSecret(string key)
    {
        string k = (key ?? "").ToLowerInvariant();
        // "TalkKey", "HotKey", "KeyBind" name a keyboard key, not a secret.
        if (k.EndsWith("hotkey") || k.Contains("keybind") || k.EndsWith("talkkey") || k.EndsWith("menukey") || k.EndsWith("consolekey") || k.EndsWith("togglekey")) return false;
        return SecretWords.Any(w => k.Contains(w));
    }

    /// <summary>The control for a setting from its type, its range (min/max) and its list of values, whichever BepInEx has.</summary>
    public static SettingControl Generic(string key, Type type, double? min = null, double? max = null, IReadOnlyList<string>? values = null)
    {
        if (type == typeof(bool)) return new SettingControl(ControlKind.Switch);
        if (values is { Count: > 0 }) return new SettingControl(ControlKind.Choice, Choices: values);
        if (type.IsEnum) return new SettingControl(ControlKind.Choice, Choices: Enum.GetNames(type));
        bool whole = type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte);
        bool number = whole || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
        if (number && min is double lo && max is double hi && double.IsFinite(lo) && double.IsFinite(hi) && hi > lo)
        {
            // A range a bar can show step by step: whole numbers in ones (up to 200 of them), otherwise a hundredth of it, rounded.
            if (whole && hi - lo <= 200) return new SettingControl(ControlKind.Slider, lo, hi, 1);
            if (!whole) return new SettingControl(ControlKind.Slider, lo, hi, NiceStep((hi - lo) / 100));
            return new SettingControl(ControlKind.Number, lo, hi);
        }
        if (number) return new SettingControl(ControlKind.Number, -1_000_000_000, 1_000_000_000);
        if (type == typeof(string) && LooksSecret(key)) return new SettingControl(ControlKind.Secret);
        return new SettingControl(ControlKind.Text);
    }

    /// <summary>A step near this one that reads well: 1, 2 or 5 times a power of ten.</summary>
    public static double NiceStep(double raw)
    {
        if (!(raw > 0) || !double.IsFinite(raw)) return 1;
        double p = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double m = raw / p;
        return Math.Round((m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10) * p, 6);
    }

    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["api"] = "API", ["ai"] = "AI", ["ui"] = "UI", ["url"] = "URL", ["npc"] = "NPC", ["npcs"] = "NPCs", ["tts"] = "TTS", ["ipv6"] = "IPv6", ["ipv4"] = "IPv4", ["ip"] = "IP",
    };

    /// <summary>A setting's name as the menu shows it: "ChatterPerHour" is "Chatter per hour", "UiScale" is "UI scale".</summary>
    public static string Title(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        key = key.Replace("IPv6", "Ipv6").Replace("IPv4", "Ipv4"); // one word, not "I Pv 6"
        var words = new List<string>();
        int start = 0;
        for (int i = 1; i <= key.Length; i++)
        {
            bool cut = i == key.Length
                       || (char.IsUpper(key[i]) && (char.IsLower(key[i - 1]) || char.IsDigit(key[i - 1]) || (i + 1 < key.Length && char.IsLower(key[i + 1]) && char.IsUpper(key[i - 1]))));
            if (!cut) continue;
            words.Add(key[start..i]);
            start = i;
        }
        for (int i = 0; i < words.Count; i++)
        {
            string lower = words[i].ToLowerInvariant();
            words[i] = Words.TryGetValue(lower, out var w) ? w : i == 0 ? char.ToUpperInvariant(lower[0]) + lower[1..] : lower;
        }
        return string.Join(" ", words);
    }

    /// <summary>The line under a setting's name: the first sentence of its explanation, cut at a word if it's long.</summary>
    public static string Hint(string description, int max = 110)
    {
        string d = (description ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        int end = -1;
        for (int i = 0; i < d.Length - 1; i++)
            if (d[i] is '.' or '!' or '?' && d[i + 1] == ' ') { end = i + 1; break; }
        string first = end > 0 ? d[..end] : d;
        if (first.Length <= max) return first;
        int cut = first.LastIndexOf(' ', max - 1);
        return (cut > max / 2 ? first[..cut] : first[..(max - 1)]).TrimEnd(',', ';', ':', ' ') + "…";
    }

    /// <summary>What a secret shows: never the value, only whether one is set.</summary>
    public static string SecretLabel(string? value) => string.IsNullOrWhiteSpace(value) ? "not set" : "set";

    /// <summary>A typed number for its type and range: null when it isn't one. Decimals with a dot or a comma.</summary>
    public static double? ParseNumber(SettingControl c, string text, bool whole)
    {
        string t = (text ?? "").Trim().Replace(',', '.');
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v)) return null;
        if (whole) v = Math.Round(v);
        if (c.Kind == ControlKind.Slider) return c.Snap(v);
        return c.Max > c.Min ? Math.Clamp(v, c.Min, c.Max) : v;
    }

    /// <summary>A value from the menu as the setting's own type (a bar's number as an int or a float, a name as its enum): null when it can't be.</summary>
    public static object? AsSetting(Type type, object? value)
    {
        try
        {
            if (value == null) return null;
            if (type == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (type == typeof(bool)) return value is bool b ? b : bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var p) ? p : null;
            if (type.IsEnum)
            {
                string name = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                return Enum.GetNames(type).FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) is { } found ? Enum.Parse(type, found) : null;
            }
            double d = value is string s ? double.Parse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture) : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (!double.IsFinite(d)) return null;
            if (type == typeof(int)) return (int)Math.Clamp(Math.Round(d), int.MinValue, int.MaxValue);
            if (type == typeof(long)) return (long)Math.Clamp(Math.Round(d), long.MinValue, long.MaxValue);
            if (type == typeof(float)) return (float)d;
            if (type == typeof(double)) return d;
        }
        catch { }
        return null;
    }

    /// <summary>A value as the menu shows it: numbers the same in every language.</summary>
    public static string Show(object? v) => v switch
    {
        null => "",
        bool b => b ? "On" : "Off",
        float f => f.ToString("0.###", CultureInfo.InvariantCulture),
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };
}
