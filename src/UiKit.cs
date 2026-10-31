using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using TMPro;
using UnityEngine;

namespace NivalisSwitchboard;

internal static class Log
{
    public static ManualLogSource? Source;
    public static void Info(string msg) => Source?.LogInfo(msg);
    public static void Warn(string msg) => Source?.LogWarning(msg);
    public static void Debug(string msg) => Source?.LogDebug(msg);
}

/// <summary>The small building blocks the menu is made of: text, rectangles, the game's fonts, escaping.</summary>
internal static class UiKit
{
    /// <summary>Text shown as it is: TextMeshPro markup in it isn't read (a closing noparse in any case stays text).</summary>
    public static string Escape(string? s) =>
        string.IsNullOrEmpty(s) ? "" : "<noparse>" + EndTag.Replace(s, "</ noparse>") + "</noparse>";

    private static readonly Regex EndTag = new(@"</\s*noparse\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static TextMeshProUGUI Text(Transform parent, string name, float size, Color color, TextAlignmentOptions align, TMP_FontAsset? font)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>().SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.richText = true;
        t.parseCtrlCharacters = false;
        return t;
    }

    public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        Place(rt, anchorMin, anchorMax, offsetMin, offsetMax);
        return rt;
    }

    public static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    public static bool Over(RectTransform? rt, Vector2 screenPos) =>
        rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null);

    /// <summary>The game's Barlow family: a bold weight for names, a regular one for the rest; the HUD's font otherwise.</summary>
    public static (TMP_FontAsset? title, TMP_FontAsset? body) PickFonts()
    {
        TMP_FontAsset? hud = null, title = null, body = null;
        try
        {
            var counts = new Dictionary<IntPtr, (TMP_FontAsset font, int n)>();
            foreach (var t in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>())
            {
                var f = t.font;
                if (f == null) continue;
                counts[f.Pointer] = counts.TryGetValue(f.Pointer, out var e) ? (f, e.n + 1) : (f, 1);
            }
            if (counts.Count > 0) hud = counts.Values.OrderByDescending(v => v.n).First().font;
        }
        catch (Exception e) { Log.Debug("font scan: " + e.Message); }
        try
        {
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Where(f => f != null).ToList();
            TMP_FontAsset? Find(params string[] prefixes)
            {
                foreach (var p in prefixes)
                    foreach (var f in all)
                        if ((f.name ?? "").StartsWith(p, StringComparison.OrdinalIgnoreCase)) return f;
                return null;
            }
            title = Find("Barlow-ExtraBold", "Barlow-Bold", "Barlow-Black", "Barlow-SemiBold");
            body = Find("Barlow-Medium", "Barlow-Regular", "Barlow-SemiBold", "Barlow SDF", "Barlow");
        }
        catch (Exception e) { Log.Debug("font list: " + e.Message); }
        try { hud ??= TMP_Settings.defaultFontAsset; } catch { }
        return (title ?? hud, body ?? hud);
    }
}
