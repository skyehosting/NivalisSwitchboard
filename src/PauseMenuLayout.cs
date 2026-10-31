using System;
using System.Collections.Generic;
using Nivalis;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NivalisSwitchboard;

/// <summary>
/// FB-55: the game's pause menu with its buttons centred on the screen (mods' buttons pushed it down to the bottom edge).
/// More than fit scroll with the mouse wheel, fading toward the edge they run past; a controller's selection is scrolled
/// into view. The buttons are measured where they are every frame the menu is up, so whatever the game or a mod adds to
/// it is taken in. CenterPauseMenu off puts everything back.
/// </summary>
internal static class PauseMenuLayout
{
    public static Func<bool> Enabled = () => true;
    /// <summary>Set after a failure: the menu is put back as the game had it and left alone for the session.</summary>
    public static bool Broken;
    private static MainMenuUI? _menu;
    private static RectTransform? _moved;   // what's shifted: the buttons' holder (or the first one up a layout doesn't place)
    private static Canvas? _canvas;
    private static Vector2 _base, _lastSet; // its own position, and the one set last
    private static bool _applied, _said, _wasUp, _faded;
    private static double _target, _shown;  // the scroll wanted, and the one shown (eased toward it)
    private static double _lastTop = double.NaN, _lastBottom = double.NaN; // the buttons' edges last used
    private static GameObject? _lastSelected;
    private static float _lastTick, _mouseAt = -10f;
    private static readonly List<(RectTransform rt, CanvasGroup? group)> Items = new();
    private static readonly HashSet<IntPtr> OurGroups = new();

    /// <summary>The pause menu (not the title menu), from the button's own hook on its Start and Show.</summary>
    public static void Attach(MainMenuUI menu)
    {
        if (menu == null || menu.title) return;
        if (_menu != null && !_menu.WasCollected && _menu.Pointer == menu.Pointer) return;
        Restore();
        OurGroups.Clear(); // the old menu's are gone with it (review: pointers can be reused)
        Broken = false;    // a new menu (a new scene): tried again
        _menu = menu;
        _moved = null;
        _canvas = null;
        _said = false;
    }

    /// <summary>Once a frame.</summary>
    public static void Tick()
    {
        var menu = _menu;
        if (menu == null || Broken || menu.title) return; // the pause menu only, even if the game reused it as the title menu
        if (menu.WasCollected) { _menu = null; _moved = null; _canvas = null; _applied = false; Items.Clear(); return; }
        if (!Enabled()) { if (_applied || _faded) Restore(); return; }
        bool up = menu.gameObject.activeInHierarchy && (menu.menuCanvasGroup == null || menu.menuCanvasGroup.alpha > 0.01f);
        if (!up)
        {
            if (_wasUp) { ShowAll(); _target = _shown = 0; _lastSelected = null; } // opened again at the top, every button shown
            _wasUp = false;
            return;
        }
        _wasUp = true;

        var settingsButton = menu.settingsButton;
        if (settingsButton == null) return;
        var holder = settingsButton.transform.parent;
        var list = holder != null ? holder.TryCast<RectTransform>() : null;
        if (list == null) return;
        if (_moved == null) { _moved = Movable(list); _applied = false; if (_moved == null) { Broken = true; return; } }
        if (_canvas == null) _canvas = list.GetComponentInParent<Canvas>();
        Camera? cam = null;
        if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            cam = _canvas.worldCamera != null ? _canvas.worldCamera : Camera.main;
            if (cam == null) return; // no camera to measure through: nothing is moved
        }
        var parent = _moved.parent;
        if (parent == null) return;

        // Its own place: taken again whenever something other than this moved it (the game's own animation).
        if (!_applied || (_moved.anchoredPosition - _lastSet).sqrMagnitude > 0.01f) { _base = _moved.anchoredPosition; _applied = true; }
        float pxPerUnit = (RectTransformUtility.WorldToScreenPoint(cam, parent.TransformPoint(new Vector3(0, 100, 0)))
                           - RectTransformUtility.WorldToScreenPoint(cam, parent.TransformPoint(Vector3.zero))).y / 100f;
        if (Mathf.Abs(pxPerUnit) < 1e-4f) return;
        double nowShift = (_moved.anchoredPosition.y - _base.y) * pxPerUnit;
        double Y(RectTransform rt, float localY) => RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(new Vector3(0, localY, 0))).y - nowShift;

        // The buttons where the game put them: measured now, less the shift already on them.
        Items.Clear();
        double top = double.MinValue, bottom = double.MaxValue;
        for (int i = 0; i < list.childCount; i++)
        {
            var child = list.GetChild(i);
            var rt = child != null ? child.TryCast<RectTransform>() : null;
            if (rt == null || !rt.gameObject.activeSelf || rt.rect.height < 1f) continue;
            top = Math.Max(top, Y(rt, rt.rect.yMax));
            bottom = Math.Min(bottom, Y(rt, rt.rect.yMin));
            Items.Add((rt, Group(rt)));
        }
        if (Items.Count == 0) return;
        // A button growing under the pointer moves the list's edges by a few pixels: that doesn't re-centre it (review).
        if (Math.Abs(top - _lastTop) < 3 && Math.Abs(bottom - _lastBottom) < 3) { top = _lastTop; bottom = _lastBottom; }
        else { _lastTop = top; _lastBottom = bottom; }
        double screenH = Screen.height;

        var plan = PauseLayout.Plan(top, bottom, screenH, _target);
        var mouse = Mouse.current;
        try { if (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0.01f) _mouseAt = Time.unscaledTime; } catch { }
        // The wheel and the selection belong to this menu only while nothing is over it: ours, or the game's own Save, Load
        // and Settings panels (review: the wheel would have scrolled the hidden buttons under them).
        bool onTop = !SettingsMenu.IsOpen && !Shown(menu.savePanel) && !Shown(menu.loadPanel) && !Shown(menu.settingsPanel)
                     && !(menu.exitPopup != null && menu.exitPopup.gameObject.activeInHierarchy);
        if (onTop)
        {
            try { if (mouse != null) _target = PauseLayout.Wheel(_target, mouse.scroll.ReadValue().y, screenH, plan); } catch { }
            var es = EventSystem.current;
            var selected = es != null ? es.currentSelectedGameObject : null;
            if (selected != null && selected != _lastSelected)
            {
                _lastSelected = selected;
                // A controller's or keyboard's move only: one the mouse made (hovering) would chase the pointer (review).
                if (Time.unscaledTime - _mouseAt > 0.3f)
                    foreach (var (rt, _) in Items)
                    {
                        if (!selected.transform.IsChildOf(rt)) continue;
                        var at = PauseLayout.Plan(top, bottom, screenH, _target);
                        _target = PauseLayout.ScrollToShow(Y(rt, rt.rect.yMax) + at.Shift, Y(rt, rt.rect.yMin) + at.Shift, screenH, at);
                        break;
                    }
            }
        }
        plan = PauseLayout.Plan(top, bottom, screenH, _target);
        _target = plan.Scroll;
        float dt = Mathf.Clamp(Time.unscaledTime - _lastTick, 0f, 0.1f);
        _lastTick = Time.unscaledTime;
        _shown += (_target - _shown) * Math.Min(1.0, dt * 14.0); // eased: a notch glides
        if (Math.Abs(_target - _shown) < 0.5) _shown = _target;
        var shownPlan = PauseLayout.Plan(top, bottom, screenH, _shown);

        _lastSet = _base + new Vector2(0, (float)(shownPlan.Shift / pxPerUnit));
        _moved.anchoredPosition = _lastSet;

        foreach (var (rt, group) in Items)
        {
            float a = (float)PauseLayout.Alpha(Y(rt, rt.rect.center.y) + shownPlan.Shift, screenH, shownPlan);
            var g = group;
            if (g == null && a < 0.999f) g = AddGroup(rt);
            if (g == null) continue;
            g.alpha = a;
            g.interactable = true;              // a controller can still move onto it (and it's scrolled into view)
            g.blocksRaycasts = a > 0.35f;       // a click on one faded nearly out doesn't go to it by mistake
            if (a < 0.999f) _faded = true;
        }
        if (!_said)
        {
            _said = true;
            Log.Info($"Pause menu: {Items.Count} buttons {(plan.Scrolls ? "scroll with the mouse wheel (more than fit)" : "centred")}" +
                     (_moved != list ? $" (moved '{_moved.name}', as a layout places '{list.name}')." : "."));
        }
    }

    private static bool Shown(Component? panel)
    {
        try { return panel != null && panel.gameObject.activeInHierarchy; } catch { return false; }
    }

    /// <summary>
    /// The buttons' holder, or the first one up that an active layout group doesn't place (a layout would put it back),
    /// never one holding anything besides the buttons (the heading would move with them: review).
    /// </summary>
    private static RectTransform? Movable(RectTransform list)
    {
        var rt = list;
        for (int i = 0; i < 4 && rt != null; i++)
        {
            var p = rt.parent;
            if (p == null) return rt;
            var layout = p.GetComponent<LayoutGroup>();
            if (layout == null || !layout.enabled) return rt;
            if (p.GetComponent<Canvas>() != null || ActiveChildren(p) > 1) break;
            rt = p.TryCast<RectTransform>();
        }
        Log.Warn("Pause menu: its buttons are placed by the game's layout with other things; they're left where they are.");
        return null;
    }

    private static int ActiveChildren(Transform t)
    {
        int n = 0;
        for (int i = 0; i < t.childCount; i++) { var c = t.GetChild(i); if (c != null && c.gameObject.activeSelf) n++; }
        return n;
    }

    /// <summary>The fade this adds to a button (never one the game put there, which its own animations use).</summary>
    private static CanvasGroup? Group(RectTransform rt)
    {
        var g = rt.GetComponent<CanvasGroup>();
        return g != null && OurGroups.Contains(g.Pointer) ? g : null;
    }

    private static CanvasGroup? AddGroup(RectTransform rt)
    {
        if (rt.GetComponent<CanvasGroup>() != null) return null; // the game's own: left alone
        var g = rt.gameObject.AddComponent<CanvasGroup>();
        OurGroups.Add(g.Pointer);
        return g;
    }

    /// <summary>Every button fully shown and clickable.</summary>
    private static void ShowAll()
    {
        _faded = false;
        try
        {
            var holder = _menu != null && !_menu.WasCollected && _menu.settingsButton != null ? _menu.settingsButton.transform.parent : null;
            if (holder == null) return;
            for (int i = 0; i < holder.childCount; i++)
            {
                var child = holder.GetChild(i);
                var rt = child != null ? child.TryCast<RectTransform>() : null;
                if (rt != null && Group(rt) is { } g) { g.alpha = 1; g.interactable = g.blocksRaycasts = true; }
            }
        }
        catch (Exception e) { Log.Debug("pause menu buttons back: " + e.Message); }
    }

    /// <summary>Back as the game had it: its place, and every button fully shown.</summary>
    public static void Restore()
    {
        try { if (_applied && _moved != null && !_moved.WasCollected) _moved.anchoredPosition = _base; }
        catch (Exception e) { Log.Debug("pause menu back: " + e.Message); }
        ShowAll();
        _applied = false;
        _target = _shown = 0;
    }
}
