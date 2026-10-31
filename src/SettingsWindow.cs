using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NivalisSwitchboard;

/// <summary>
/// The menu: a locked 16:9 window (the same shape on every monitor). Down the left, one tree: each of the author's mods a
/// heading (a divider above it), the open mod's groups under it, the column scrolling when it's long. On the right, the
/// group's settings: pill switches, bars filled from the left, dropdowns, text fields, keys, and any rows the mod's page
/// adds. The footer explains what's under the highlight. The mouse, the keyboard and a controller work side by side: the
/// d-pad or stick moves through the tree and the settings, A chooses, B goes back (and from the tree closes the menu), LB/RB
/// go to the previous / next group, a text field opens the on-screen keyboard. Every change is applied at once.
/// </summary>
internal sealed partial class SettingsWindow
{
    internal static readonly Color NameColor = new Color32(217, 165, 90, 255);
    internal static readonly Color Body = new Color32(207, 203, 196, 255);
    internal static readonly Color Muted = new Color32(138, 133, 125, 255);
    internal static readonly Color GoldDim = new Color32(118, 97, 62, 255);
    internal static readonly Color FocusTint = new Color32(196, 160, 98, 46);
    internal static readonly Color HoverTint = new Color32(196, 160, 98, 22);
    private static readonly Color ButtonIdle = Color.white, ButtonHover = new(1f, 0.86f, 0.62f, 1f);

    private const float W = 1280f, H = 720f, TreeW = 270f, HeaderH = 84f, FooterH = 104f;
    internal const float RowH = 64f;
    private const float ModH = 44f, GroupH = 32f;

    private GameObject? _root;
    private RectTransform? _window, _treeView, _treeContent, _viewport, _content, _closeBtn, _kbBtn, _kbOverlay, _kbGridRt, _dropOverlay;
    private CanvasGroup? _fade;
    private TextMeshProUGUI? _groupTitle, _intro, _help, _legend, _kbTitle, _kbTyped, _empty;
    private Image? _closeImg, _kbImg;
    private TMP_FontAsset? _titleFont, _bodyFont;
    private readonly KeyboardPanel _keys = new();

    private List<FoundMod> _mods = new();
    private enum Zone { Tree, Rows, Bar }
    private Zone _zone = Zone.Tree;
    private int _mod = -1, _focus;
    private string _group = "";
    private float _scroll, _scrollTarget, _treeScroll, _treeTarget;
    private bool _textFocusedLastFrame;
    private string _note = "";
    private float _noteUntil;
    /// <summary>The group last shown for each mod (by GUID): opening the menu again comes back to it.</summary>
    private readonly Dictionary<string, string> _lastGroup = new();
    private string? _lastMod;

    public bool Visible => _root != null && _root.activeSelf;
    /// <summary>The menu asked to close (B from the tree, Esc, its X).</summary>
    public bool WantsClose { get; set; }

    public void Build(TMP_FontAsset? titleFont, TMP_FontAsset? bodyFont)
    {
        if (_root != null) return;
        _titleFont = titleFont;
        _bodyFont = bodyFont;
        _root = new GameObject("NivalisSwitchboard.Window");
        UnityEngine.Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32010; // over People of Nivalis's chat window and the game's menus
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _root.AddComponent<GraphicRaycaster>();

        // Always 1280 x 720 of the 1920 x 1080 layout: always 16:9, scaled with the screen.
        _window = UiKit.Rect(_root.transform, "Window", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _window.sizeDelta = new Vector2(W, H);
        _fade = _window.gameObject.AddComponent<CanvasGroup>();
        var bg = _window.gameObject.AddComponent<Image>();
        bg.sprite = Sprites.Bubble;
        bg.type = Image.Type.Sliced;

        var heading = UiKit.Text(_window, "Heading", 15, Muted, TextAlignmentOptions.TopLeft, _bodyFont);
        UiKit.Place(heading.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -44), new Vector2(TreeW, -18));
        heading.fontStyle = FontStyles.UpperCase;
        heading.characterSpacing = 2f;
        heading.text = "Switchboard";
        var title = UiKit.Text(_window, "Title", 30, NameColor, TextAlignmentOptions.TopLeft, _titleFont);
        UiKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -80), new Vector2(TreeW, -40));
        title.fontStyle = FontStyles.UpperCase;
        title.characterSpacing = 1.5f;
        title.text = "Settings";

        _groupTitle = UiKit.Text(_window, "Group", 24, NameColor, TextAlignmentOptions.TopLeft, _titleFont);
        UiKit.Place(_groupTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(TreeW + 26, -50), new Vector2(-150, -20));
        _groupTitle.fontStyle = FontStyles.UpperCase;
        _groupTitle.enableWordWrapping = false;
        _groupTitle.overflowMode = TextOverflowModes.Ellipsis;
        _intro = UiKit.Text(_window, "Intro", 15, Muted, TextAlignmentOptions.TopLeft, _bodyFont);
        UiKit.Place(_intro.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(TreeW + 26, -HeaderH + 4), new Vector2(-150, -50));
        _intro.enableWordWrapping = true;
        _intro.overflowMode = TextOverflowModes.Ellipsis;

        (_closeBtn, _closeImg) = HeaderButton("Close", null, "X", -18);
        (_kbBtn, _kbImg) = HeaderButton("Keyboard", Sprites.KeyboardIcon, "", -74);

        // The tree, down the left, scrolling; a line between it and the settings.
        _treeView = UiKit.Rect(_window, "Tree", new Vector2(0, 0), new Vector2(0, 1), new Vector2(14, FooterH + 4), new Vector2(TreeW - 10, -HeaderH - 6));
        _treeView.gameObject.AddComponent<RectMask2D>();
        _treeContent = UiKit.Rect(_treeView, "Content", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        _treeContent.pivot = new Vector2(0.5f, 1f);
        Line(_window, new Vector2(0, 0), new Vector2(0, 1), new Vector2(TreeW, FooterH + 6), new Vector2(TreeW + 1, -HeaderH - 6));

        // The settings, scrolling.
        _viewport = UiKit.Rect(_window, "Settings", Vector2.zero, Vector2.one, new Vector2(TreeW + 14, FooterH), new Vector2(-22, -HeaderH - 6));
        _viewport.gameObject.AddComponent<RectMask2D>();
        _content = UiKit.Rect(_viewport, "Content", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        _content.pivot = new Vector2(0.5f, 1f);
        _empty = UiKit.Text(_viewport, "Empty", 18, Muted, TextAlignmentOptions.Center, _bodyFont);
        UiKit.Place(_empty.rectTransform, Vector2.zero, Vector2.one, new Vector2(40, 0), new Vector2(-40, 0));
        _empty.enableWordWrapping = true;
        _empty.gameObject.SetActive(false);

        // The footer: what the setting under the highlight does, and what the buttons do.
        Line(_window, new Vector2(0, 0), new Vector2(1, 0), new Vector2(22, FooterH - 2), new Vector2(-22, FooterH - 1));
        _help = UiKit.Text(_window, "Help", 16, Body, TextAlignmentOptions.TopLeft, _bodyFont);
        UiKit.Place(_help.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(30, 36), new Vector2(-30, FooterH - 10));
        _help.enableWordWrapping = true;
        _help.enableAutoSizing = true;
        _help.fontSizeMin = 12;
        _help.fontSizeMax = 16;
        _help.overflowMode = TextOverflowModes.Ellipsis;
        _legend = UiKit.Text(_window, "Legend", 14, Muted, TextAlignmentOptions.BottomLeft, _bodyFont);
        UiKit.Place(_legend.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(30, 12), new Vector2(-30, 34));
        _legend.enableWordWrapping = false;

        BuildKeyboardOverlay();
        BuildDropdown();
        BuildSaveBar();
        BuildLeaveDialog();
        _root.SetActive(false);
    }

    private static Image Line(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var rt = UiKit.Rect(parent, "Line", aMin, aMax, oMin, oMax);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Sprites.Cell;
        img.color = new Color(GoldDim.r, GoldDim.g, GoldDim.b, 0.5f);
        img.raycastTarget = false;
        return img;
    }

    private (RectTransform rt, Image img) HeaderButton(string name, Sprite? icon, string letter, float x)
    {
        var rt = UiKit.Rect(_window!, name, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        rt.pivot = new Vector2(1, 1);
        rt.sizeDelta = new Vector2(46, 46);
        rt.anchoredPosition = new Vector2(x, -18);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Sprites.Pill;
        img.type = Image.Type.Sliced;
        if (icon != null)
        {
            var irt = UiKit.Rect(rt, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            irt.sizeDelta = new Vector2(28, 28);
            var ii = irt.gameObject.AddComponent<Image>();
            ii.sprite = icon;
            ii.color = NameColor;
            ii.raycastTarget = false;
        }
        else
        {
            var t = UiKit.Text(rt, "Letter", 20, NameColor, TextAlignmentOptions.Center, _titleFont);
            UiKit.Place(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            t.text = letter;
        }
        return (rt, img);
    }

    // ------------------------------------------------------------------ opening and closing

    public void Open(List<FoundMod> mods, string? guid)
    {
        if (_root == null) return;
        WantsClose = false;
        _mods = mods;
        _root.SetActive(true);
        if (_fade != null) _fade.alpha = 1f;
        _note = "";
        _textFocusedLastFrame = false;
        _mod = -1;
        _treeOpen = -1;
        Show(guid ?? _lastMod);
        _zone = Zone.Tree;
    }

    /// <summary>Shows this mod's settings (its last group, or its first), or the first mod's when it isn't one found.</summary>
    public void Show(string? guid)
    {
        if (_mods.Count == 0) { BuildTree(); ShowEmpty(); return; }
        int m = Math.Max(0, _mods.FindIndex(x => x.Guid == guid));
        var groups = _mods[m].Groups();
        string g = _lastGroup.TryGetValue(_mods[m].Guid, out var last) && groups.Any(x => x.name == last) ? last : groups.FirstOrDefault().name ?? "";
        Select(m, g);
    }

    private void ShowEmpty()
    {
        if (_groupTitle != null) _groupTitle.text = "No mods found";
        if (_intro != null) _intro.text = "";
        if (_empty != null)
        {
            _empty.text = "None of the mods this menu is for are installed (People of Nivalis, and the others as they come). " +
                          "Their settings show here as soon as they are.";
            _empty.gameObject.SetActive(true);
        }
        ClearRows();
    }

    public void Close()
    {
        if (_root == null) return;
        CommitPending();
        CloseKeyboard(save: false);
        CloseDropdown();
        _capture = null;
        CommitTexts(); // a field still being typed in keeps what was typed (like clicking away from it)
        StopPreview();
        try { EventSystem.current?.SetSelectedGameObject(null); } catch { }
        if (_mod >= 0 && _mod < _mods.Count) { _lastMod = _mods[_mod].Guid; _lastGroup[_lastMod] = _group; }
        _root.SetActive(false);
        WantsClose = false;
    }

    public void Destroy()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null;
        _window = _treeView = _treeContent = _viewport = _content = _closeBtn = _kbBtn = _kbOverlay = _kbGridRt = _dropOverlay = null;
        _tree.Clear();
        _rows.Clear();
        _keys.Destroy();
    }

    // ------------------------------------------------------------------ the tree: mods, and the open one's groups

    private sealed class TreeItem
    {
        public int Mod;
        public string? Group; // null: the mod's heading (or a category's)
        /// <summary>A category's heading (its groups fold under it).</summary>
        public string? Category;
        public RectTransform Rt = null!;
        public Image Bg = null!;
        public TextMeshProUGUI Text = null!;
        public float Top;
    }

    private readonly List<TreeItem> _tree = new();
    private float _treeHeight;
    /// <summary>The tree item the controller is on (in the tree zone).</summary>
    private int _treeFocus;

    private void BuildTree()
    {
        foreach (var t in _tree) if (t.Rt != null) UnityEngine.Object.Destroy(t.Rt.gameObject);
        _tree.Clear();
        float y = 0;
        for (int m = 0; m < _mods.Count; m++)
        {
            var mod = _mods[m];
            if (m > 0)
            {
                y += 6;
                var div = UiKit.Rect(_treeContent!, "Divider", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
                div.pivot = new Vector2(0.5f, 1f);
                div.sizeDelta = new Vector2(-12, 1);
                div.anchoredPosition = new Vector2(0, -y);
                var di = div.gameObject.AddComponent<Image>();
                di.sprite = Sprites.Cell;
                di.color = new Color(GoldDim.r, GoldDim.g, GoldDim.b, 0.7f);
                di.raycastTarget = false;
                _tree.Add(new TreeItem { Mod = -1, Rt = div, Bg = di, Text = null!, Top = y });
                y += 7;
            }
            bool open = m == _treeOpen;
            AddTreeItem(m, null, (open ? "▾  " : "▸  ") + mod.Name, mod.Version, ref y, ModH);
            if (!open) continue;
            string? lastCat = null;
            foreach (var (name, _) in mod.Groups())
            {
                // A category's groups sit under its folding heading (0.4.0).
                string? cat = mod.CategoryOf(name);
                bool catOpen = cat == null || _catOpen.Contains(CatKey(mod, cat));
                if (cat != lastCat)
                {
                    lastCat = cat;
                    if (cat != null) AddTreeItem(m, null, (catOpen ? "▾  " : "▸  ") + cat, "", ref y, GroupH, cat);
                }
                if (!catOpen) continue;
                AddTreeItem(m, name, name, "", ref y, GroupH, null, cat != null ? 48f : 30f);
            }
            y += 4;
        }
        _treeHeight = y + 8;
        _treeContent!.sizeDelta = new Vector2(0, _treeHeight);
    }

    private void AddTreeItem(int mod, string? group, string label, string side, ref float y, float h, string? category = null, float indent = 30f)
    {
        var rt = UiKit.Rect(_treeContent!, group == null ? "Mod" : "Group", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0, h - 2);
        rt.anchoredPosition = new Vector2(0, -y);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = Sprites.Field;
        bg.type = Image.Type.Sliced;
        bg.enabled = false;
        bool heading = group == null && category == null;
        var text = UiKit.Text(rt, "Name", heading ? 17 : category != null ? 14 : 16, heading ? Body : Muted, TextAlignmentOptions.MidlineLeft, heading ? _titleFont : _bodyFont);
        UiKit.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(heading ? 10 : category != null ? 30 : indent, 0), new Vector2(side.Length > 0 ? -58 : -8, 0));
        if (category != null) { text.fontStyle = FontStyles.UpperCase | FontStyles.Bold; text.characterSpacing = 2f; }
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.text = UiKit.Escape(label);
        if (heading) text.fontStyle = FontStyles.UpperCase;
        if (side.Length > 0)
        {
            var v = UiKit.Text(rt, "Version", 12, Muted, TextAlignmentOptions.MidlineRight, _bodyFont);
            UiKit.Place(v.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-56, 0), new Vector2(-8, 0));
            v.enableWordWrapping = false;
            v.text = UiKit.Escape(side);
        }
        _tree.Add(new TreeItem { Mod = mod, Group = group, Category = category, Rt = rt, Bg = bg, Text = text, Top = y });
        y += h;
    }

    private List<TreeItem> Pickable() => _tree.Where(t => t.Mod >= 0).ToList();

    /// <summary>Opens this mod in the tree and shows this group of it.</summary>
    private void Select(int mod, string group)
    {
        if (_mods.Count == 0) return;
        CommitPending();
        CommitTexts();
        CloseDropdown();
        StopPreview();
        if (_mod >= 0 && _mod < _mods.Count && _group.Length > 0) _lastGroup[_mods[_mod].Guid] = _group;
        _mod = Math.Clamp(mod, 0, _mods.Count - 1);
        _group = group;
        bool catOpened = _mods[_mod].CategoryOf(group) is { } cat && _catOpen.Add(CatKey(_mods[_mod], cat));
        if (_treeOpen != _mod || _tree.Count == 0 || catOpened) { _treeOpen = _mod; BuildTree(); }
        var m = _mods[_mod];
        var intro = m.Groups().FirstOrDefault(g => g.name == group).intro ?? "";
        if (_groupTitle != null) _groupTitle.text = UiKit.Escape(m.Name) + "  <color=#8A857D>·</color>  " + UiKit.Escape(group);
        if (_intro != null) _intro.text = UiKit.Escape(intro);
        if (_empty != null) _empty.gameObject.SetActive(false);
        BuildRows(m, group);
        _focus = 0;
        _scroll = _scrollTarget = 0f;
        var picks = Pickable();
        _treeFocus = Math.Max(0, picks.FindIndex(t => t.Mod == _mod && t.Group == group));
        TreeScrollTo(_treeFocus);
    }

    /// <summary>A mod's heading chosen: it opens (on its last group) or, open already, folds back to its heading.</summary>
    private void ToggleMod(int mod)
    {
        if (mod == _mod)
        {
            // folding the open mod: its groups go, the heading stays highlighted
            CommitPending();
            CommitTexts();
            if (_group.Length > 0) _lastGroup[_mods[_mod].Guid] = _group;
            _treeOpen = _treeOpen == mod ? -1 : mod; // folds, or opens again
            BuildTree();
            _treeFocus = Math.Max(0, Pickable().FindIndex(t => t.Mod == mod && t.Group == null));
            TreeScrollTo(_treeFocus);
            return;
        }
        var groups = _mods[mod].Groups();
        string g = _lastGroup.TryGetValue(_mods[mod].Guid, out var last) && groups.Any(x => x.name == last) ? last : groups.FirstOrDefault().name ?? "";
        Guarded(() => Select(mod, g));
    }

    /// <summary>The mod whose groups the tree shows (-1: all folded); the mod shown on the right stays <see cref="_mod"/>.</summary>
    private int _treeOpen = -1;

    /// <summary>The mod shown is folded away in the tree: its heading is the one highlighted.</summary>
    private bool FoldedOpen => _treeOpen != _mod;

    private void TreeScrollTo(int index)
    {
        var picks = Pickable();
        if (_treeView == null || index < 0 || index >= picks.Count) return;
        float top = picks[index].Top, bottom = top + picks[index].Rt.sizeDelta.y + 2, view = _treeView.rect.height;
        if (top < _treeTarget + 6f) _treeTarget = Mathf.Max(0f, top - 6f);
        else if (bottom > _treeTarget + view - 6f) _treeTarget = bottom - view + 6f;
    }

    /// <summary>The group before or after this one, across the mods (LB / RB).</summary>
    private void StepGroup(int dir)
    {
        if (_mods.Count == 0) return;
        var all = new List<(int mod, string group)>();
        for (int m = 0; m < _mods.Count; m++) foreach (var (name, _) in _mods[m].Groups()) all.Add((m, name));
        if (all.Count == 0) return;
        int i = all.FindIndex(x => x.mod == _mod && x.group == _group);
        var next = all[((i + dir) % all.Count + all.Count) % all.Count];
        Guarded(() => Select(next.mod, next.group));
    }

    // ------------------------------------------------------------------ each frame

    public void Tick()
    {
        if (!Visible || _window == null) return;
        float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        Vector2 pos = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
        float wheel = mouse != null ? mouse.scroll.ReadValue().y : 0f;
        bool esc = kb != null && kb.escapeKey.wasPressedThisFrame;
        bool textFocused = AnyTextFocused();

        if (Asking) TickLeave(pos, click, esc);
        else if (_capture != null) TickCapture(kb, esc, click);
        else if (_kbOpen) TickKeyboard(esc);
        else if (_dropRow != null) TickDropdown(pos, click, wheel, esc);
        else
        {
            // Esc closes the menu, unless it was leaving a text field (that puts the field back as it was instead).
            if (esc && !textFocused && !_textFocusedLastFrame) AskClose();
            if (Pad.Start && _saveBarShown && StagedCount > 0) { PressBar(1); Pad.Consume(); }
            TickMouse(pos, click, wheel);
            TickPad(now);
        }
        _textFocusedLastFrame = textFocused;
        CommitTextsLosingFocus();
        if (_pendingRow != null && _drag == null && now - _pendingAt > 0.45f) CommitPending();

        // Scrolling, eased: the settings and the tree; the right stick scrolls the settings.
        if (Pad.ScrollY != 0f && !_kbOpen && _dropRow == null) _scrollTarget -= Pad.ScrollY * 900f * dt;
        float max = Mathf.Max(0f, _contentHeight - _viewport!.rect.height);
        _scrollTarget = Mathf.Clamp(_scrollTarget, 0f, max);
        _scroll = Mathf.Lerp(_scroll, _scrollTarget, 1f - Mathf.Exp(-16f * dt));
        if (Mathf.Abs(_scroll - _scrollTarget) < 0.5f) _scroll = _scrollTarget;
        _content!.anchoredPosition = new Vector2(0, _scroll);
        float tmax = Mathf.Max(0f, _treeHeight - _treeView!.rect.height);
        _treeTarget = Mathf.Clamp(_treeTarget, 0f, tmax);
        _treeScroll = Mathf.Lerp(_treeScroll, _treeTarget, 1f - Mathf.Exp(-16f * dt));
        if (Mathf.Abs(_treeScroll - _treeTarget) < 0.5f) _treeScroll = _treeTarget;
        _treeContent!.anchoredPosition = new Vector2(0, _treeScroll);

        TickPresses(now);
        if (_rebuildPending && now >= _rebuildAt) RebuildGroup(); // rows came or went (a page said so); after a press has shown
        TickPreview(now);
        if (now >= _nextRefresh) { _nextRefresh = now + 0.5f; RefreshAll(); } // changed elsewhere meanwhile (a command, a saved file)
        Visuals(pos);
    }

    private float _nextRefresh;

    private void TickMouse(Vector2 pos, bool click, float wheel)
    {
        if (UiKit.Over(_viewport, pos) && Mathf.Abs(wheel) > 0.01f) _scrollTarget -= Mathf.Sign(wheel) * RowH * 1.5f;
        if (UiKit.Over(_treeView, pos) && Mathf.Abs(wheel) > 0.01f) _treeTarget -= Mathf.Sign(wheel) * GroupH * 2f;
        if (!click) { TickDrag(pos); return; }
        if (UiKit.Over(_closeBtn, pos)) { AskClose(); return; }
        if (_saveBarShown && _barRow?.Buttons != null)
            for (int i = 0; i < _barRow.Buttons.Count; i++)
                if (UiKit.Over(_barRow.Buttons[i].Rt, pos)) { _zone = Zone.Bar; _barRow.ButtonFocus = i; PressBar(i); return; }
        if (UiKit.Over(_kbBtn, pos)) { OpenKeyboardFor(HoveredRow(pos) ?? FocusedRow()); return; }
        if (UiKit.Over(_treeView, pos))
        {
            foreach (var t in Pickable())
            {
                if (!UiKit.Over(t.Rt, pos)) continue;
                _zone = Zone.Tree;
                if (t.Category != null) ToggleCategory(t.Mod, t.Category);
                else if (t.Group == null) ToggleMod(t.Mod);
                else { var pick = t; Guarded(() => Select(pick.Mod, pick.Group!)); }
                return;
            }
            return;
        }
        if (!UiKit.Over(_viewport, pos)) return;
        for (int i = 0; i < _rows.Count; i++)
        {
            if (!UiKit.Over(_rows[i].Rt, pos)) continue;
            if (!_rows[i].Focusable) return; // a heading
            _focus = i;
            _zone = Zone.Rows;
            if (UiKit.Over(_rows[i].PinRt, pos)) { TogglePin(_rows[i]); return; }
            if (_rows[i].CanStep && UiKit.Over(_rows[i].StepLess, pos)) { StepChange(_rows[i], -1); return; }
            if (_rows[i].CanStep && UiKit.Over(_rows[i].StepMore, pos)) { StepChange(_rows[i], 1); return; }
            ClickRow(_rows[i], pos);
            return;
        }
    }

    private void TickPad(float now)
    {
        if (Pad.LB || Pad.RB) { StepGroup(Pad.RB ? 1 : -1); _zone = Zone.Tree; Pad.Consume(); return; }
        if (_zone == Zone.Tree)
        {
            var picks = Pickable();
            if (Pad.Dy != 0 && picks.Count > 0)
            {
                int was = _treeFocus;
                _treeFocus = Math.Clamp(_treeFocus + Pad.Dy, 0, picks.Count - 1);
                var t = picks[_treeFocus];
                if (t.Group != null) Guarded(() => Select(t.Mod, t.Group));
                if (Asking) _treeFocus = was; // it asks first: the highlight stays on the page shown (Select moves it if they go)
                TreeScrollTo(_treeFocus);
            }
            else if (Pad.A && _treeFocus < picks.Count && picks[_treeFocus].Category is { } cat) ToggleCategory(picks[_treeFocus].Mod, cat);
            else if (Pad.A && _treeFocus < picks.Count && picks[_treeFocus].Group == null) ToggleMod(picks[_treeFocus].Mod);
            else if ((Pad.A || Pad.Dx > 0) && _rows.Any(r => r.Focusable)) { _zone = Zone.Rows; _focus = Focusable(Math.Clamp(_focus, 0, _rows.Count - 1), 1); ScrollTo(_focus); }
            else if (Pad.B) AskClose();
        }
        else if (_zone == Zone.Bar)
        {
            // The bar under the rows: Discard and Save (up goes back to the rows).
            if (Pad.Dy < 0 || Pad.B) _zone = Zone.Rows;
            else if (Pad.Dx != 0 && _barRow != null) _barRow.ButtonFocus = Math.Clamp(_barRow.ButtonFocus + Math.Sign(Pad.Dx), 0, 1);
            else if (Pad.A && _barRow != null) PressBar(_barRow.ButtonFocus);
        }
        else
        {
            var row = FocusedRow();
            if (Pad.B) { CommitPending(); _zone = Zone.Tree; }
            else if (Pad.Dy != 0 && _rows.Count > 0)
            {
                CommitPending();
                // Down past the last row: the Save bar.
                if (Pad.Dy > 0 && _saveBarShown && !_rows.Skip(_focus + 1).Any(r => r.Focusable)) { _zone = Zone.Bar; if (_barRow != null) _barRow.ButtonFocus = 1; }
                else
                {
                    _focus = Focusable(Math.Clamp(_focus + Pad.Dy, 0, _rows.Count - 1), Pad.Dy);
                    ScrollTo(_focus);
                }
            }
            else if (Pad.Dx != 0 && row != null)
            {
                if (!PadAdjust(row, Pad.Dx, now) && Pad.Dx < 0) { CommitPending(); _zone = Zone.Tree; }
            }
            else if (Pad.A && row != null) PadChoose(row);
            else if (Pad.Y && row is { HasPin: true }) TogglePin(row);
            else if (Pad.X && row is { Kind: ControlKind.Secret } && row.Current is string s && s.Trim().Length > 0)
            {
                SetValue(row, "");
                Say($"{UiKit.Escape(row.Title)} cleared.");
            }
        }
        if (Pad.Dx != 0 || Pad.Dy != 0 || Pad.A || Pad.B || Pad.X || Pad.Y) Pad.Consume();
    }

    /// <summary>The row the highlight can be on, from <paramref name="i"/> on in the direction it moved (headings are passed over).</summary>
    private int Focusable(int i, int dir)
    {
        if (_rows.Count == 0) return 0;
        int step = dir < 0 ? -1 : 1;
        for (int j = i; j >= 0 && j < _rows.Count; j += step) if (_rows[j].Focusable) return j;
        for (int j = i; j >= 0 && j < _rows.Count; j -= step) if (_rows[j].Focusable) return j;
        return i;
    }

    private void ScrollTo(int index)
    {
        if (_viewport == null || index < 0 || index >= _rows.Count) return;
        float top = -_rows[index].Rt.anchoredPosition.y, bottom = top + _rows[index].Height, view = _viewport.rect.height;
        // A heading just above stays in view with its row.
        if (index > 0 && _rows[index - 1].Kind is ControlKind.Section or ControlKind.Step or ControlKind.Subsection or ControlKind.ChangeHeader)
            top = -_rows[index - 1].Rt.anchoredPosition.y;
        if (top < _scrollTarget + 8f) _scrollTarget = Mathf.Max(0f, top - 8f);
        else if (bottom > _scrollTarget + view - 8f) _scrollTarget = bottom - view + 8f;
    }

    private void Visuals(Vector2 mouse)
    {
        bool pad = Pad.Active;
        var picks = Pickable();
        for (int i = 0; i < picks.Count; i++)
        {
            var t = picks[i];
            bool current = t.Mod == _mod && t.Category == null && (t.Group == _group || (t.Group == null && FoldedOpen));
            bool hover = UiKit.Over(t.Rt, mouse) && !_kbOpen && _dropRow == null;
            bool focused = pad && _zone == Zone.Tree && i == _treeFocus;
            t.Bg.enabled = current || hover || focused;
            t.Bg.color = focused ? new Color(1f, 0.86f, 0.62f, 1f) : current ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            if (t.Category != null) t.Text.color = hover || focused ? Body : Muted;
            else if (t.Group != null) t.Text.color = current ? NameColor : hover ? Body : Muted;
            else t.Text.color = t.Mod == _mod ? NameColor : Body;
        }
        if (_closeImg != null) _closeImg.color = UiKit.Over(_closeBtn, mouse) ? ButtonHover : ButtonIdle;
        if (_kbImg != null) _kbImg.color = UiKit.Over(_kbBtn, mouse) || _kbOpen ? ButtonHover : ButtonIdle;

        if (_barRow != null && _saveBarShown) ButtonVisuals(_barRow, _zone == Zone.Bar, mouse, Time.unscaledTime);
        if (_leaveRow != null && Asking) ButtonVisuals(_leaveRow, true, mouse, Time.unscaledTime);
        Row? hovered = _kbOpen || _dropRow != null || Asking ? null : HoveredRow(mouse);
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            bool focused = r.Focusable && ((pad && _zone == Zone.Rows && i == _focus) || r == _kbRow || r == _dropRow || r == _capture);
            r.Focus.enabled = r.Focusable && (focused || r == hovered);
            r.Focus.color = focused ? FocusTint : HoverTint;
            if (r.Buttons != null) ButtonVisuals(r, focused, mouse, Time.unscaledTime);
        }
        var shown = hovered ?? (_zone == Zone.Rows || _kbOpen || _dropRow != null ? (_kbRow ?? _dropRow ?? FocusedRow()) : null);
        string help = Time.unscaledTime < _noteUntil && _note.Length > 0 ? _note
            : shown != null ? HelpOf(shown)
            : _mod >= 0 && _mod < _mods.Count ? UiKit.Escape(_mods[_mod].Groups().FirstOrDefault(g => g.name == _group).intro ?? "") : "";
        if (_help != null && _help.text != help) _help.text = help;
        string legend = pad
            ? _kbOpen ? $"D-pad / stick  move  ·  {Pad.Label("A")}  type  ·  {Pad.Label("LB")}  delete  ·  hold {Pad.Label("LT")}  capitals  ·  Done  saves  ·  {Pad.Label("B")}  cancel"
            : _dropRow != null ? $"D-pad  choose  ·  {Pad.Label("A")}  pick  ·  {Pad.Label("B")}  cancel"
            : Asking ? $"left / right  choose  ·  {Pad.Label("A")}  press  ·  {Pad.Label("B")}  keep editing"
            : _zone == Zone.Bar ? $"left / right  Discard or Save  ·  {Pad.Label("A")}  press  ·  up  back to the settings"
            : _zone == Zone.Tree ? $"D-pad  mods and groups  ·  {Pad.Label("A")}  open a mod, or into its settings  ·  {Pad.Label("LB")} / {Pad.Label("RB")}  groups  ·  {Pad.Label("B")}  close"
            : FocusedRow() is { HasPin: true }
            ? $"D-pad  move  ·  left / right  change  ·  {Pad.Label("Y")}  pin it (kept as yours)  ·  {Pad.Label("A")}  choose  ·  {Pad.Label("B")}  back to the list"
            : FocusedRow() is { Kind: ControlKind.Buttons or ControlKind.Button }
            ? $"left / right  choose a button  ·  {Pad.Label("A")}  press it  ·  {Pad.Label("B")}  back to the list"
            : FocusedRow() is { Kind: ControlKind.Secret }
            ? $"{Pad.Label("A")}  type a new one  ·  {Pad.Label("X")}  clear it  ·  {Pad.Label("B")}  back to the list"
            : $"D-pad  move  ·  left / right  change  ·  {Pad.Label("A")}  choose  ·  {Pad.Label("B")}  back to the list  ·  {Pad.Label("LB")} / {Pad.Label("RB")}  groups"
            : $"Every change is saved at once  ·  click a setting to change it  ·  Esc or {SettingsMenu.KeyLabel} closes";
        if (_legend != null && _legend.text != legend) _legend.text = legend;
        if (_fade != null) _fade.alpha = Mathf.MoveTowards(_fade.alpha, _fadeTarget, Time.unscaledDeltaTime * 4f);
    }

    /// <summary>The footer's words for a row: worked out now when its page says so, and its amber tag's reason after them.</summary>
    private static string HelpOf(Row r)
    {
        string help = r.Help;
        try { if (r.Custom?.HelpLive?.Invoke() is { Length: > 0 } live) help = UiKit.Escape(live); } catch { }
        try { if ((r.Custom?.BadgeHelp ?? r.BadgeHelp)?.Invoke() is { Length: > 0 } why && r.BadgeNow is { Length: > 0 }) help += "  <color=#E0A84E>" + UiKit.Escape(why) + "</color>"; } catch { }
        return help;
    }

    /// <summary>A note in the footer for a few seconds (a number that wasn't one, a key that can't be used...).</summary>
    private void Say(string note)
    {
        _note = note;
        _noteUntil = Time.unscaledTime + 5f;
    }

    private Row? FocusedRow() => _focus >= 0 && _focus < _rows.Count ? _rows[_focus] : null;

    private Row? HoveredRow(Vector2 pos)
    {
        if (!UiKit.Over(_viewport, pos)) return null;
        foreach (var r in _rows) if (UiKit.Over(r.Rt, pos)) return r;
        return null;
    }

    // ------------------------------------------------------------------ a page's live preview (People of Nivalis's rings)

    private float _fadeTarget = 1f, _movedAt = -10f;
    /// <summary>The bar moved last (the window stays faded for a moment after, if it's see-through).</summary>
    private Row? _movedRow;
    private Row? _previewRow;
    private object? _previewValue;

    /// <summary>
    /// A row with a preview is told what it shows while it's hovered, highlighted or moving (its value), and null once it isn't;
    /// a see-through one fades the window while it moves, so the game behind shows.
    /// </summary>
    private void TickPreview(float now)
    {
        // Moving: being dragged, or moved a moment ago (saved or not): it stays the one shown, and a see-through one
        // keeps the window faded, until a moment after the last move (review of d6138be).
        bool recent = _movedRow != null && _rows.Contains(_movedRow) && now - _movedAt < 0.9f;
        var see = _drag ?? (recent ? _movedRow : null);
        _fadeTarget = see?.Custom?.SeeThrough == true ? 0.4f : 1f;
        var hovered = HoveredRow(Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero);
        Row? row = see ?? hovered ?? (Pad.Active && _zone == Zone.Rows ? FocusedRow() : null);
        if (row?.Custom?.Preview == null) row = null;
        object? value = row == null ? null : row.Pending is double p ? p : row.Current;
        if (row == _previewRow && Equals(value, _previewValue)) return;
        if (_previewRow != null && _previewRow != row) Preview(_previewRow, null);
        _previewRow = row;
        _previewValue = value;
        if (row != null) Preview(row, value);
        if (row != null && row.Custom!.Rebuild == false) RefreshAll(); // other rows that follow it (the other rings)
    }

    private void StopPreview()
    {
        if (_previewRow != null) Preview(_previewRow, null);
        _previewRow = null;
        _previewValue = null;
        _fadeTarget = 1f;
    }

    private void Preview(Row row, object? value)
    {
        try { row.Custom?.Preview?.Invoke(value); } catch (Exception e) { Log.Debug("preview: " + e.Message); }
    }

    // ------------------------------------------------------------------ the on-screen keyboard for a text field

    private bool _kbOpen;
    private Row? _kbRow;
    private string _kbText = "";

    private void BuildKeyboardOverlay()
    {
        _kbOverlay = UiKit.Rect(_window!, "KeyboardOverlay", new Vector2(0, 0), new Vector2(1, 0), new Vector2(TreeW + 10, 10), new Vector2(-12, 352));
        var bg = _kbOverlay.gameObject.AddComponent<Image>();
        bg.sprite = Sprites.Bubble;
        bg.type = Image.Type.Sliced;
        _kbTitle = UiKit.Text(_kbOverlay, "Editing", 15, Muted, TextAlignmentOptions.TopLeft, _bodyFont);
        UiKit.Place(_kbTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -34), new Vector2(-22, -12));
        _kbTitle.fontStyle = FontStyles.UpperCase;
        _kbTitle.characterSpacing = 1.5f;
        var typed = UiKit.Rect(_kbOverlay, "Typed", new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -84), new Vector2(-18, -38));
        var tbg = typed.gameObject.AddComponent<Image>();
        tbg.sprite = Sprites.Field;
        tbg.type = Image.Type.Sliced;
        _kbTyped = UiKit.Text(typed, "Text", 20, Color.white, TextAlignmentOptions.MidlineLeft, _bodyFont);
        UiKit.Place(_kbTyped.rectTransform, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-14, 0));
        _kbTyped.enableWordWrapping = false;
        _kbTyped.overflowMode = TextOverflowModes.Truncate;
        _kbGridRt = UiKit.Rect(_kbOverlay, "Grid", Vector2.zero, Vector2.one, new Vector2(16, 14), new Vector2(-16, -92));
        _keys.Build(_kbGridRt, _bodyFont);
        _keys.SendLabel = "Done";
        _kbOverlay.gameObject.SetActive(false);
    }

    private void OpenKeyboardFor(Row? row)
    {
        if (row == null || !row.Typed)
        {
            Say("The keyboard types into a text setting (a name, an address, a key): choose one first.");
            return;
        }
        CommitPending();
        CommitTexts();
        try { EventSystem.current?.SetSelectedGameObject(null); } catch { }
        _kbRow = row;
        _kbText = row.Kind == ControlKind.Secret ? "" : row.CurrentText();
        _kbOpen = true;
        _keys.Reset();
        if (_kbTitle != null) _kbTitle.text = UiKit.Escape(row.Title) + (row.Kind == ControlKind.Secret ? "  ·  typed hidden, never shown" : "");
        _kbOverlay!.gameObject.SetActive(true);
        _kbOverlay.SetAsLastSibling();
        ShowTyped();
    }

    private void TickKeyboard(bool esc)
    {
        if (_kbRow == null) { CloseKeyboard(false); return; }
        if (Pad.B || esc) { Pad.Consume(); CloseKeyboard(save: false); return; }
        string before = _kbText;
        var act = _keys.Tick(ref _kbText, 2000);
        if (_kbText != before) ShowTyped();
        if (act == KeyAction.Send) CloseKeyboard(save: true);
        else if (act == KeyAction.Close) CloseKeyboard(save: false);
    }

    private void ShowTyped()
    {
        if (_kbTyped == null || _kbRow == null) return;
        string shown = _kbRow.Kind == ControlKind.Secret ? new string('*', _kbText.Length) : UiKit.Escape(_kbText);
        _kbTyped.text = shown + "<color=#C4A062>|</color>";
    }

    private void CloseKeyboard(bool save)
    {
        if (!_kbOpen) return;
        var row = _kbRow;
        _kbOpen = false;
        _kbRow = null;
        _kbOverlay?.gameObject.SetActive(false);
        if (save && row != null) CommitText(row, _kbText);
        _kbText = "";
    }

    // ------------------------------------------------------------------ a dropdown's list

    private RectTransform? _dropList;
    private readonly List<(RectTransform rt, Image bg, TextMeshProUGUI text)> _dropItems = new();
    private Row? _dropRow;
    private List<string> _dropChoices = new();
    private int _dropIndex, _dropTop;
    private const int DropShown = 9;
    private const float DropItemH = 34f;

    private void BuildDropdown()
    {
        _dropOverlay = UiKit.Rect(_window!, "Dropdown", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _dropList = UiKit.Rect(_dropOverlay, "List", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _dropList.pivot = new Vector2(0, 1);
        var bg = _dropList.gameObject.AddComponent<Image>();
        bg.sprite = Sprites.Field;
        bg.type = Image.Type.Sliced;
        for (int i = 0; i < DropShown; i++)
        {
            var rt = UiKit.Rect(_dropList, "Item", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-8, DropItemH - 2);
            rt.anchoredPosition = new Vector2(0, -4 - i * DropItemH);
            var ibg = rt.gameObject.AddComponent<Image>();
            ibg.sprite = Sprites.Field;
            ibg.type = Image.Type.Sliced;
            ibg.color = new Color(1f, 0.86f, 0.62f, 1f);
            var text = UiKit.Text(rt, "Text", 17, Body, TextAlignmentOptions.MidlineLeft, _bodyFont);
            UiKit.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-8, 0));
            text.enableWordWrapping = false;
            _dropItems.Add((rt, ibg, text));
        }
        _dropOverlay.gameObject.SetActive(false);
    }

    private void OpenDropdown(Row row)
    {
        if (row.Box == null || row.Control.Choices == null) return;
        CommitPending();
        _dropRow = row;
        string current = row.CurrentText();
        _dropChoices = row.Control.ChoicesWith(current).ToList();
        _dropIndex = Math.Max(0, _dropChoices.FindIndex(c => string.Equals(c, current, StringComparison.OrdinalIgnoreCase)));
        _dropTop = Math.Clamp(_dropIndex - DropShown / 2, 0, Math.Max(0, _dropChoices.Count - DropShown));
        int shown = Math.Min(DropShown, _dropChoices.Count);
        float h = shown * DropItemH + 8f, w = Mathf.Max(row.Box.rect.width, 220f);
        _dropList!.sizeDelta = new Vector2(w, h);
        // Under the box, or over it when there's no room below (in the settings area's middle if it can't be placed).
        var screen = RectTransformUtility.WorldToScreenPoint(null, row.Box.position);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_window!, screen, null, out var local))
            local = new Vector2(TreeW / 2f + 40f, h / 2f);
        float left = local.x - row.Box.rect.width * row.Box.pivot.x;
        float top = local.y - row.Box.rect.height * row.Box.pivot.y - 2f;
        if (top - h < -H / 2f + 12f) top = local.y + row.Box.rect.height * (1f - row.Box.pivot.y) + h + 2f;
        _dropList.anchoredPosition = new Vector2(Mathf.Min(left, W / 2f - w - 12f), top);
        _dropOverlay!.gameObject.SetActive(true);
        _dropOverlay.SetAsLastSibling();
        ShowDropItems();
        // 0.4.2 (checklist 38: a list didn't take the pad): said in the log, so the next test shows which step stops.
        _dropMovedByPad = false;
        if (Pad.Active) Log.Info($"menu: list open on {row.Title} ({_dropChoices.Count} choices, the pad: up/down move, A picks, B closes)");
    }

    private bool _dropMovedByPad;

    private void ShowDropItems()
    {
        for (int i = 0; i < _dropItems.Count; i++)
        {
            int c = _dropTop + i;
            var (rt, bg, text) = _dropItems[i];
            bool has = c < _dropChoices.Count;
            if (rt.gameObject.activeSelf != has) rt.gameObject.SetActive(has);
            if (!has) continue;
            text.text = UiKit.Escape(ChoiceLabel(_dropChoices[c]));
            bg.enabled = c == _dropIndex;
            text.color = c == _dropIndex ? Color.white : Body;
        }
    }

    private void TickDropdown(Vector2 pos, bool click, float wheel, bool esc)
    {
        if (_dropRow == null) return;
        if (esc || Pad.B) { Pad.Consume(); CloseDropdown(); return; }
        if (Pad.Dy != 0)
        {
            _dropIndex = Math.Clamp(_dropIndex + Pad.Dy, 0, _dropChoices.Count - 1);
            if (!_dropMovedByPad) { _dropMovedByPad = true; Log.Info($"menu: the list moved with the pad (now on {ChoiceLabel(_dropChoices[_dropIndex])})"); }
        }
        if (UiKit.Over(_dropList, pos) && Mathf.Abs(wheel) > 0.01f)
            _dropTop = Math.Clamp(_dropTop - Math.Sign(wheel), 0, Math.Max(0, _dropChoices.Count - DropShown));
        for (int i = 0; i < _dropItems.Count; i++)
            if (_dropTop + i < _dropChoices.Count && UiKit.Over(_dropItems[i].rt, pos) && Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 0.5f)
                _dropIndex = _dropTop + i;
        if (_dropIndex < _dropTop) _dropTop = _dropIndex;
        if (_dropIndex >= _dropTop + DropShown) _dropTop = _dropIndex - DropShown + 1;
        bool pick = Pad.A;
        if (click)
        {
            int hit = -1;
            for (int i = 0; i < _dropItems.Count; i++)
                if (_dropTop + i < _dropChoices.Count && UiKit.Over(_dropItems[i].rt, pos)) hit = _dropTop + i;
            if (hit < 0) { CloseDropdown(); return; }
            _dropIndex = hit;
            pick = true;
        }
        if (Pad.Dy != 0 || Pad.A) Pad.Consume();
        ShowDropItems();
        if (!pick) return;
        var row = _dropRow;
        string choice = _dropChoices[_dropIndex];
        if (!click) Log.Info($"menu: {row.Title}: {ChoiceLabel(choice)} picked with the pad");
        CloseDropdown();
        SetValue(row, choice);
    }

    private void CloseDropdown()
    {
        _dropRow = null;
        _dropOverlay?.gameObject.SetActive(false);
    }

    /// <summary>How a choice is shown: an empty one is the default.</summary>
    internal static string ChoiceLabel(string choice) => choice.Length == 0 ? "(the default)" : choice;
}
