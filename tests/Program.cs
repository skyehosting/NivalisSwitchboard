using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NivalisSwitchboard;

[assembly: System.Reflection.AssemblyMetadata("NivalisSettingsMenu", "1")]

namespace NivalisSwitchboard.Tests;

internal static class Program
{
    private static int _pass, _fail;

    private static void Check(bool ok, string what)
    {
        if (ok) _pass++;
        else { _fail++; Console.WriteLine("  FAIL: " + what); }
    }

    private enum Mode { Easy, Hard }

    private static int Main()
    {
        Console.WriteLine("-- which mods are listed");
        Check(Marker.Has(typeof(Program).Assembly), "a DLL with the marker is listed");
        Check(!Marker.Has(typeof(object).Assembly) && !Marker.Has(typeof(Enumerable).Assembly), "...one without it (any other mod) isn't");

        Console.WriteLine("-- the plain controls, from what BepInEx knows");
        Check(Catalog.Generic("ShowNameplates", typeof(bool)).Kind == ControlKind.Switch, "on/off is a switch");
        var players = Catalog.Generic("MaxPlayers", typeof(int), 2, 8);
        Check(players.Kind == ControlKind.Slider && players.Min == 2 && players.Max == 8 && players.Step == 1, "a whole-number range is a bar in ones (NivalisMP's MaxPlayers, 2-8)");
        Check(Catalog.Generic("Port", typeof(int), 1024, 65535).Kind == ControlKind.Number, "...too wide a range for a bar is a number field (its Port)");
        var vol = Catalog.Generic("Volume", typeof(float), 0, 1);
        Check(vol.Kind == ControlKind.Slider && vol.Step == 0.01, "a decimal range is a bar in hundredths of it");
        Check(Catalog.Generic("Speed", typeof(string), values: new[] { "Slow", "Fast" }).Choices!.SequenceEqual(new[] { "Slow", "Fast" })
              && Catalog.Generic("Mode", typeof(Mode)).Choices!.SequenceEqual(new[] { "Easy", "Hard" }), "a list of values or an enum is a dropdown");
        Check(Catalog.Generic("Password", typeof(string)).Kind == ControlKind.Secret && Catalog.Generic("ApiKey", typeof(string)).Kind == ControlKind.Secret
              && Catalog.Generic("SecretToken", typeof(string)).Kind == ControlKind.Secret, "a password, key or token is a secret (never shown)");
        Check(Catalog.Generic("TalkKey", typeof(string)).Kind == ControlKind.Text && Catalog.Generic("LastAddress", typeof(string)).Kind == ControlKind.Text,
              "...a keyboard key's name or an address isn't");
        Check(Catalog.Generic("Count", typeof(int)).Kind == ControlKind.Number && Catalog.Generic("Name", typeof(string)).Kind == ControlKind.Text,
              "anything else is a number or a text field");
        Check(Catalog.NiceStep(0.37) == 0.5 && Catalog.NiceStep(0.013) == 0.01 && Catalog.NiceStep(2.2) == 2 && Catalog.NiceStep(double.NaN) == 1,
              "a bar's step reads well (1, 2 or 5 of a power of ten)");
        Check(Catalog.Title("ListenIPv6") == "Listen IPv6" && Catalog.Title("MaxPlayers") == "Max players" && Catalog.Title("UiScale") == "UI scale"
              && Catalog.Title("ApiUrl") == "API URL", "names read as words");
        Check(Catalog.AsSetting(typeof(int), 4.6) is 5 && Catalog.AsSetting(typeof(bool), "true") is true && Catalog.AsSetting(typeof(Mode), "hard") is Mode.Hard
              && Catalog.AsSetting(typeof(int), double.NaN) == null, "a value becomes the setting's own type, or nothing");

        Console.WriteLine("-- a mod's page");
        string? got = null;
        object saved = 0;
        var page = Page.Read(new Dictionary<string, object?>
        {
            ["name"] = "People of Nivalis",
            ["savesItself"] = true,
            ["groups"] = new List<object> { new Dictionary<string, object?> { ["name"] = "Voices", ["intro"] = "People speak." } },
            ["settings"] = new Dictionary<string, object?>
            {
                ["Voices/Volume"] = new Dictionary<string, object?> { ["kind"] = "slider", ["min"] = 0, ["max"] = 100, ["step"] = 5, ["unit"] = "%" },
                ["Voices/ApiKey"] = new Dictionary<string, object?> { ["kind"] = "secret" },
                ["Controls/TalkKey"] = new Dictionary<string, object?> { ["kind"] = "key", ["validate"] = (Func<string, string?>)(k => k == "Escape" ? "no" : null) },
                ["Bad/One"] = new Dictionary<string, object?> { ["kind"] = "wheel" },
                ["Bad/Two"] = new Dictionary<string, object?> { ["kind"] = "slider", ["min"] = 5, ["max"] = 5 },
            },
            ["rows"] = (Func<string, IEnumerable>)(g => g == "Voices"
                ? new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["after"] = "Enabled", ["title"] = "OpenAI", ["kind"] = "switch",
                        ["get"] = (Func<object?>)(() => true), ["set"] = (Func<object, string?>)(v => { saved = v; return null; }),
                    },
                    new Dictionary<string, object?> { ["title"] = "Remove", ["kind"] = "button", ["label"] = "Remove", ["click"] = (Func<string?>)(() => "gone"), ["rebuild"] = true },
                    "not a row",
                }
                : throw new InvalidOperationException("no rows here")),
            ["changed"] = (Action<string>)(k => got = k),
        });
        Check(page.Name == "People of Nivalis" && page.SavesItself && page.Groups.Single() == ("Voices", "People speak."), "its name, groups and that it saves itself");
        var volume = page.Hints["Voices/Volume"].Control!;
        Check(volume.Kind == ControlKind.Slider && volume.Max == 100 && volume.Step == 5 && volume.Unit == "%" && page.Hints["Voices/ApiKey"].Control!.Kind == ControlKind.Secret,
              "a setting's hint: a bar with its range, step and unit; a secret");
        Check(page.Hints["Controls/TalkKey"].Validate!("Escape") == "no" && page.Hints["Controls/TalkKey"].Validate!("T") == null, "a key's own rule comes with it");
        Check(page.Hints["Bad/One"].Control == null && page.Hints["Bad/Two"].Control!.Kind == ControlKind.Number && page.Problems.Count == 2,
              "a kind it doesn't know, or a bar with no range, falls back and is noted");
        page.Changed!("Volume");
        Check(got == "Volume", "the mod hears which of its settings changed");
        var problems = new List<string>();
        var rows = Page.Rows(page, "Voices", problems);
        Check(rows.Count == 2 && rows[0].After == "Enabled" && rows[0].ValueType == typeof(bool) && rows[0].Get!() is true && rows[0].Set!(false) == null && saved is false,
              "its own rows: where they go, their type, reading and saving through the mod");
        Check(rows[1].Control.Kind == ControlKind.Button && rows[1].Control.Unit == "Remove" && rows[1].Rebuild && rows[1].Click!() == "gone", "a button row with its label");
        Check(Page.Rows(page, "Other", problems).Count == 0 && problems.Count == 1, "a page that throws for a group: no rows, noted, nothing breaks");
        bool pinnedNow = false;
        var rich = Page.Read(new Dictionary<string, object?>
        {
            ["settings"] = new Dictionary<string, object?> { ["A/B"] = new Dictionary<string, object?> { ["kind"] = "switch", ["badge"] = (Func<string?>)(() => "changed") } },
            ["rows"] = (Func<string, IEnumerable>)(g => new List<object>
            {
                new Dictionary<string, object?> { ["kind"] = "section", ["title"] = "Where you are" },
                new Dictionary<string, object?>
                {
                    ["kind"] = "meter", ["title"] = "AI", ["get"] = (Func<object?>)(() => "1.2M of 5M"),
                    ["meter"] = (Func<double[]?>)(() => new[] { 0.24, 0.45, 0.87 }), ["helpLive"] = (Func<string?>)(() => "why"), ["hintLive"] = (Func<string?>)(() => "now 3"),
                },
                new Dictionary<string, object?>
                {
                    ["kind"] = "slider", ["min"] = 0, ["max"] = 10, ["title"] = "Lines", ["pinned"] = (Func<bool>)(() => pinnedNow),
                    ["pin"] = (Action<bool>)(v => pinnedNow = v), ["badge"] = (Func<string?>)(() => "changed"), ["badgeHelp"] = (Func<string?>)(() => "kept as yours"),
                },
            }),
        });
        var rr = Page.Rows(rich, "Usage", new List<string>());
        rr[2].Pin!(true);
        Check(rr[0].Control.Kind == ControlKind.Section && rr[1].Control.Kind == ControlKind.Meter && rr[1].Meter!()![2] == 0.87 && rr[1].HelpLive!() == "why" && rr[1].HintLive!() == "now 3"
              && rr[2].Pinned!() && rr[2].Badge!() == "changed" && rr[2].BadgeHelp!() == "kept as yours" && rich.Hints["A/B"].Badge!() == "changed",
              "headings, meters, pins and amber tags are read (on rows, and on a setting's hint)");
        bool shownNow = true, pressed = false;
        var guided = Page.Read(new Dictionary<string, object?>
        {
            ["rows"] = (Func<string, IEnumerable>)(g => new List<object>
            {
                new Dictionary<string, object?> { ["kind"] = "tiles", ["tiles"] = (Func<IEnumerable?>)(() => new List<object> { new Dictionary<string, object?> { ["title"] = "AI" } }) },
                new Dictionary<string, object?> { ["kind"] = "step", ["title"] = "Your limits", ["number"] = 1, ["chip"] = (Func<string?>)(() => "set"),
                                                  ["chipTone"] = (Func<string?>)(() => "ok"), ["state"] = (Func<string?>)(() => "done") },
                new Dictionary<string, object?> { ["kind"] = "subsection", ["title"] = "AI" },
                new Dictionary<string, object?> { ["kind"] = "changes", ["title"] = "Setting", ["fromLabel"] = "Now", ["toLabel"] = "Recommended", ["endLabel"] = "Pin" },
                new Dictionary<string, object?> { ["kind"] = "change", ["title"] = "Lines", ["from"] = (Func<string?>)(() => "4"), ["to"] = (Func<string?>)(() => "3"),
                                                  ["toTone"] = (Func<string?>)(() => ""), ["visible"] = (Func<bool>)(() => shownNow) },
                new Dictionary<string, object?>
                {
                    ["kind"] = "buttons", ["buttons"] = new List<object>
                    {
                        new Dictionary<string, object?> { ["label"] = "Undo", ["visible"] = (Func<bool>)(() => false) },
                        "not a button",
                        new Dictionary<string, object?> { ["label"] = "Apply", ["main"] = true, ["rebuild"] = true, ["labelLive"] = (Func<string?>)(() => "Apply 2 changes"),
                                                          ["click"] = (Func<string?>)(() => { pressed = true; return null; }) },
                    },
                },
            }),
        });
        var gr = Page.Rows(guided, "Usage", new List<string>());
        var btns = gr[5].Buttons!;
        btns[1].Click!();
        Check(gr[0].Control.Kind == ControlKind.Tiles && gr[0].Tiles!().Cast<object>().Count() == 1
              && gr[1].Control.Kind == ControlKind.Step && gr[1].Number == 1 && gr[1].Chip!() == "set" && gr[1].ChipTone!() == "ok" && gr[1].State!() == "done"
              && gr[2].Control.Kind == ControlKind.Subsection && gr[3].Control.Kind == ControlKind.ChangeHeader && gr[3].FromLabel == "Now" && gr[3].EndLabel == "Pin"
              && gr[4].Control.Kind == ControlKind.Change && gr[4].From!() == "4" && gr[4].To!() == "3" && gr[4].Visible!()
              && gr[5].Control.Kind == ControlKind.Buttons && btns.Count == 2 && !btns[0].Visible!() && btns[1].Main && btns[1].Rebuild
              && btns[1].LabelLive!() == "Apply 2 changes" && pressed,
              "guided pages: tiles, steps (number, chip, state), sub-headings, a change list, buttons rows (a non-button left out), rows that come and go");
        var savePage = Page.Read(new Dictionary<string, object?>
        {
            ["groups"] = new List<object>
            {
                new Dictionary<string, object?> { ["name"] = "Setup Wizard" },
                new Dictionary<string, object?> { ["name"] = "Voices", ["category"] = "Advanced" },
            },
            ["rows"] = (Func<string, IEnumerable>)(g => new List<object> { new Dictionary<string, object?> { ["kind"] = "slider", ["min"] = 0, ["max"] = 1, ["immediate"] = true } }),
        });
        var plain = Page.Read(new Dictionary<string, object?> { ["saveButton"] = false });
        Check(savePage.SaveButton && !plain.SaveButton && savePage.Categories.TryGetValue("Voices", out var cat) && cat == "Advanced" && !savePage.Categories.ContainsKey("Setup Wizard")
              && Page.Rows(savePage, "Voices", new List<string>())[0].Immediate,
              "0.4.0: pages save on Save unless they say not to; a group's category; a what-if row acts at once");
        var fillPage = Page.Read(new Dictionary<string, object?>
        {
            ["groups"] = new List<object> { new Dictionary<string, object?> { ["name"] = "Setup Wizard" } },
            ["rows"] = (Func<string, IEnumerable>)(g => new List<object> { new Dictionary<string, object?> { ["kind"] = "choice", ["choices"] = new List<object> { "OpenAI", "Other" }, ["id"] = "provider",
                ["fills"] = (Func<object, IDictionary?>)(v => v as string == "OpenAI" ? new Dictionary<string, object?> { ["AI Connection/ApiUrl"] = "https://api.openai.com/v1/chat/completions" } : null) } }),
        });
        var fillRow = Page.Rows(fillPage, "Setup Wizard", new List<string>())[0];
        Check(fillRow.Id == "provider" && fillRow.Fills?.Invoke("OpenAI")?["AI Connection/ApiUrl"] as string == "https://api.openai.com/v1/chat/completions" && fillRow.Fills("Other") == null,
              "0.4.1: a row's id and what it fills in are read");
        Check(StageKeys.Shared(new[] { W("Chance", "voice:Local chance"), W("Chance", "voice:Local chance"), W("Chance", "voice:Windows chance") }).SetEquals(new[] { "i:voice:Local chance" }),
              "0.4.1: two rows with one id: that key is shared (a waiting change is put on neither)");
        var enabledPage = Page.Read(new Dictionary<string, object?>
        {
            ["groups"] = new List<object> { new Dictionary<string, object?> { ["name"] = "Usage" } },
            ["rows"] = (Func<string, IEnumerable>)(g => new List<object> { new Dictionary<string, object?> { ["kind"] = "buttons",
                ["buttons"] = new List<object> { new Dictionary<string, object?> { ["label"] = "Apply", ["enabled"] = (Func<bool>)(() => false) } } } }),
        });
        Check(Page.Rows(enabledPage, "Usage", new List<string>())[0].Buttons is [{ Enabled: { } en }] && !en(), "0.4.2: a button's enabled is read (greyed out while false)");
        Check(!Page.Read(new Dictionary<string, object?> { ["savesItself"] = false }).SavesItself && !Page.Read(new Dictionary<string, object?> { ["savesItself"] = "yes" }).SavesItself,
              "only a page that says true saves itself (otherwise the menu saves the file for the mod)");
        Check(Page.Read(42).Problems.Count == 1 && Page.Read(null).Hints.Count == 0, "a page that isn't one: noted, the plain controls are used");

        Console.WriteLine("-- 0.5.5: settings hidden from other menus (Browsable = false), listed when the page asks");
        var asks = Page.Read(new Dictionary<string, object?> { ["listHidden"] = true });
        var doesnt = Page.Read(new Dictionary<string, object?>());
        var hiddenHint = new PageHint(new SettingControl(ControlKind.Hidden), null, null, null);
        var switchHint = new PageHint(new SettingControl(ControlKind.Switch), null, null, null);
        Check(asks.ListHidden && !doesnt.ListHidden && !Page.Read(new Dictionary<string, object?> { ["listHidden"] = "yes" }).ListHidden,
              "listHidden read only when true");
        Check(Page.Listed(asks, true, null) && !Page.Listed(doesnt, true, null), "a hidden-tagged setting: listed only when the page asks");
        Check(Page.Listed(doesnt, true, switchHint), "a hidden-tagged setting the page gives a control: listed as before");
        Check(!Page.Listed(asks, true, hiddenHint) && !Page.Listed(asks, false, hiddenHint), "the page's own hidden still hides, listHidden or not");
        Check(Page.Listed(doesnt, false, null) && Page.Listed(asks, false, null), "an untagged setting: listed either way");

        Console.WriteLine("-- the on-screen keyboard and the controller (as in People of Nivalis)");
        Check(KeyboardLayout.Move(false, (2, 4), 0, +1) == (3, 1) && KeyboardLayout.Type("", KeyboardLayout.At(false, 1, 0), true) == "A", "moves and types as there");
        var rep = new PadRepeat();
        Check(rep.Step(1, 0f) == 1 && rep.Step(1, 0.1f) == 0 && rep.Step(1, 0.39f) == 1 && PadRepeat.Label("A", true) == "X", "a held direction repeats; pad names");

        Console.WriteLine("-- a change waiting for Save stays on its own row through a rebuild");
        static StageKeys.Who W(string title, string? id = null, string? setting = null) => new(setting, id, title);
        // Three voice providers whose rows share titles; P1 (the first) is removed while P2's Chance waits.
        var before = new[] { W("Chance"), W("Chance"), W("Chance") };
        var after = new[] { W("Chance"), W("Chance") };
        string p2 = StageKeys.For(before, 1);
        Check(StageKeys.For(after, 0) != p2 && StageKeys.For(after, 1) != p2, "rows sharing a title, one gone: the waiting change matches no row (dropped, never put on P3's)");
        Check(StageKeys.For(before, 1) == StageKeys.For(new[] { W("Chance"), W("Chance"), W("Chance") }, 1), "...the same rows again: it stays on its row");
        var withIds = new[] { W("Chance", "voice.p1.chance"), W("Chance", "voice.p2.chance"), W("Chance", "voice.p3.chance") };
        var idsAfter = new[] { W("Chance", "voice.p2.chance"), W("Chance", "voice.p3.chance") };
        Check(StageKeys.For(withIds, 1) == StageKeys.For(idsAfter, 0) && StageKeys.For(withIds, 1) != StageKeys.For(idsAfter, 1), "with ids: it follows P2 to its new place");
        Check(StageKeys.For(new[] { W("Volume", setting: "Voices/Volume") }, 0) == "s:Voices/Volume" && StageKeys.For(new[] { W("Note"), W("Volume", setting: "Voices/Volume"), W("Model") }, 1) == "s:Voices/Volume", "a setting row is its setting, wherever it is");
        Check(StageKeys.For(new[] { W("Model"), W("Address") }, 1) == StageKeys.For(new[] { W("Address") }, 0), "a title used once: another row coming or going doesn't move it");

        Console.WriteLine("-- FB-55: the pause menu centred, and scrolled when it runs off");
        const double H = 1440; // the author's screen: nine buttons from y 920 down to 70 (screen y up), 107 px apart
        var fits = PauseLayout.Plan(920, 70, H, 0);
        Check(!fits.Scrolls && Math.Abs((920 + 70) / 2.0 + fits.Shift - H / 2) < 0.01, "nine buttons that fit: their middle on the screen's middle");
        Check(Math.Abs(920 + fits.Shift - (H / 2 + 425)) < 0.01, "...the whole list moved as one (the top button 425 px over the middle)");
        var tall = PauseLayout.Plan(1100, 300, H, 0); // 800 px: fits the band but not centred under the heading
        Check(!tall.Scrolls && 1100 + tall.Shift <= H * PauseLayout.TopShare + 0.01, "...a taller list that fits stays under the heading (moved down from the middle)");
        var over = PauseLayout.Plan(1500, 0, H, 0);
        double bandTop = H * PauseLayout.TopShare, bandBottom = H * PauseLayout.BottomShare;
        Check(over.Scrolls && Math.Abs(1500 + over.Shift - bandTop) < 0.01 && Math.Abs(over.MaxScroll - (1500 - (bandTop - bandBottom))) < 0.01,
              "more than fit: it scrolls, the first button at the top of the band, as far as the last one at the bottom");
        var end = PauseLayout.Plan(1500, 0, H, 1e9);
        Check(end.Scroll == end.MaxScroll && Math.Abs(0 + end.Shift - bandBottom) < 0.01 && PauseLayout.Plan(1500, 0, H, -50).Scroll == 0,
              "...the scroll stops at both ends");
        Check(PauseLayout.Alpha(bandBottom + 2, H, over) < 0.1 && PauseLayout.Alpha(bandTop - 2, H, over) == 1 && PauseLayout.Alpha(H / 2, H, over) == 1,
              "at the top: only the bottom edge fades (more below), the middle is fully shown");
        var mid = PauseLayout.Plan(1500, 0, H, over.MaxScroll / 2);
        Check(PauseLayout.Alpha(bandTop - 2, H, mid) < 0.1 && PauseLayout.Alpha(bandBottom + 2, H, mid) < 0.1, "...scrolled halfway: both edges fade");
        Check(PauseLayout.Alpha(bandBottom + 2, H, end) == 1 && PauseLayout.Alpha(bandTop - 2, H, end) < 0.1, "...at the end: only the top fades");
        Check(PauseLayout.Alpha(10, H, fits) == 1, "nothing fades when nothing scrolls");
        Check(PauseLayout.Wheel(0, -120, H, over) > 0 && PauseLayout.Wheel(over.MaxScroll, 120, H, over) < over.MaxScroll && PauseLayout.Wheel(0, 120, H, over) == 0,
              "the wheel down shows the buttons below, up those above, and stops at the ends");
        Check(PauseLayout.Wheel(0, -1, H, over) > 0 && PauseLayout.Wheel(0, -120, H, fits) == 0, "...a small wheel step still moves it; with nothing to scroll the wheel does nothing");
        double lastTop = 100 + over.Shift, lastBottom = 0 + over.Shift; // the last button, while at the top
        double show = PauseLayout.ScrollToShow(lastTop, lastBottom, H, over);
        var shown = PauseLayout.Plan(1500, 0, H, show);
        Check(show > 0 && 0 + shown.Shift >= bandBottom + H * PauseLayout.FadeShare - 0.01 || show == over.MaxScroll,
              "a controller's selection below the band is scrolled into view, clear of the fade");
        Check(PauseLayout.ScrollToShow(H / 2 + 50, H / 2 - 50, H, mid) == mid.Scroll, "...one already in view doesn't move it");

        Console.WriteLine($"{_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }
}
