using System;
using System.Collections.Generic;
using HarmonyLib;
using Nivalis;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NivalisSwitchboard;

/// <summary>
/// The menu's own button on the game's title menu and pause menu, right under the game's Settings (a copy of it, so it looks
/// and sounds like the rest, the way NivalisMP adds its options). Its label is MenuButtonLabel ("Switchboard"), kept
/// apart from the Unofficial Patch's own "Mod settings". While the menu is up, Esc and the pause key close it, not the
/// game's menu under it.
/// </summary>
internal static class MenuButton
{
    public const string ObjectName = "NivalisSwitchboard.Button";
    public static Func<string> Label = () => "Switchboard";
    public static Func<bool> Enabled = () => true;
    /// <summary>The button the menu was opened from (given the selection back on close).</summary>
    public static GameObject? OpenedFrom;
    // 0.4.2: the buttons added (title and pause menus), with the Settings button each went under: a change of MenuButton or
    // MenuButtonLabel shows at once (it showed only once the game was restarted).
    private static readonly List<(GameObject go, Button settings)> Added = new();

    public static void Inject(MainMenuUI menu)
    {
        if (menu == null) return;
        PauseMenuLayout.Attach(menu); // FB-55: the pause menu's buttons centred (and scrolled)
        var settings = menu.settingsButton;
        if (settings == null) return;
        var parent = settings.transform.parent;
        if (parent == null) return;
        var existing = parent.Find(ObjectName);
        if (existing != null) { if (Enabled()) SetLabel(existing.gameObject); else Remove(existing.gameObject, settings); return; } // already added
        if (!Enabled()) return;
        var go = UnityEngine.Object.Instantiate(settings.gameObject, parent);
        go.name = ObjectName;
        // The game's localisation would put "Settings" back on the copy: remove whatever localises it.
        foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            string kind = "";
            try { kind = c.GetIl2CppType().FullName ?? ""; } catch { }
            if (kind.IndexOf("Locali", StringComparison.OrdinalIgnoreCase) >= 0 || kind.IndexOf("LocString", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("Translat", StringComparison.OrdinalIgnoreCase) >= 0 || kind.EndsWith(".LocText") || kind.EndsWith(".LocTMP"))
                try { UnityEngine.Object.DestroyImmediate(c); } catch { }
        }
        SetLabel(go);
        var b = go.GetComponent<Button>();
        b.onClick = new Button.ButtonClickedEvent(); // drops the Settings action the copy came with
        b.onClick.AddListener((Action)(() =>
        {
            try { OpenedFrom = go; SettingsMenu.Open(null); }
            catch (Exception e) { Log.Warn("Opening the settings: " + e.Message); }
        }));
        go.SetActive(true);
        go.transform.SetSiblingIndex(settings.transform.GetSiblingIndex() + 1);
        Relink(settings, b);
        Added.Add((go, settings));
        Log.Info($"Added \"{Label()}\" to the {(menu.title ? "title" : "pause")} menu.");
    }

    private static void SetLabel(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
        {
            t.text = Label();
            t.enableWordWrapping = false; // a longer word than "Settings" stays on one line (FB-56: "SWITCHB / OARD")
            t.overflowMode = TextOverflowModes.Overflow;
        }
    }

    /// <summary>
    /// FB-56 (NivalisMP's fix, the same bug): after the game's own updates, every frame a menu shows, the button takes the
    /// shape (its rect and its children's) of one of the menu's own buttons that isn't selected. A copy keeps whatever shape
    /// the Settings button had at the moment it was made, and on some PCs that's mid-animation or selected (narrower,
    /// shifted right: the label wrapped onto two lines). While it's selected itself, it's left to its own effects.
    /// </summary>
    private static int _shapeWarned;

    public static void LateTick()
    {
        if (Added.Count == 0) return;
        GameObject? selected = null;
        try { var es = EventSystem.current; selected = es != null ? es.currentSelectedGameObject : null; } catch { }
        for (int i = Added.Count - 1; i >= 0; i--)
        {
            var (go, settings) = Added[i];
            try
            {
                if (go == null || go.WasCollected || settings == null || settings.WasCollected) { Added.RemoveAt(i); continue; }
                if (!go.activeInHierarchy || (selected != null && selected.Pointer == go.Pointer)) continue;
                var rest = RestingButton(settings, go.transform, selected);
                if (rest != null) Follow(rest, go.transform, root: true);
            }
            catch (Exception e) { if (_shapeWarned++ == 0) Log.Debug("menu button shape: " + e.Message); } // kept: the settings still reach it (review)
        }
    }

    /// <summary>The Settings button if it isn't selected, else another of the menu's own buttons made the same way.</summary>
    private static Transform? RestingButton(Button settings, Transform copy, GameObject? selected)
    {
        var parent = copy.parent;
        if (parent == null) return null;
        bool Usable(Transform t) =>
            t.gameObject.activeInHierarchy && t.childCount == copy.childCount && t.GetComponent<Button>() != null
            && (selected == null || selected.Pointer != t.gameObject.Pointer);
        if (Usable(settings.transform)) return settings.transform;
        for (int i = 0; i < parent.childCount; i++)
        {
            var t = parent.GetChild(i);
            if (t == null || t.Pointer == copy.Pointer || t.name.StartsWith("NivalisMP.", StringComparison.Ordinal)
                || t.name.StartsWith("NivalisSwitchboard.", StringComparison.Ordinal)) continue;
            if (Usable(t)) return t;
        }
        return null;
    }

    /// <summary>Gives <paramref name="to"/> the rect of <paramref name="from"/>, and its children theirs (the menu's layout places the button itself).</summary>
    private static void Follow(Transform from, Transform to, bool root)
    {
        var a = from.TryCast<RectTransform>();
        var b = to.TryCast<RectTransform>();
        // A button the menu's layout group sizes is left to it: its size from its own label would differ from Settings', and
        // the two would rewrite it every frame (review). A layout that only places the buttons still gets the fix.
        bool laidOut = false;
        if (root && to.parent != null)
        {
            var lg = to.parent.GetComponent<LayoutGroup>();
            if (lg != null && lg.enabled)
            {
                var hv = lg.TryCast<HorizontalOrVerticalLayoutGroup>();
                laidOut = hv == null || hv.childControlWidth || hv.childControlHeight; // a grid sizes its cells itself
            }
        }
        if (a != null && b != null && !laidOut)
        {
            if (b.anchorMin != a.anchorMin) b.anchorMin = a.anchorMin;
            if (b.anchorMax != a.anchorMax) b.anchorMax = a.anchorMax;
            if (b.pivot != a.pivot) b.pivot = a.pivot;
            if (b.sizeDelta != a.sizeDelta) b.sizeDelta = a.sizeDelta;
            if (!root)
            {
                if (b.anchoredPosition != a.anchoredPosition) b.anchoredPosition = a.anchoredPosition;
                if (b.localScale != a.localScale) b.localScale = a.localScale;
            }
        }
        int n = Math.Min(from.childCount, to.childCount);
        for (int i = 0; i < n; i++) Follow(from.GetChild(i), to.GetChild(i), root: false);
    }

    /// <summary>MenuButton or MenuButtonLabel changed: each button added shows the new label, or goes (it comes back the next time the menu opens).</summary>
    /// <summary>Set when a setting changed (on any thread); the button is brought up to date on the game's thread.</summary>
    public static volatile bool RefreshSoon;

    public static void Refresh()
    {
        RefreshSoon = false;
        for (int i = Added.Count - 1; i >= 0; i--)
        {
            var (go, settings) = Added[i];
            try
            {
                if (go == null || go.WasCollected) { Added.RemoveAt(i); continue; }
                if (Enabled()) SetLabel(go);
                else Remove(go, settings);
            }
            catch (Exception e) { Log.Debug("updating the menu button: " + e.Message); Added.RemoveAt(i); }
        }
    }

    /// <summary>The button taken off its menu, the gamepad links put back as they were.</summary>
    private static void Remove(GameObject go, Button? settings)
    {
        Added.RemoveAll(a => a.go == go);
        try
        {
            var added = go.GetComponent<Button>();
            if (settings != null && added != null)
            {
                var nav = settings.navigation;
                if (nav.mode == Navigation.Mode.Explicit && nav.selectOnDown == added)
                {
                    var after = added.navigation.selectOnDown;
                    nav.selectOnDown = after;
                    settings.navigation = nav;
                    if (after != null) { var an = after.navigation; if (an.selectOnUp == added) { an.selectOnUp = settings; after.navigation = an; } }
                }
            }
        }
        catch (Exception e) { Log.Debug("gamepad links back: " + e.Message); }
        if (OpenedFrom == go) OpenedFrom = settings != null ? settings.gameObject : null; // the selection goes back to Settings
        UnityEngine.Object.Destroy(go);
    }

    /// <summary>When the menu's buttons are linked by hand for the gamepad, the new one goes in right after Settings.</summary>
    private static void Relink(Button settings, Button added)
    {
        try
        {
            var nav = settings.navigation;
            if (nav.mode != Navigation.Mode.Explicit) return;
            var after = nav.selectOnDown;
            var n = added.navigation;
            n.mode = Navigation.Mode.Explicit;
            n.selectOnUp = settings;
            n.selectOnDown = after;
            added.navigation = n;
            nav.selectOnDown = added;
            settings.navigation = nav;
            if (after != null)
            {
                var an = after.navigation;
                an.selectOnUp = added;
                after.navigation = an;
            }
        }
        catch (Exception e) { Log.Debug("gamepad links for the button: " + e.Message); }
    }

    /// <summary>The game's menu selection is cleared while the menu is up (its d-pad moves stay ours), and given back after.</summary>
    public static void HoldSelection(bool hold)
    {
        try
        {
            var es = EventSystem.current;
            if (es == null) return;
            if (hold) es.SetSelectedGameObject(null);
            else if (OpenedFrom != null && OpenedFrom.activeInHierarchy) es.SetSelectedGameObject(OpenedFrom);
        }
        catch { }
        if (!hold) OpenedFrom = null;
    }

    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.Start))]
    internal static class StartPatch
    {
        private static void Postfix(MainMenuUI __instance)
        {
            try { Inject(__instance); } catch (Exception e) { Log.Warn("Adding the settings button: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.Show))]
    internal static class ShowPatch
    {
        private static void Postfix(MainMenuUI __instance)
        {
            try { Inject(__instance); } catch (Exception e) { Log.Warn("Adding the settings button: " + e.Message); }
        }
    }

    /// <summary>Esc and the pause key go to the menu while it's up (or on the frame it closed), not to the game's menu.</summary>
    private static bool LetThrough() => !SettingsMenu.IsOpen && SettingsMenu.ClosedFrame != Time.frameCount;

    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.OnCancelAction))]
    internal static class CancelPatch { private static bool Prefix() => LetThrough(); }

    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.OnPauseAction))]
    internal static class PausePatch { private static bool Prefix() => LetThrough(); }
}
