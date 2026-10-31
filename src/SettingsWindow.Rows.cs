using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NivalisSwitchboard;

/// <summary>The menu's rows: one per setting (its control on the right, its value read from the setting), and a page's own rows.</summary>
internal sealed partial class SettingsWindow
{
    private static readonly Color TrackOff = new Color32(58, 53, 48, 255);
    private static readonly Color TrackOn = new Color32(96, 150, 82, 255);
    private static readonly Color KnobOff = new Color32(138, 133, 125, 255);
    private static readonly Color KnobOn = new Color32(232, 242, 228, 255);
    private static readonly Color Gold = new Color32(196, 160, 98, 255);

    internal sealed class Row
    {
        public FoundMod Mod = null!;
        public ConfigEntryBase? Entry;
        public PageRow? Custom;
        public SettingControl Control = new(ControlKind.Text);
        public ControlKind Kind => Control.Kind;
        public string Title = "", Help = "";
        public Func<string, string?>? Validate;
        /// <summary>A setting's amber tag and its sentence (from its page's hint; a custom row has its own).</summary>
        public Func<string?>? Badge, BadgeHelp;
        public float Height = RowH;
        public TextMeshProUGUI? TitleText, HintText;
        public string? LastHint;
        public RectTransform? PinRt, MeterUsed, MeterProjected, MeterTick;
        /// <summary>0.5.4: a change row's arrows either side of its "now" value (stepped with them, or left/right).</summary>
        public RectTransform? StepLess, StepMore;
        public bool CanStep => Kind == ControlKind.Change && Custom?.Step != null && Custom.Set != null;
        public Image? PinImg;
        public TextMeshProUGUI? PinText;
        public string? LastBadge;
        /// <summary>Headings, tiles and captions aren't settings: the highlight passes over them (and over a hidden row).</summary>
        public bool Focusable => Shown && Kind is not (ControlKind.Section or ControlKind.Step or ControlKind.Subsection or ControlKind.Tiles or ControlKind.ChangeHeader);
        /// <summary>Inside a step (indented past its rail); shown (a page can hide a row).</summary>
        public bool InStep, Shown = true;
        public Image? Rail, ChipImg, NumFill, NumGlow;
        public TextMeshProUGUI? FromText, ToText, ChipText, NumText;
        public RectTransform? ChipRt;
        public string? LastChip, LastTone, LastState;
        public List<TileUi>? Tiles;
        public int TileCountBuilt;
        public List<ButtonUi>? Buttons;
        /// <summary>Which of a buttons row's buttons the pad's highlight is on.</summary>
        public int ButtonFocus;
        public bool HasPin => Custom?.Pin != null && Custom.Pinned != null;
        public bool IsPinned { get { try { return Custom?.Pinned?.Invoke() == true; } catch { return false; } } }
        public string? BadgeNow { get { try { return (Custom?.Badge ?? Badge)?.Invoke(); } catch { return null; } } }
        public RectTransform Rt = null!;
        public Image Focus = null!;
        public RectTransform? Area, Track, Fill, Knob, Box, Clear;
        public Image? TrackImg, KnobImg;
        public TextMeshProUGUI? Value;
        public TMP_InputField? Input;
        /// <summary>What was in its field when it was last staged as typed (0.4.2).</summary>
        public string? TypedSeen;
        public bool WasFocused;
        /// <summary>When its button (or pin) was last pressed: a short press animation plays from then.</summary>
        public float PressedAt = -10f;
        /// <summary>A bar's value moved but not saved yet (saved once it stops: a drag let go, a pause between steps).</summary>
        public double? Pending;

        /// <summary>0.4.0: a change waiting for Save (shown in place of the saved value).</summary>
        public object? Staged;
        public bool HasStaged;

        /// <summary>The value as saved: the setting's, or the one the page's row reads.</summary>
        public object? SavedValue
        {
            get
            {
                if (Entry != null) return Entry.BoxedValue;
                try { return Custom?.Get?.Invoke(); } catch { return null; }
            }
        }

        /// <summary>The value shown: a change waiting for Save, else the saved one.</summary>
        public object? Current
        {
            get
            {
                if (HasStaged) return Staged;
                if (Entry != null) return Entry.BoxedValue;
                try { return Custom?.Get?.Invoke(); } catch { return null; }
            }
        }

        public Type Type => Entry?.SettingType ?? Custom?.ValueType ?? typeof(string);
        public bool Editable => Entry != null || Custom?.Set != null;
        /// <summary>Typed in (with the keys or the on-screen keyboard).</summary>
        public bool Typed => Kind is ControlKind.Text or ControlKind.Number or ControlKind.Secret;
        /// <summary>The value as text (a secret's is never asked for: only whether it's set).</summary>
        public string CurrentText() => !Editable || Kind == ControlKind.Secret ? "" : Catalog.Show(Current);
    }

    private readonly List<Row> _rows = new();
    private float _contentHeight, _nextY;
    private Row? _drag, _pendingRow, _capture;
    private float _pendingAt;
    private bool _rebuildPending;

    // ------------------------------------------------------------------ building a group's rows

    private void ClearRows()
    {
        foreach (var r in _rows) if (r.Rt != null) UnityEngine.Object.Destroy(r.Rt.gameObject);
        _rows.Clear();
        _drag = _pendingRow = _capture = null;
        _contentHeight = _nextY = 0;
        _stepSeen = false;
        if (_content != null) _content.sizeDelta = Vector2.zero;
    }

    private void BuildRows(FoundMod mod, string group)
    {
        ClearRows();
        var problems = new List<string>();
        var custom = Page.Rows(mod.Page, group, problems);
        foreach (var p in problems) Log.Warn($"{mod.Name}'s settings page: {p}");
        var placed = new HashSet<PageRow>();
        var entries = mod.Entries.Where(e => e.Definition.Section == group)
            .Select((e, i) => (e, i, meta: Discovery.Meta(e))).OrderByDescending(x => x.meta.order).ThenBy(x => x.i).ToList();
        foreach (var (e, _, meta) in entries)
        {
            string key = e.Definition.Key;
            foreach (var c in custom.Where(c => c.Before == key && placed.Add(c))) AddCustom(mod, c);
            mod.Page.Hints.TryGetValue(group + "/" + key, out var hint);
            var control = hint?.Control ?? Catalog.Generic(key, e.SettingType, meta.min, meta.max, meta.values);
            if (control.Kind != ControlKind.Hidden && Page.Listed(mod.Page, meta.hidden, hint))
            {
                string desc = e.Description?.Description ?? "";
                string help = UiKit.Escape(desc);
                if (control.Kind != ControlKind.Secret)
                    help += $"  <color=#8A857D>Default: {UiKit.Escape(control.Kind == ControlKind.Choice ? ChoiceLabel(Catalog.Show(e.DefaultValue)) : Catalog.Show(e.DefaultValue))}.</color>";
                AddRow(mod, e, null, control, hint?.Title ?? meta.name ?? Catalog.Title(key), key + "  ·  " + (hint?.Hint ?? Catalog.Hint(desc)), help, hint?.Validate);
                _rows[^1].Badge = hint?.Badge;
                _rows[^1].BadgeHelp = hint?.BadgeHelp;
            }
            foreach (var c in custom.Where(c => c.After == key && placed.Add(c))) AddCustom(mod, c);
        }
        foreach (var c in custom.Where(c => placed.Add(c))) AddCustom(mod, c);
        TrimLastRail();
        _contentHeight = _nextY + 18f;
        _content!.sizeDelta = new Vector2(0, _contentHeight);
        ShowSaveBar(mod.Page.SaveButton);
        RefreshAll(force: true);
        RefreshSaveBar();
    }

    private void AddCustom(FoundMod mod, PageRow c) =>
        AddRow(mod, null, c, c.Control, c.Title, c.Hint, UiKit.Escape(c.Help), null);

    /// <summary>The group's rows built again (rows came or went), the highlight and the scroll kept where they were.</summary>
    private void RebuildGroup()
    {
        _rebuildPending = false;
        if (_mod < 0 || _mod >= _mods.Count) return;
        int focus = _focus;
        float scroll = _scrollTarget;
        StopPreview();
        var staged = TakeStaged(); // changes waiting for Save stay waiting
        BuildRows(_mods[_mod], _group);
        PutStaged(staged);
        _focus = Math.Clamp(focus, 0, Math.Max(0, _rows.Count - 1));
        _scroll = _scrollTarget = scroll;
    }

    private void AddRow(FoundMod mod, ConfigEntryBase? entry, PageRow? custom, SettingControl control, string title, string hint, string help, Func<string, string?>? validate)
    {
        var row = new Row { Mod = mod, Entry = entry, Custom = custom, Control = control, Title = title, Help = help, Validate = validate };
        // A tiles row is sized and built from one reading of its tiles (review: two readings could disagree).
        if (control.Kind == ControlKind.Tiles) row.TileCountBuilt = TileCount(custom);
        row.Height = HeightOf(control, row.TileCountBuilt);
        if (control.Kind == ControlKind.Step) _stepSeen = true;
        else row.InStep = _stepSeen;
        var rt = UiKit.Rect(_content!, "Row", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0, row.Height - 4);
        rt.anchoredPosition = new Vector2(0, -6 - _nextY);
        _nextY += row.Height;
        row.Rt = rt;
        var focus = rt.gameObject.AddComponent<Image>();
        focus.sprite = Sprites.Field;
        focus.type = Image.Type.Sliced;
        focus.color = FocusTint;
        focus.enabled = false;
        row.Focus = focus;
        if (_stepSeen) BuildRail(row);

        if (control.Kind == ControlKind.Section)
        {
            // A heading: the part's name in gold capitals, a line under it; nothing to change.
            var st = UiKit.Text(rt, "Section", 14, Gold, TextAlignmentOptions.BottomLeft, _titleFont);
            UiKit.Place(st.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 8), new Vector2(-16, -4));
            st.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            st.characterSpacing = 3f;
            st.text = UiKit.Escape(title);
            var line = UiKit.Rect(rt, "Rule", new Vector2(0, 0), new Vector2(1, 0), new Vector2(16, 2), new Vector2(-16, 3));
            var li = line.gameObject.AddComponent<Image>();
            li.sprite = Sprites.Cell;
            li.color = new Color(GoldDim.r, GoldDim.g, GoldDim.b, 0.45f);
            li.raycastTarget = false;
            row.TitleText = st;
            _rows.Add(row);
            return;
        }
        if (BuildSpecial(row)) { _rows.Add(row); return; }
        var t = UiKit.Text(rt, "Title", 19, Body, TextAlignmentOptions.BottomLeft, _bodyFont);
        UiKit.Place(t.rectTransform, new Vector2(0, 0.5f), new Vector2(0.55f, 1), new Vector2(LabelLeft(row), 0), new Vector2(0, -6));
        t.fontStyle = FontStyles.Bold;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.text = UiKit.Escape(title);
        row.TitleText = t;
        var h = UiKit.Text(rt, "Hint", 14, Muted, TextAlignmentOptions.TopLeft, _bodyFont);
        UiKit.Place(h.rectTransform, new Vector2(0, 0), new Vector2(0.55f, 0.5f), new Vector2(LabelLeft(row), 4), new Vector2(0, -2));
        h.enableWordWrapping = false;
        h.overflowMode = TextOverflowModes.Ellipsis;
        h.text = UiKit.Escape(hint);
        row.HintText = h;

        // Every control ends on the control edge; a pin sits in the end column after it (as a bar's value does).
        var area = ControlArea(row);
        row.Area = area;
        if (custom?.Pin != null && custom.Pinned != null) BuildPin(row, rt);
        switch (control.Kind)
        {
            case ControlKind.Switch: BuildSwitch(row, area); break;
            case ControlKind.Slider: BuildSlider(row, area); break;
            case ControlKind.Choice:
            case ControlKind.Key: BuildBox(row, area); break;
            case ControlKind.Number: row.Input = BuildField(area, row.Type, password: false); break;
            case ControlKind.Text: row.Input = BuildField(area, row.Type, password: false); break;
            case ControlKind.Secret: BuildSecret(row, area); break;
            case ControlKind.Meter: BuildMeter(row, area); break;
            default:
                row.Value = UiKit.Text(area, "Note", 15, Muted, TextAlignmentOptions.MidlineRight, _bodyFont);
                UiKit.Place(row.Value.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                break;
        }
        _rows.Add(row);
    }

    /// <summary>A meter: a thin bar, gold to what's used, lighter on to where it's going, a white tick where it should be by now; its words on the right.</summary>
    private void BuildMeter(Row row, RectTransform area)
    {
        var bar = UiKit.Rect(area, "Meter", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -7), new Vector2(0, 7));
        var bg = bar.gameObject.AddComponent<Image>();
        bg.sprite = Sprites.Field;
        bg.type = Image.Type.Sliced;
        var inner = UiKit.Rect(bar, "Inside", Vector2.zero, Vector2.one, new Vector2(2, 2), new Vector2(-2, -2));
        RectTransform Part(string name, Color c)
        {
            var p = UiKit.Rect(inner, name, Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero);
            var img = p.gameObject.AddComponent<Image>();
            img.sprite = Sprites.Cell;
            img.type = Image.Type.Sliced;
            img.color = c;
            img.raycastTarget = false;
            return p;
        }
        row.MeterProjected = Part("Projected", new Color(Gold.r, Gold.g, Gold.b, 0.3f));
        row.MeterUsed = Part("Used", Gold);
        var tick = UiKit.Rect(bar, "Pace", new Vector2(0, 0), new Vector2(0, 1), new Vector2(-1, -3), new Vector2(1, 3));
        var ti = tick.gameObject.AddComponent<Image>();
        ti.sprite = Sprites.Cell;
        ti.color = Color.white;
        ti.raycastTarget = false;
        row.MeterTick = tick;
        row.Value = UiKit.Text(area, "Value", 15, Body, TextAlignmentOptions.MidlineLeft, _bodyFont);
        UiKit.Place(row.Value.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(EndGap, 0), new Vector2(EndGap + EndCol, 0));
        row.Value.enableWordWrapping = false;
        row.Value.overflowMode = TextOverflowModes.Ellipsis;
    }

    /// <summary>The pin on a row's right end: "pin" or (gold) "pinned".</summary>
    private void BuildPin(Row row, RectTransform rt)
    {
        var pin = UiKit.Rect(rt, "Pin", new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero);
        pin.pivot = new Vector2(1, 0.5f);
        pin.sizeDelta = new Vector2(EndCol, 26);
        pin.anchoredPosition = new Vector2(-RightPad, 0);
        row.PinImg = pin.gameObject.AddComponent<Image>();
        row.PinImg.sprite = Sprites.Knob;
        row.PinImg.type = Image.Type.Sliced;
        row.PinText = UiKit.Text(pin, "Label", 13, Muted, TextAlignmentOptions.Center, _bodyFont);
        UiKit.Place(row.PinText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        row.PinText.fontStyle = FontStyles.Bold;
        row.PinRt = pin;
    }

    private void TogglePin(Row row)
    {
        if (!row.HasPin) return;
        row.PressedAt = Time.unscaledTime;
        try { row.Custom!.Pin!(!row.IsPinned); } catch (Exception e) { Say("That couldn't be pinned: " + UiKit.Escape(e.Message)); }
        RefreshAll();
    }

    private void BuildSwitch(Row row, RectTransform area)
    {
        var track = UiKit.Rect(area, "Track", new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero);
        track.pivot = new Vector2(1, 0.5f);
        track.sizeDelta = new Vector2(56, 30);
        row.Track = track;
        row.TrackImg = track.gameObject.AddComponent<Image>();
        row.TrackImg.sprite = Sprites.Knob;
        row.TrackImg.type = Image.Type.Sliced;
        var knob = UiKit.Rect(track, "Knob", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
        knob.sizeDelta = new Vector2(22, 22);
        row.Knob = knob;
        row.KnobImg = knob.gameObject.AddComponent<Image>();
        row.KnobImg.sprite = Sprites.Knob;
        row.KnobImg.raycastTarget = false;
    }

    /// <summary>A bar: a long rectangle filled from its left edge up to the value (the author's design: where you click, it fills to).</summary>
    private void BuildSlider(Row row, RectTransform area)
    {
        var track = UiKit.Rect(area, "Bar", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 2), new Vector2(0, -2));
        row.Track = track;
        row.TrackImg = track.gameObject.AddComponent<Image>();
        row.TrackImg.sprite = Sprites.Field;
        row.TrackImg.type = Image.Type.Sliced;
        var inner = UiKit.Rect(track, "Inside", Vector2.zero, Vector2.one, new Vector2(3, 3), new Vector2(-3, -3));
        var fill = UiKit.Rect(inner, "Fill", Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.sprite = Sprites.Cell;
        fillImg.type = Image.Type.Sliced;
        fillImg.color = new Color(Gold.r, Gold.g, Gold.b, 0.8f);
        fillImg.raycastTarget = false;
        row.Fill = fill;
        row.Value = UiKit.Text(area, "Value", 17, Body, TextAlignmentOptions.MidlineLeft, _bodyFont);
        UiKit.Place(row.Value.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(EndGap, 0), new Vector2(EndGap + EndCol, 0));
        row.Value.fontStyle = FontStyles.Bold;
        row.Value.enableWordWrapping = false;
    }

    private void BuildBox(Row row, RectTransform area)
    {
        var box = UiKit.Rect(area, "Box", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        row.Box = box;
        var bg = box.gameObject.AddComponent<Image>();
        bg.sprite = Sprites.Field;
        bg.type = Image.Type.Sliced;
        row.Value = UiKit.Text(box, "Value", 17, Body, TextAlignmentOptions.MidlineLeft, _bodyFont);
        UiKit.Place(row.Value.rectTransform, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-14, 0));
        row.Value.enableWordWrapping = false;
        row.Value.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void BuildSecret(Row row, RectTransform area)
    {
        row.Value = UiKit.Text(area, "Set", 15, Muted, TextAlignmentOptions.MidlineLeft, _bodyFont);
        UiKit.Place(row.Value.rectTransform, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(70, 0));
        var fieldArea = UiKit.Rect(area, "FieldArea", Vector2.zero, Vector2.one, new Vector2(76, 0), new Vector2(-78, 0));
        row.Input = BuildField(fieldArea, row.Type, password: true);
        var clear = UiKit.Rect(area, "Clear", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-70, 0), Vector2.zero);
        var cbg = clear.gameObject.AddComponent<Image>();
        cbg.sprite = Sprites.Pill;
        cbg.type = Image.Type.Sliced;
        var ct = UiKit.Text(clear, "Label", 15, NameColor, TextAlignmentOptions.Center, _bodyFont);
        UiKit.Place(ct.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ct.text = "Clear";
        row.Clear = clear;
    }

    /// <summary>A text field in the look of People of Nivalis's chat box (typed in with the keys; the on-screen keyboard fills it too).</summary>
    private TMP_InputField BuildField(RectTransform parent, Type type, bool password)
    {
        var go = new GameObject("Field");
        go.SetActive(false);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        UiKit.Place(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var bg = go.AddComponent<Image>();
        bg.sprite = Sprites.Field;
        bg.type = Image.Type.Sliced;
        var area = UiKit.Rect(rt, "Text Area", Vector2.zero, Vector2.one, new Vector2(12, 4), new Vector2(-12, -4));
        area.gameObject.AddComponent<RectMask2D>();
        var text = UiKit.Text(area, "Text", 17, Body, TextAlignmentOptions.MidlineLeft, _bodyFont);
        UiKit.Place(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        text.enableWordWrapping = false;
        text.richText = false;
        var ph = UiKit.Text(area, "Placeholder", 16, new Color32(110, 105, 98, 255), TextAlignmentOptions.MidlineLeft, _bodyFont);
        UiKit.Place(ph.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ph.fontStyle = FontStyles.Italic;
        ph.enableWordWrapping = false;
        ph.text = password ? "type a new one (hidden)" : "empty";
        var input = go.AddComponent<TMP_InputField>();
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = ph;
        if (_bodyFont != null) input.fontAsset = _bodyFont;
        input.pointSize = 17;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 2000;
        input.richText = false;
        input.onFocusSelectAll = false;
        input.resetOnDeActivation = false;
        input.caretWidth = 2;
        input.customCaretColor = true;
        input.caretColor = NameColor;
        input.selectionColor = new Color(NameColor.r, NameColor.g, NameColor.b, 0.3f);
        input.targetGraphic = bg;
        // The d-pad drives the menu's own highlight: it never moves the game's UI selection off this field.
        var nav = input.navigation;
        nav.mode = Navigation.Mode.None;
        input.navigation = nav;
        if (password)
        {
            input.contentType = TMP_InputField.ContentType.Password;
            input.asteriskChar = '*';
        }
        else if (type == typeof(int)) input.contentType = TMP_InputField.ContentType.IntegerNumber;
        try { go.AddComponent<Nivalis.InputFieldPreventExternalInput>(); } catch { } // the game doesn't act on what's typed
        go.SetActive(true);
        return input;
    }

    // ------------------------------------------------------------------ the press animation

    /// <summary>How long a press shows (and how long rows wait to be rebuilt after one).</summary>
    private const float PressTime = 0.28f;
    private float _rebuildAt;

    /// <summary>
    /// A pressed button dips and lights up gold for a moment, then springs back, and a pressed pin pops; so a press is seen
    /// even when what it does is quiet (the author: "the buttons to animate in some way so the user knows when they
    /// clicked a button").
    /// </summary>
    private void TickPresses(float now)
    {
        foreach (var r in _rows)
        {
            float t = (now - r.PressedAt) / PressTime;
            if (t < 0f || t > 1.5f) continue;
            float k = t >= 1f ? 0f : Mathf.Sin(t * Mathf.PI);           // 0 -> 1 -> 0
            if (r.PinRt != null && r.HasPin)
            {
                float pop = 1f + 0.12f * k;
                r.PinRt.localScale = new Vector3(pop, pop, 1f);
            }
        }
    }

    // ------------------------------------------------------------------ showing values

    private void RefreshAll(bool force = false)
    {
        RefreshVisibility();
        foreach (var r in _rows)
            if (force || (r != _pendingRow && r != _drag && r != _capture && !(r.Input != null && r.Input.isFocused)))
                RefreshRow(r);
    }

    private double SliderValue(Row row)
    {
        if (row.Pending is double p) return p;
        try { return Convert.ToDouble(row.Current, CultureInfo.InvariantCulture); } catch { return row.Control.Min; }
    }

    private void RefreshRow(Row row)
    {
        // The amber tag after its name, and the pin's look.
        string? badge = row.HasStaged ? "unsaved" : row.BadgeNow;
        if (row.TitleText != null && row.Kind != ControlKind.Section && badge != row.LastBadge)
        {
            row.LastBadge = badge;
            row.TitleText.text = UiKit.Escape(row.Title) + (string.IsNullOrEmpty(badge) ? "" : $"  <color=#E0A84E><size=72%>{UiKit.Escape(badge.ToUpperInvariant())}</size></color>");
        }
        // A hint the page works out each time (a recommended value that follows a dial).
        if (row.HintText != null && row.Custom?.HintLive != null)
        {
            string? live = null;
            try { live = row.Custom.HintLive(); } catch { }
            if (live != null && live != row.LastHint) { row.LastHint = live; row.HintText.text = UiKit.Escape(live); }
        }
        if (row.PinImg != null && row.PinText != null)
        {
            bool pinned = row.IsPinned;
            row.PinImg.color = pinned ? Gold : new Color32(42, 38, 34, 255);
            row.PinText.color = pinned ? new Color32(26, 22, 18, 255) : Muted;
            row.PinText.text = pinned ? "pinned" : "pin";
        }
        switch (row.Kind)
        {
            case ControlKind.Meter:
            {
                double[]? f = null;
                try { f = row.Custom?.Meter?.Invoke(); } catch { }
                float used = f is { Length: > 0 } ? Mathf.Clamp01((float)f[0]) : 0f;
                float pace = f is { Length: > 1 } ? Mathf.Clamp01((float)f[1]) : -1f;
                float proj = f is { Length: > 2 } ? Mathf.Clamp01((float)f[2]) : used;
                if (row.MeterUsed != null) { row.MeterUsed.anchorMax = new Vector2(used, 1f); row.MeterUsed.gameObject.SetActive(used > 0.001f); }
                if (row.MeterProjected != null) { row.MeterProjected.anchorMax = new Vector2(Mathf.Max(used, proj), 1f); row.MeterProjected.gameObject.SetActive(proj > 0.001f); }
                if (row.MeterTick != null) { row.MeterTick.gameObject.SetActive(pace >= 0f); row.MeterTick.anchorMin = new Vector2(Mathf.Max(0, pace), 0); row.MeterTick.anchorMax = new Vector2(Mathf.Max(0, pace), 1); }
                if (row.Value != null) row.Value.text = UiKit.Escape(row.Current as string ?? "");
                break;
            }
            case ControlKind.Switch:
            {
                bool on = row.Current is true;
                if (row.TrackImg != null) row.TrackImg.color = on ? TrackOn : TrackOff;
                if (row.KnobImg != null) row.KnobImg.color = on ? KnobOn : KnobOff;
                if (row.Knob != null) row.Knob.anchoredPosition = new Vector2(on ? 41 : 15, 0);
                if (row.Value != null) row.Value.text = on ? "On" : "Off";
                break;
            }
            case ControlKind.Slider:
            {
                var c = row.Control;
                // A page's bar can say it's off (People of Nivalis's voices ring with no hearing ring): no number shows then.
                var cur = row.Pending is double ? null : row.Current;
                bool off = cur is string s && s == "off";
                double snapped = c.Snap(off ? c.Min : SliderValue(row));
                float t = c.Max > c.Min ? (float)((snapped - c.Min) / (c.Max - c.Min)) : 0f;
                float fill = off ? 0f : Mathf.Clamp01(t);
                if (row.Fill != null)
                {
                    row.Fill.anchorMax = new Vector2(fill, 1f);
                    if (row.Fill.gameObject.activeSelf != fill > 0.001f) row.Fill.gameObject.SetActive(fill > 0.001f);
                }
                if (row.Value != null) row.Value.text = off ? "off" : c.Format(snapped);
                break;
            }
            case ControlKind.Choice:
                if (row.Value != null) row.Value.text = "‹   " + UiKit.Escape(ChoiceLabel(row.CurrentText())) + "   ›";
                break;
            case ControlKind.Key:
                if (row.Value != null) row.Value.text = row == _capture ? "<i>Press a key…  (Esc: keep it)</i>" : UiKit.Escape(row.CurrentText());
                break;
            case ControlKind.Text:
            case ControlKind.Number:
                if (row.Input != null && !row.Input.isFocused && row.Input.text != row.CurrentText()) row.Input.text = row.CurrentText();
                break;
            case ControlKind.Secret:
            {
                // Read only to say whether one is set; never shown, never logged.
                bool set = row.Current is string s && s.Trim().Length > 0;
                if (row.Value != null) row.Value.text = set ? $"<color=#96C4A8>{Catalog.SecretLabel("x")}</color>" : Catalog.SecretLabel("");
                if (row.Clear != null && row.Clear.gameObject.activeSelf != set) row.Clear.gameObject.SetActive(set);
                break;
            }
            case ControlKind.Note:
                if (row.Value != null) row.Value.text = UiKit.Escape(row.Current as string ?? "");
                break;
            case ControlKind.Step:
            case ControlKind.Tiles:
            case ControlKind.Change:
            case ControlKind.Buttons:
            case ControlKind.Button:
                RefreshSpecial(row);
                break;
        }
    }

    // ------------------------------------------------------------------ changing values

    /// <summary>Saves a value (the setting's own type), unless it's the one it has already; then the mod hears of it.</summary>
    private void SetValue(Row row, object value)
    {
        if (!row.Editable) return;
        var v = Catalog.AsSetting(row.Type, value);
        if (v == null) { Say($"That isn't something {UiKit.Escape(row.Title)} can be."); RefreshRow(row); return; }
        if (Staging(row)) { Stage(row, v); ApplyFills(row, v); return; } // waits for Save (0.4.0), with what it fills in (0.4.1)
        WriteValue(row, v);
        if (_rebuildPending) return;
        if (row.Custom != null) RefreshAll(); else RefreshRow(row); // a page's rows can follow each other (the rings)
    }

    /// <summary>Saves one value now (a change on a page without Save, or Save itself); false when it wasn't saved (the footer says why).</summary>
    private bool WriteValue(Row row, object v)
    {
        if (Equals(v, row.SavedValue)) return true;
        try
        {
            if (row.Entry != null)
            {
                row.Entry.BoxedValue = v;
                // A mod that doesn't write its own file, and whose BepInEx config doesn't save on every change: saved here.
                if (!row.Mod.Page.SavesItself && !row.Mod.Config.SaveOnConfigSet) row.Mod.Config.Save();
                try { row.Mod.Page.Changed?.Invoke(row.Entry.Definition.Key); } catch (Exception e) { Log.Warn($"{row.Mod.Name} after a change: {e.Message}"); }
            }
            else if (row.Custom?.Set?.Invoke(v) is { } error) { Say(UiKit.Escape(error)); return false; }
            if (row.Custom?.Rebuild == true) _rebuildPending = true;
            return true;
        }
        catch (Exception e) { Say("That couldn't be saved: " + UiKit.Escape(e.Message)); return false; }
    }

    private void ClickRow(Row row, Vector2 pos)
    {
        switch (row.Kind)
        {
            case ControlKind.Switch: SetValue(row, !(row.Current is true)); break;
            case ControlKind.Button:
            case ControlKind.Buttons:
                ClickButtons(row, pos);
                break;
            case ControlKind.Slider:
                if (UiKit.Over(row.Track, pos)) { _drag = row; TickDrag(pos); }
                break;
            case ControlKind.Choice:
                if (UiKit.Over(row.Box, pos)) OpenDropdown(row);
                break;
            case ControlKind.Key:
                if (UiKit.Over(row.Box, pos)) StartCapture(row);
                break;
            case ControlKind.Secret:
                if (UiKit.Over(row.Clear, pos)) { SetValue(row, ""); Say($"{UiKit.Escape(row.Title)} cleared."); }
                else FocusField(row, pos);
                break;
            case ControlKind.Text:
            case ControlKind.Number:
                FocusField(row, pos);
                break;
        }
    }

    /// <summary>A click on a field puts the cursor in it (the menu's own clicks drive every control; the game's UI clicks
    /// may never reach the field), as People of Nivalis's chat box does.</summary>
    private void FocusField(Row row, Vector2 pos)
    {
        if (row.Input == null || !row.Editable) return;
        try
        {
            var rt = row.Input.gameObject.GetComponent<RectTransform>();
            if (!UiKit.Over(rt, pos)) return;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(row.Input.gameObject);
            row.Input.ActivateInputField();
        }
        catch (Exception e) { Log.Debug("field focus: " + e.Message); }
    }

    private void TickDrag(Vector2 pos)
    {
        if (_drag == null) return;
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.isPressed) { _drag = null; CommitPending(); return; }
        if (_drag.Track == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_drag.Track, pos, null, out var local)) return;
        var rect = _drag.Track.rect;
        float t = rect.width > 1f ? Mathf.Clamp01((local.x - rect.xMin) / rect.width) : 0f;
        var c = _drag.Control;
        double v = c.Snap(c.Min + t * (c.Max - c.Min));
        if (Math.Abs(v - SliderValue(_drag)) > 1e-6) SetPending(_drag, v);
    }

    /// <summary>
    /// 0.5.4: a change row's "now" value a step down or up, as the mod says (it pins it, and plans around it). It acts at once,
    /// even on a page with Save (a what-if list, like the dial). False when there's no step that way (at its end).
    /// </summary>
    private bool StepChange(Row row, int dx, bool quiet = false)
    {
        if (!row.CanStep) return false;
        if (Time.unscaledTime - row.PressedAt < 0.12f) return true; // a held d-pad steps at a calm pace (review SW-4)
        string? next;
        try { next = row.Custom!.Step!(row.Custom.From?.Invoke(), dx); }
        catch (Exception e) { Log.Warn($"{row.Mod.Name}: stepping {row.Title}: {e.Message}"); Say("That couldn't be changed: " + UiKit.Escape(e.Message)); return true; }
        if (next == null) { if (!quiet) Say("That's as far as it goes."); return false; }
        string? why;
        try { why = row.Custom!.Set!(next); }
        catch (Exception e) { Log.Warn($"{row.Mod.Name}: setting {row.Title}: {e.Message}"); why = e.Message; }
        if (why != null) Say(UiKit.Escape(why));
        row.PressedAt = Time.unscaledTime;
        if (why == null && row.Custom!.Rebuild) { _rebuildPending = true; return true; } // rows come or go: built again (review SW-1)
        RefreshAll();
        return true;
    }

    /// <summary>Left/right on a row with the controller: a switch off/on, a bar a step, a dropdown to the next choice.</summary>
    private bool PadAdjust(Row row, int dx, float now)
    {
        switch (row.Kind)
        {
            // Left at the start of its range goes back to the list, as on any row (review SW-2).
            case ControlKind.Change when row.CanStep: return StepChange(row, dx, quiet: dx < 0) || dx > 0;
            case ControlKind.Switch: return false; // 0.4.2 (checklist 14): only A flips a switch; left goes back to the list, as on a heading
            case ControlKind.Button:
            case ControlKind.Buttons: return PadButtons(row, dx);
            case ControlKind.Slider: SetPending(row, row.Control.Stepped(SliderValue(row), dx)); return true;
            case ControlKind.Choice when row.Custom?.PickOnly != true:
            {
                var list = row.Control.ChoicesWith(row.CurrentText());
                int i = list.ToList().FindIndex(c => string.Equals(c, row.CurrentText(), StringComparison.OrdinalIgnoreCase));
                SetValue(row, list[((i + dx) % list.Count + list.Count) % list.Count]);
                return true;
            }
        }
        return false;
    }

    /// <summary>A on a row: a switch flips, a dropdown opens, a text field opens the keyboard, a key waits for a key, a button presses.</summary>
    private void PadChoose(Row row)
    {
        switch (row.Kind)
        {
            case ControlKind.Switch: SetValue(row, !(row.Current is true)); break;
            case ControlKind.Button:
            case ControlKind.Buttons:
            {
                var shown = ShownButtons(row);
                if (row.ButtonFocus < shown.Count) PressButton(row, shown[row.ButtonFocus]);
                break;
            }
            case ControlKind.Change: TogglePin(row); break;
            case ControlKind.Choice: OpenDropdown(row); break;
            case ControlKind.Key: StartCapture(row); break;
            case ControlKind.Slider: Say("Left and right move it."); break;
            default:
                if (row.Typed) OpenKeyboardFor(row);
                break;
        }
    }

    private void SetPending(Row row, double value)
    {
        if (_pendingRow != null && _pendingRow != row) CommitPending();
        row.Pending = row.Control.Snap(value);
        _pendingRow = row;
        _pendingAt = _movedAt = Time.unscaledTime;
        _movedRow = row;
        RefreshRow(row);
    }

    /// <summary>A bar that moved is saved (once it's let go, or a moment after the last step).</summary>
    private void CommitPending()
    {
        var row = _pendingRow;
        if (row == null) return;
        _pendingRow = null;
        if (row.Pending is double v)
        {
            row.Pending = null;
            SetValue(row, v);
        }
    }

    /// <summary>
    /// 0.4.2 (checklist 11): on a page with Save, what's typed waits for Save as it's typed (its UNSAVED tag at once, no Enter
    /// needed): a number once it reads as one, text as it is. A number field emptied or not reading as one lets go of what it
    /// held (review: a cleared field saved its last digits). A key waits until you leave its field (a part of one is never
    /// held). The field itself isn't touched while it's typed in; leaving it still checks and keeps it as before.
    /// </summary>
    private void StageTyped(Row r, string typed)
    {
        if (r.Kind is not (ControlKind.Number or ControlKind.Text) || typed == r.TypedSeen || !r.Editable || !Staging(r)) return;
        r.TypedSeen = typed;
        string t = typed.Trim();
        object? v = r.Kind == ControlKind.Number
            ? Catalog.ParseNumber(r.Control, t, whole: r.Type == typeof(int) || r.Type == typeof(long)) is double n ? Catalog.AsSetting(r.Type, n) : null
            : Catalog.AsSetting(r.Type, t);
        if (v != null) Stage(r, v);
        else if (r.HasStaged) { r.HasStaged = false; r.Staged = null; RefreshRow(r); RefreshSaveBar(); }
    }

    private bool AnyTextFocused()
    {
        foreach (var r in _rows) if (r.Input != null && r.Input.isFocused) return true;
        return false;
    }

    /// <summary>A field you were typing in and left (Enter, a click elsewhere) is saved; Esc puts it back as it was first.</summary>
    private void CommitTextsLosingFocus()
    {
        foreach (var r in _rows.ToList())
        {
            if (r.Input == null) continue;
            bool f = r.Input.isFocused;
            if (f) StageTyped(r, r.Input.text ?? "");
            if (r.WasFocused && !f) { CommitText(r, r.Input.text ?? ""); r.TypedSeen = null; }
            r.WasFocused = f;
        }
    }

    /// <summary>Every field being typed in is saved as it is (another group, the keyboard opened, the menu closing).</summary>
    private void CommitTexts()
    {
        foreach (var r in _rows.ToList())
        {
            if (r.Input == null || !(r.Input.isFocused || r.WasFocused)) continue;
            r.WasFocused = false;
            r.TypedSeen = null;
            string text = r.Input.text ?? "";
            try { r.Input.DeactivateInputField(); } catch { }
            CommitText(r, text);
        }
    }

    private void CommitText(Row row, string text)
    {
        if (!row.Editable) return;
        string t = (text ?? "").Trim();
        switch (row.Kind)
        {
            case ControlKind.Secret:
                if (row.Input != null) row.Input.text = "";
                if (t.Length == 0) return; // nothing typed: it stays as it was
                SetValue(row, t);
                Say($"{UiKit.Escape(row.Title)} saved (it's never shown).");
                return;
            case ControlKind.Number:
            {
                if (t == row.CurrentText()) return;
                var n = Catalog.ParseNumber(row.Control, t, whole: row.Type == typeof(int) || row.Type == typeof(long));
                if (n == null) { Say($"\"{UiKit.Escape(t)}\" isn't a number: {UiKit.Escape(row.Title)} stays {UiKit.Escape(row.CurrentText())}."); RefreshRow(row); return; }
                SetValue(row, n.Value);
                return;
            }
            default:
                if (t != row.CurrentText()) SetValue(row, t);
                else RefreshRow(row);
                return;
        }
    }

    // ------------------------------------------------------------------ a key setting waiting for its key

    private static Key[]? _allKeys;
    private int _captureFrame;

    private void StartCapture(Row row)
    {
        CommitPending();
        _capture = row;
        _captureFrame = Time.frameCount;
        RefreshRow(row);
    }

    private void TickCapture(Keyboard? kb, bool esc, bool click)
    {
        var row = _capture;
        if (row == null) return;
        if (esc || Pad.B || (click && Time.frameCount > _captureFrame)) { Pad.Consume(); _capture = null; RefreshRow(row); return; }
        if (kb == null || Time.frameCount <= _captureFrame) return;
        _allKeys ??= ((Key[])Enum.GetValues(typeof(Key))).Where(k => k != Key.None && k != Key.Escape && k.ToString() != "IMESelected").Distinct().ToArray();
        foreach (var k in _allKeys)
        {
            bool down;
            try { down = kb[k].wasPressedThisFrame; } catch { continue; }
            if (!down) continue;
            string name = k.ToString();
            string? problem = null;
            try { problem = row.Validate?.Invoke(name); } catch { }
            problem ??= row.Mod.Guid == Plugin.Guid ? null : SettingsMenu.KeyProblem(name);
            _capture = null;
            if (problem != null) Say(UiKit.Escape(problem));
            else SetValue(row, name);
            RefreshRow(row);
            return;
        }
    }
}
