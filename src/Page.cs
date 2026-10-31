using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NivalisSwitchboard;

/// <summary>What a mod's page says about one of its settings (any part may be missing: the plain control is used then).</summary>
public sealed record PageHint(SettingControl? Control, string? Title, string? Hint, Func<string, string?>? Validate,
    Func<string?>? Badge = null, Func<string?>? BadgeHelp = null);

/// <summary>One button of a buttons row.</summary>
public sealed class PageButton
{
    public string Label = "Do it";
    /// <summary>Its label worked out each refresh ("Apply 4 changes").</summary>
    public Func<string?>? LabelLive;
    public Func<string?>? Click;
    /// <summary>The main action: filled gold (last in its row).</summary>
    public bool Main;
    /// <summary>Rows come or go after it's pressed.</summary>
    public bool Rebuild;
    /// <summary>Shown only while this says so (null: always).</summary>
    public Func<bool>? Visible;
    /// <summary>0.4.2: pressable only while this says so (null: always); greyed out otherwise ("Apply" with nothing to apply).</summary>
    public Func<bool>? Enabled;
}

/// <summary>A row a mod adds that isn't one of its BepInEx settings (People of Nivalis's rings, its voice providers).</summary>
public sealed class PageRow
{
    /// <summary>Shown just before (or after) this setting of the group (its key); at the group's end with neither.</summary>
    public string? Before, After;
    public string Title = "", Hint = "", Help = "";
    public SettingControl Control = new(ControlKind.Note);
    /// <summary>The type <see cref="Set"/> takes: bool, double or string.</summary>
    public Type ValueType = typeof(string);
    public Func<object?>? Get;
    /// <summary>Saves a value; returns why not (shown to the player), or null.</summary>
    public Func<object, string?>? Set;
    /// <summary>A button row: what pressing it does (null, or why it couldn't).</summary>
    public Func<string?>? Click;
    /// <summary>A bar moving (the value), or done moving / not looked at any more (null): the mod can show it live.</summary>
    public Action<object?>? Preview;
    /// <summary>A dropdown whose choice does something: left/right don't step through it.</summary>
    public bool PickOnly;
    /// <summary>While this bar moves, the window fades so the game behind it shows (the rings at your feet).</summary>
    public bool SeeThrough;
    /// <summary>Rows come or go after a change here: the group's rows are asked for again.</summary>
    public bool Rebuild;
    /// <summary>A meter row: the used, pace and projected fractions of its bar (0-1; projected may go past 1).</summary>
    public Func<double[]?>? Meter;
    /// <summary>A pin on the row (Y with a controller): whether it's pinned, and pinning or unpinning it.</summary>
    public Func<bool>? Pinned;
    public Action<bool>? Pin;
    /// <summary>A short amber tag after its name ("changed"), and the sentence the footer adds for it.</summary>
    public Func<string?>? Badge, BadgeHelp;
    /// <summary>The footer's text for it, worked out when it's shown (instead of <see cref="Help"/>).</summary>
    public Func<string?>? HelpLive;
    /// <summary>0.4.0: acts at once, never waiting for Save (a what-if control, not a setting).</summary>
    public bool Immediate;
    /// <summary>0.4.0: who this row is, the same across a rebuild (a change waiting for Save stays on it). Rows sharing a title should have one.</summary>
    public string? Id;
    /// <summary>0.4.1: what changing this row fills in on others, waiting for Save with it: the value picked -> { a row's id, or
    /// a setting's "Section/Key" -> its new value } (null or empty: nothing).</summary>
    public Func<object, IDictionary?>? Fills;
    /// <summary>The line under its name, worked out each time the rows are refreshed (instead of <see cref="Hint"/>).</summary>
    public Func<string?>? HintLive;
    /// <summary>Shown only while this says so (null: always); the rows close up around a hidden one.</summary>
    public Func<bool>? Visible;
    /// <summary>A step: its number, its chip's words and tone ("ok", "warn" or none), its state ("done", "now" or none).</summary>
    public int Number;
    public Func<string?>? Chip, ChipTone, State;
    /// <summary>Tiles: each a dictionary of title, value, caption, meter (double[]) and warn (bool), read each refresh.</summary>
    public Func<IEnumerable?>? Tiles;
    /// <summary>A change: what it is now and what it would be, and how the second reads ("kept": amber; "same": muted).</summary>
    public Func<string?>? From, To, ToTone;
    /// <summary>
    /// 0.5.4 (People of Nivalis FB-59): a change whose "now" value can be stepped right in the list (left/right, or its arrows):
    /// the value now and a direction (-1, +1) -> the next value (null: as far as it goes), given to <see cref="Set"/>.
    /// </summary>
    public Func<string?, int, string?>? Step;
    /// <summary>A change list's captions (over now, recommended and the pins).</summary>
    public string FromLabel = "", ToLabel = "", EndLabel = "";
    /// <summary>A buttons row's buttons, left to right.</summary>
    public List<PageButton>? Buttons;
}

/// <summary>A mod's page as the menu reads it from <c>SettingsMenuPage.Describe()</c>.</summary>
public sealed class ModPage
{
    public string? Name;
    /// <summary>The mod writes its own settings file when a setting changes (the menu never calls ConfigFile.Save for it).</summary>
    public bool SavesItself;
    /// <summary>
    /// 0.5.5: list the mod's settings that tell other menus not to (ConfigurationManagerAttributes.Browsable = false) all the
    /// same. People of Nivalis hides its settings from other menus, which would save its file through BepInEx and lose part of it.
    /// </summary>
    public bool ListHidden;
    public List<(string name, string intro)> Groups = new();
    /// <summary>0.4.0: a group's category (shown under a folding heading in the tree), by group name.</summary>
    public Dictionary<string, string> Categories = new(StringComparer.Ordinal);
    /// <summary>0.4.0: changes wait for Save (the default); false: every change is saved at once, as before.</summary>
    public bool SaveButton = true;
    /// <summary>Hints by "Section/Key".</summary>
    public Dictionary<string, PageHint> Hints = new(StringComparer.Ordinal);
    public Func<string, IEnumerable?>? Rows;
    public Action? Opened, Closed;
    public Action<string>? Changed;
    /// <summary>What the menu couldn't read in the page (said once in the log).</summary>
    public List<string> Problems = new();
}

/// <summary>
/// Reads a mod's page: a dictionary of plain .NET types (strings, numbers, lists, dictionaries and System delegates), so the
/// mod needs nothing of the menu's to describe itself (see CONTRACT.md). Anything of the wrong type is left out and noted;
/// nothing here throws. No Unity, no BepInEx.
/// </summary>
public static class Page
{
    /// <summary>
    /// Whether a setting gets a row: not one the page hides (hint kind "hidden"), and not one tagged for no menus
    /// (Browsable = false) unless the page gives it a control or asks for those too (listHidden).
    /// </summary>
    public static bool Listed(ModPage page, bool hiddenTag, PageHint? hint) =>
        hint?.Control?.Kind != ControlKind.Hidden && (!hiddenTag || hint?.Control != null || page.ListHidden);

    public static ModPage Read(object? describe)
    {
        var page = new ModPage();
        if (describe is not IDictionary d) { page.Problems.Add("Describe() didn't return a dictionary"); return page; }
        page.Name = Str(d, "name");
        page.SavesItself = Get(d, "savesItself") is true;
        page.ListHidden = Get(d, "listHidden") is true;
        page.SaveButton = Get(d, "saveButton") is not false;
        if (Get(d, "groups") is IEnumerable groups)
            foreach (var g in groups)
                if (g is IDictionary gd && Str(gd, "name") is { Length: > 0 } name)
                {
                    page.Groups.Add((name, Str(gd, "intro") ?? ""));
                    if (Str(gd, "category") is { Length: > 0 } cat) page.Categories[name] = cat;
                }
        if (Get(d, "settings") is IDictionary settings)
            foreach (DictionaryEntry e in settings)
                if (e.Key is string key && e.Value is IDictionary hd) page.Hints[key] = Hint(hd, page.Problems, key);
        page.Rows = Get(d, "rows") as Func<string, IEnumerable?>;
        if (Get(d, "rows") is { } r && page.Rows == null) page.Problems.Add("rows isn't a Func<string, IEnumerable>");
        page.Opened = Get(d, "opened") as Action;
        page.Closed = Get(d, "closed") as Action;
        page.Changed = Get(d, "changed") as Action<string>;
        return page;
    }

    /// <summary>A group's custom rows, read the same careful way (a row that isn't a dictionary is left out).</summary>
    public static List<PageRow> Rows(ModPage page, string group, List<string> problems)
    {
        var list = new List<PageRow>();
        IEnumerable? rows = null;
        try { rows = page.Rows?.Invoke(group); }
        catch (Exception e) { problems.Add($"rows for {group}: {e.GetType().Name}"); }
        if (rows == null) return list;
        foreach (var o in rows)
        {
            if (o is not IDictionary rd) continue;
            var c = Control(rd, problems, "a row") ?? new SettingControl(ControlKind.Note);
            list.Add(new PageRow
            {
                Before = Str(rd, "before"), After = Str(rd, "after"),
                Title = Str(rd, "title") ?? "", Hint = Str(rd, "hint") ?? "", Help = Str(rd, "help") ?? "",
                Control = c,
                ValueType = (Str(rd, "type") ?? "") switch { "bool" => typeof(bool), "double" or "number" => typeof(double), _ => c.Kind == ControlKind.Switch ? typeof(bool) : c.Kind == ControlKind.Slider ? typeof(double) : typeof(string) },
                Get = Get(rd, "get") as Func<object?>,
                Set = Get(rd, "set") as Func<object, string?>,
                Click = Get(rd, "click") as Func<string?>,
                Preview = Get(rd, "preview") as Action<object?>,
                PickOnly = Get(rd, "pickOnly") is true,
                SeeThrough = Get(rd, "seeThrough") is true,
                Rebuild = Get(rd, "rebuild") is true,
                Meter = Get(rd, "meter") as Func<double[]?>,
                Pinned = Get(rd, "pinned") as Func<bool>,
                Pin = Get(rd, "pin") as Action<bool>,
                Badge = Get(rd, "badge") as Func<string?>,
                BadgeHelp = Get(rd, "badgeHelp") as Func<string?>,
                HelpLive = Get(rd, "helpLive") as Func<string?>,
                HintLive = Get(rd, "hintLive") as Func<string?>,
                Visible = Get(rd, "visible") as Func<bool>,
                Immediate = Get(rd, "immediate") is true,
                Id = Str(rd, "id"),
                Fills = Get(rd, "fills") as Func<object, IDictionary?>,
                Number = (int)(Num(rd, "number") ?? 0),
                Chip = Get(rd, "chip") as Func<string?>,
                ChipTone = Get(rd, "chipTone") as Func<string?>,
                State = Get(rd, "state") as Func<string?>,
                Tiles = Get(rd, "tiles") as Func<IEnumerable?>,
                From = Get(rd, "from") as Func<string?>,
                To = Get(rd, "to") as Func<string?>,
                ToTone = Get(rd, "toTone") as Func<string?>,
                Step = Get(rd, "step") as Func<string?, int, string?>,
                FromLabel = Str(rd, "fromLabel") ?? "", ToLabel = Str(rd, "toLabel") ?? "", EndLabel = Str(rd, "endLabel") ?? "",
                Buttons = ReadButtons(Get(rd, "buttons")),
            });
        }
        return list;
    }

    /// <summary>A buttons row's buttons (one that isn't a dictionary is left out).</summary>
    private static List<PageButton>? ReadButtons(object? o)
    {
        if (o is not IEnumerable list || o is string) return null;
        var buttons = new List<PageButton>();
        foreach (var b in list)
        {
            if (b is not IDictionary bd) continue;
            buttons.Add(new PageButton
            {
                Label = Str(bd, "label") ?? "Do it", LabelLive = Get(bd, "labelLive") as Func<string?>, Click = Get(bd, "click") as Func<string?>,
                Main = Get(bd, "main") is true, Rebuild = Get(bd, "rebuild") is true, Visible = Get(bd, "visible") as Func<bool>,
                Enabled = Get(bd, "enabled") as Func<bool>,
            });
        }
        return buttons;
    }

    private static PageHint Hint(IDictionary d, List<string> problems, string key) =>
        new(Control(d, problems, key), Str(d, "title"), Str(d, "hint"), Get(d, "validate") as Func<string, string?>,
            Get(d, "badge") as Func<string?>, Get(d, "badgeHelp") as Func<string?>);

    /// <summary>The control a hint or row describes ("kind" and its range, step, unit, choices, or a button's "label").</summary>
    public static SettingControl? Control(IDictionary d, List<string> problems, string what)
    {
        string? kind = Str(d, "kind");
        if (kind == null) return null;
        ControlKind? k = kind.ToLowerInvariant() switch
        {
            "switch" => ControlKind.Switch, "slider" => ControlKind.Slider, "choice" or "dropdown" => ControlKind.Choice,
            "text" => ControlKind.Text, "number" => ControlKind.Number, "secret" => ControlKind.Secret, "key" => ControlKind.Key,
            "hidden" => ControlKind.Hidden, "button" => ControlKind.Button, "note" => ControlKind.Note,
            "section" => ControlKind.Section, "meter" => ControlKind.Meter, "step" => ControlKind.Step, "subsection" => ControlKind.Subsection,
            "tiles" => ControlKind.Tiles, "change" => ControlKind.Change, "changes" => ControlKind.ChangeHeader, "buttons" => ControlKind.Buttons,
            _ => null,
        };
        if (k == null) { problems.Add($"{what}: kind \"{kind}\" isn't one the menu knows"); return null; }
        double min = Num(d, "min") ?? 0, max = Num(d, "max") ?? 0, step = Num(d, "step") ?? 1;
        if (k == ControlKind.Slider && !(max > min)) { problems.Add($"{what}: a slider needs min below max"); return new SettingControl(ControlKind.Number); }
        var choices = Get(d, "choices") is IEnumerable ch and not string ? ch.Cast<object?>().Select(x => Convert.ToString(x, CultureInfo.InvariantCulture) ?? "").ToList() : null;
        string unit = k == ControlKind.Button ? Str(d, "label") ?? "Do it" : Str(d, "unit") ?? "";
        return new SettingControl(k.Value, min, max, step > 0 ? step : 1, choices, unit);
    }

    private static object? Get(IDictionary d, string key) => d.Contains(key) ? d[key] : null;
    private static string? Str(IDictionary d, string key) => Get(d, key) as string;

    private static double? Num(IDictionary d, string key)
    {
        try
        {
            var v = Get(d, key);
            if (v == null || v is string) return null;
            double x = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            return double.IsFinite(x) ? x : null;
        }
        catch { return null; }
    }
}
