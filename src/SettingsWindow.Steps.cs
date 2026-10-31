using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NivalisSwitchboard;

/// <summary>
/// The page layout every row follows, and the guided-page rows (People of Nivalis's Usage Optimizer, the author's choice of
/// "step based, helps guide them through it"). Two edges run down the whole page: every control (bar, field, dropdown, switch,
/// a row of buttons) ends on the control edge, and the narrow end column right after it holds only what belongs to a control
/// (a bar's value, a tile's %, a pin). Steps sit on a rail with their number; a change row reads now -> recommended.
/// </summary>
internal sealed partial class SettingsWindow
{
    /// <summary>Row padding on the right, the end column (values, pins) and the gap between it and the control edge.</summary>
    private const float RightPad = 16f, EndCol = 72f, EndGap = 12f;
    /// <summary>Row padding on the left, and how far a step's rows are indented (its rail and number).</summary>
    private const float LeftPad = 16f, Rail = 48f;
    /// <summary>Where every control ends, from a row's right edge.</summary>
    private const float ControlEdge = RightPad + EndCol + EndGap;
    private const float ButtonH = 36f, ButtonMinW = 150f, ButtonGap = 10f;
    private static readonly Color Amber = new Color32(224, 168, 78, 255);
    private static readonly Color Green = new Color32(143, 181, 115, 255);
    private static readonly Color WindowFill = new Color32(36, 33, 30, 255);
    private static readonly Color ButtonFill = new Color32(26, 44, 42, 255);
    private static readonly Color DarkText = new Color32(28, 22, 16, 255);

    /// <summary>One button of a row: its look and when it was pressed.</summary>
    internal sealed class ButtonUi
    {
        public PageButton Def = new();
        public RectTransform Rt = null!;
        public Image Fill = null!, Edge = null!;
        public TextMeshProUGUI Label = null!;
        public float PressedAt = -10f, Width;
        public string LastLabel = "";
        public bool Shown = true;
        /// <summary>Greyed and not pressable (the Save bar with nothing to save).</summary>
        public bool Disabled;
    }

    /// <summary>One status tile.</summary>
    internal sealed class TileUi
    {
        public Image Edge = null!;
        public TextMeshProUGUI Title = null!, Value = null!, Caption = null!;
        public RectTransform Used = null!, Projected = null!, Tick = null!;
    }

    private bool _stepSeen;

    private static float HeightOf(SettingControl control, int tiles) => control.Kind switch
    {
        ControlKind.Section => 42f,
        ControlKind.Step => 74f,
        ControlKind.Subsection => 38f,
        ControlKind.ChangeHeader => 34f,
        ControlKind.Change => 46f,
        ControlKind.Buttons or ControlKind.Button => 58f,
        ControlKind.Tiles => Math.Max(1, (tiles + 2) / 3) * 92f + 10f,
        _ => RowH,
    };

    private static int TileCount(PageRow? custom)
    {
        try { return custom?.Tiles?.Invoke()?.Cast<object>().Count() ?? 0; } catch { return 0; }
    }

    /// <summary>How far a row's name starts from the left (a step's rows sit past its rail).</summary>
    private static float LabelLeft(Row row) => LeftPad + (row.InStep ? Rail : 0f);

    private Image Line(RectTransform parent, string name, Vector2 anchor, float left, float right, float y, float alpha)
    {
        var rt = UiKit.Rect(parent, name, new Vector2(0, anchor.y), new Vector2(1, anchor.y), new Vector2(left, y), new Vector2(-right, y + 1));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Sprites.Cell;
        img.color = new Color(GoldDim.r, GoldDim.g, GoldDim.b, alpha);
        img.raycastTarget = false;
        return img;
    }

    private TextMeshProUGUI Caps(RectTransform parent, string name, string text, float size, Color color, TextAlignmentOptions align)
    {
        var t = UiKit.Text(parent, name, size, color, align, _bodyFont);
        t.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
        t.characterSpacing = 3f;
        t.enableWordWrapping = false;
        t.text = UiKit.Escape(text);
        return t;
    }

    /// <summary>The rail beside a step's rows (a thin line down from its number to the next step).</summary>
    private void BuildRail(Row row)
    {
        float top = row.Kind == ControlKind.Step ? 6f + 32f + 4f : 0f; // a step's own: from under its number
        var rt = UiKit.Rect(row.Rt, "Rail", new Vector2(0, 0), new Vector2(0, 1), new Vector2(LeftPad + 15f, -4f), new Vector2(LeftPad + 17f, -top));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Sprites.Cell;
        img.color = new Color(GoldDim.r, GoldDim.g, GoldDim.b, 0.5f);
        img.raycastTarget = false;
        row.Rail = img;
    }

    /// <summary>The last step's rows have no rail below them (nothing to lead to).</summary>
    private void TrimLastRail()
    {
        int last = _rows.FindLastIndex(r => r.Kind == ControlKind.Step);
        if (last < 0) return;
        for (int i = last; i < _rows.Count; i++) if (_rows[i].Rail != null) _rows[i].Rail!.enabled = false;
    }

    // ------------------------------------------------------------------ building the guided rows

    /// <summary>Builds the row kinds that aren't one control beside a name; true when it did.</summary>
    private bool BuildSpecial(Row row)
    {
        var rt = row.Rt;
        switch (row.Kind)
        {
            case ControlKind.Step: BuildStep(row); return true;
            case ControlKind.Subsection:
            {
                var t = Caps(rt, "Subsection", row.Title, 12, Muted, TextAlignmentOptions.BottomLeft);
                UiKit.Place(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(LabelLeft(row), 6), new Vector2(-RightPad, -4));
                row.TitleText = t;
                // A line above it, unless it comes straight under its step's heading.
                if (_rows.Count > 0 && _rows[^1].Kind != ControlKind.Step) Line(rt, "Rule", new Vector2(0, 1), LabelLeft(row), RightPad, -2, 0.4f);
                return true;
            }
            case ControlKind.Tiles: BuildTiles(row); return true;
            case ControlKind.ChangeHeader:
            {
                var c = row.Custom!;
                var t = Caps(rt, "Setting", row.Title, 12, Muted, TextAlignmentOptions.BottomLeft);
                UiKit.Place(t.rectTransform, new Vector2(0, 0), new Vector2(0.55f, 1), new Vector2(LabelLeft(row), 6), new Vector2(0, -4));
                var area = ControlArea(row);
                var from = Caps(area, "Now", c.FromLabel, 12, Muted, TextAlignmentOptions.BottomRight);
                UiKit.Place(from.rectTransform, new Vector2(0, 0), new Vector2(0.44f, 1), new Vector2(0, -4), new Vector2(0, 0));
                var to = Caps(area, "Then", c.ToLabel, 12, Muted, TextAlignmentOptions.BottomLeft);
                UiKit.Place(to.rectTransform, new Vector2(0.56f, 0), new Vector2(1, 1), new Vector2(0, -4), new Vector2(0, 0));
                var end = Caps(rt, "End", c.EndLabel, 12, Muted, TextAlignmentOptions.Bottom);
                UiKit.Place(end.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-RightPad - EndCol, 6), new Vector2(-RightPad, -4));
                Line(rt, "Rule", new Vector2(0, 0), LabelLeft(row), RightPad, 2, 0.55f);
                return true;
            }
            case ControlKind.Change:
            {
                var t = UiKit.Text(rt, "Title", 17, Body, TextAlignmentOptions.MidlineLeft, _bodyFont);
                UiKit.Place(t.rectTransform, new Vector2(0, 0), new Vector2(0.55f, 1), new Vector2(LabelLeft(row), 0), new Vector2(0, 0));
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Ellipsis;
                t.text = UiKit.Escape(row.Title);
                row.TitleText = t;
                var area = ControlArea(row);
                row.FromText = UiKit.Text(area, "Now", 17, Muted, TextAlignmentOptions.MidlineRight, _bodyFont);
                UiKit.Place(row.FromText.rectTransform, new Vector2(0, 0), new Vector2(0.44f, 1), Vector2.zero, Vector2.zero);
                if (row.CanStep)
                {
                    // 0.5.4: its value stepped right here: - and + either side of it (left/right with a controller).
                    UiKit.Place(row.FromText.rectTransform, new Vector2(0.07f, 0), new Vector2(0.37f, 1), Vector2.zero, Vector2.zero);
                    var less = UiKit.Text(area, "Less", 20, GoldDim, TextAlignmentOptions.Center, _bodyFont);
                    UiKit.Place(less.rectTransform, new Vector2(0, 0), new Vector2(0.07f, 1), Vector2.zero, Vector2.zero);
                    less.text = "-"; // plain ASCII: the game's fonts may not have the angle quotes
                    var more = UiKit.Text(area, "More", 20, GoldDim, TextAlignmentOptions.Center, _bodyFont);
                    UiKit.Place(more.rectTransform, new Vector2(0.37f, 0), new Vector2(0.44f, 1), Vector2.zero, Vector2.zero);
                    more.text = "+";
                    row.StepLess = less.rectTransform;
                    row.StepMore = more.rectTransform;
                }
                var arrow = UiKit.Text(area, "Arrow", 17, GoldDim, TextAlignmentOptions.Center, _bodyFont);
                UiKit.Place(arrow.rectTransform, new Vector2(0.44f, 0), new Vector2(0.56f, 1), Vector2.zero, Vector2.zero);
                arrow.text = "→";
                row.ToText = UiKit.Text(area, "Then", 17, Gold, TextAlignmentOptions.MidlineLeft, _bodyFont);
                UiKit.Place(row.ToText.rectTransform, new Vector2(0.56f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);
                row.ToText.fontStyle = FontStyles.Bold;
                foreach (var x in new[] { row.FromText, row.ToText }) { x.enableWordWrapping = false; x.overflowMode = TextOverflowModes.Ellipsis; }
                if (row.HasPin) BuildPin(row, rt);
                Line(rt, "Rule", new Vector2(0, 0), LabelLeft(row), RightPad, 0, 0.3f);
                return true;
            }
            case ControlKind.Buttons:
            case ControlKind.Button:
            {
                // A lone button is a row of one (the same look, press and focus as any other).
                var defs = row.Kind == ControlKind.Buttons
                    ? row.Custom?.Buttons ?? new List<PageButton>()
                    : new List<PageButton> { new() { Label = row.Control.Unit.Length > 0 ? row.Control.Unit : "Do it", Click = row.Custom?.Click, Rebuild = row.Custom?.Rebuild == true } };
                if (row.Kind == ControlKind.Button)
                {
                    // A lone button keeps its name and line on the left, like any setting.
                    BuildLabel(row);
                }
                else if ((row.Custom?.Hint ?? "").Length > 0 || row.Custom?.HintLive != null)
                {
                    // A buttons row's note on the left (0.4.0: "Sends one short request: a few tokens").
                    var note = UiKit.Text(row.Rt, "Hint", 14, Muted, TextAlignmentOptions.MidlineLeft, _bodyFont);
                    UiKit.Place(note.rectTransform, new Vector2(0, 0), new Vector2(0.55f, 1), new Vector2(LabelLeft(row), 0), new Vector2(0, 0));
                    note.enableWordWrapping = true;
                    note.overflowMode = TextOverflowModes.Ellipsis;
                    note.text = UiKit.Escape(row.Custom?.Hint ?? "");
                    row.HintText = note;
                }
                var area = ControlArea(row);
                row.Area = area;
                row.Buttons = defs.Select(d => MakeButton(area, d)).ToList();
                LayoutButtons(row);
                return true;
            }
        }
        return false;
    }

    /// <summary>A row's name and the line under it (a setting's look).</summary>
    private void BuildLabel(Row row)
    {
        var t = UiKit.Text(row.Rt, "Title", 19, Body, TextAlignmentOptions.BottomLeft, _bodyFont);
        UiKit.Place(t.rectTransform, new Vector2(0, 0.5f), new Vector2(0.55f, 1), new Vector2(LabelLeft(row), 0), new Vector2(0, -6));
        t.fontStyle = FontStyles.Bold;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.text = UiKit.Escape(row.Title);
        row.TitleText = t;
        var h = UiKit.Text(row.Rt, "Hint", 14, Muted, TextAlignmentOptions.TopLeft, _bodyFont);
        UiKit.Place(h.rectTransform, new Vector2(0, 0), new Vector2(0.55f, 0.5f), new Vector2(LabelLeft(row), 4), new Vector2(0, -2));
        h.enableWordWrapping = false;
        h.overflowMode = TextOverflowModes.Ellipsis;
        h.text = UiKit.Escape(row.Custom?.Hint ?? "");
        row.HintText = h;
    }

    /// <summary>The control column of a row: from just past the names to the control edge.</summary>
    private static RectTransform ControlArea(Row row) =>
        UiKit.Rect(row.Rt, "Control", new Vector2(0.55f, 0), new Vector2(1, 1), new Vector2(14, 10), new Vector2(-ControlEdge, -10));

    private void BuildStep(Row row)
    {
        var rt = row.Rt;
        var c = row.Custom!;
        // The number on the rail: a gold ring, filled once the step is done; a soft glow on the step you're on.
        var glow = UiKit.Rect(rt, "Glow", new Vector2(0, 1), new Vector2(0, 1), new Vector2(LeftPad - 5, -6 - 37), new Vector2(LeftPad + 37, -6 + 5));
        row.NumGlow = glow.gameObject.AddComponent<Image>();
        row.NumGlow.sprite = Sprites.Knob;
        row.NumGlow.color = new Color(Gold.r, Gold.g, Gold.b, 0.16f);
        row.NumGlow.raycastTarget = false;
        var dot = UiKit.Rect(rt, "Number", new Vector2(0, 1), new Vector2(0, 1), new Vector2(LeftPad, -6 - 32), new Vector2(LeftPad + 32, -6));
        row.NumFill = dot.gameObject.AddComponent<Image>();
        row.NumFill.sprite = Sprites.Knob;
        row.NumFill.color = WindowFill;
        row.NumFill.raycastTarget = false;
        var ring = UiKit.Rect(dot, "Ring", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var ri = ring.gameObject.AddComponent<Image>();
        ri.sprite = Sprites.Outline;
        ri.type = Image.Type.Sliced;
        ri.color = Gold;
        ri.raycastTarget = false;
        row.NumText = UiKit.Text(dot, "N", 16, Gold, TextAlignmentOptions.Center, _titleFont);
        UiKit.Place(row.NumText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        row.NumText.fontStyle = FontStyles.Bold;
        row.NumText.text = c.Number.ToString(CultureInfo.InvariantCulture);
        // Its title in gold, a chip after it, and its line.
        var t = UiKit.Text(rt, "Title", 20, NameColor, TextAlignmentOptions.TopLeft, _titleFont);
        UiKit.Place(t.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(LeftPad + Rail, -36), new Vector2(-RightPad, -8));
        t.fontStyle = FontStyles.Bold;
        t.enableWordWrapping = false;
        t.text = UiKit.Escape(row.Title);
        row.TitleText = t;
        var chip = UiKit.Rect(rt, "Chip", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
        chip.pivot = new Vector2(0, 0.5f);
        row.ChipImg = chip.gameObject.AddComponent<Image>();
        row.ChipImg.sprite = Sprites.Outline;
        row.ChipImg.type = Image.Type.Sliced;
        row.ChipImg.raycastTarget = false;
        row.ChipText = UiKit.Text(chip, "Words", 12, Muted, TextAlignmentOptions.Center, _bodyFont);
        UiKit.Place(row.ChipText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        row.ChipText.fontStyle = FontStyles.Bold;
        row.ChipText.enableWordWrapping = false;
        row.ChipRt = chip;
        var h = UiKit.Text(rt, "Line", 14, Muted, TextAlignmentOptions.TopLeft, _bodyFont);
        UiKit.Place(h.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(LeftPad + Rail, 4), new Vector2(-RightPad, -40));
        h.enableWordWrapping = false;
        h.overflowMode = TextOverflowModes.Ellipsis;
        h.text = UiKit.Escape(c.Hint);
        row.HintText = h;
    }

    private void BuildTiles(Row row)
    {
        int n = row.TileCountBuilt;
        row.Tiles = new List<TileUi>();
        if (n == 0) return;
        int perLine = Math.Min(3, n);
        for (int i = 0; i < n; i++)
        {
            int line = i / 3, col = i % 3;
            float x0 = col / (float)perLine, x1 = (col + 1) / (float)perLine;
            var tile = UiKit.Rect(row.Rt, "Tile", new Vector2(x0, 1), new Vector2(x1, 1), Vector2.zero, Vector2.zero);
            tile.pivot = new Vector2(0.5f, 1f);
            // Equal tiles from the left padding to the right one, 12 px apart.
            float left = col == 0 ? LeftPad : 6f, right = col == perLine - 1 ? RightPad : 6f;
            tile.offsetMin = new Vector2(left, -(line * 92f) - 4f - 84f);
            tile.offsetMax = new Vector2(-right, -(line * 92f) - 4f);
            var bg = tile.gameObject.AddComponent<Image>();
            bg.sprite = Sprites.Field;
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;
            var edge = UiKit.Rect(tile, "Edge", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var ei = edge.gameObject.AddComponent<Image>();
            ei.sprite = Sprites.Outline;
            ei.type = Image.Type.Sliced;
            ei.color = new Color(0, 0, 0, 0);
            ei.raycastTarget = false;
            var ui = new TileUi { Edge = ei };
            ui.Title = UiKit.Text(tile, "Name", 15, Body, TextAlignmentOptions.TopLeft, _bodyFont);
            UiKit.Place(ui.Title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -30), new Vector2(-14, -8));
            ui.Title.fontStyle = FontStyles.Bold;
            ui.Title.enableWordWrapping = false;
            ui.Title.overflowMode = TextOverflowModes.Ellipsis;
            // The meter, and its value straight after it.
            var bar = UiKit.Rect(tile, "Meter", new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -46), new Vector2(-14 - 52, -36));
            var bbg = bar.gameObject.AddComponent<Image>();
            bbg.sprite = Sprites.Field;
            bbg.type = Image.Type.Sliced;
            var inner = UiKit.Rect(bar, "Inside", Vector2.zero, Vector2.one, new Vector2(2, 2), new Vector2(-2, -2));
            RectTransform Part(string name, Color col2)
            {
                var p = UiKit.Rect(inner, name, Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero);
                var img = p.gameObject.AddComponent<Image>();
                img.sprite = Sprites.Cell;
                img.type = Image.Type.Sliced;
                img.color = col2;
                img.raycastTarget = false;
                return p;
            }
            ui.Projected = Part("Projected", new Color(Gold.r, Gold.g, Gold.b, 0.3f));
            ui.Used = Part("Used", Gold);
            ui.Tick = UiKit.Rect(bar, "Pace", Vector2.zero, new Vector2(0, 1), new Vector2(-1, -3), new Vector2(1, 3));
            var ti = ui.Tick.gameObject.AddComponent<Image>();
            ti.sprite = Sprites.Cell;
            ti.color = Color.white;
            ti.raycastTarget = false;
            ui.Value = UiKit.Text(tile, "Value", 16, Body, TextAlignmentOptions.MidlineLeft, _bodyFont);
            UiKit.Place(ui.Value.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-14 - 44, -50), new Vector2(-6, -32));
            ui.Value.fontStyle = FontStyles.Bold;
            ui.Value.enableWordWrapping = false;
            ui.Caption = UiKit.Text(tile, "Line", 13, Muted, TextAlignmentOptions.TopLeft, _bodyFont);
            UiKit.Place(ui.Caption.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(14, 8), new Vector2(-14, -56));
            ui.Caption.enableWordWrapping = false;
            ui.Caption.overflowMode = TextOverflowModes.Ellipsis;
            row.Tiles.Add(ui);
        }
    }

    private ButtonUi MakeButton(RectTransform area, PageButton def)
    {
        var rt = UiKit.Rect(area, "Button", new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero);
        rt.pivot = new Vector2(1, 0.5f);
        var fill = rt.gameObject.AddComponent<Image>();
        fill.sprite = Sprites.Knob;
        fill.type = Image.Type.Sliced;
        var edgeRt = UiKit.Rect(rt, "Edge", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var edge = edgeRt.gameObject.AddComponent<Image>();
        edge.sprite = Sprites.Outline;
        edge.type = Image.Type.Sliced;
        edge.raycastTarget = false;
        var label = UiKit.Text(rt, "Label", 16, NameColor, TextAlignmentOptions.Center, _bodyFont);
        UiKit.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-12, 0));
        label.fontStyle = FontStyles.Bold;
        label.enableWordWrapping = false;
        var b = new ButtonUi { Def = def, Rt = rt, Fill = fill, Edge = edge, Label = label };
        SetButtonLabel(b, def.Label);
        return b;
    }

    private static void SetButtonLabel(ButtonUi b, string text)
    {
        b.LastLabel = text;
        b.Label.text = UiKit.Escape(text);
        float w = 0f;
        try { w = b.Label.GetPreferredValues(b.Label.text).x; } catch { }
        b.Width = Mathf.Max(ButtonMinW, Mathf.Ceil(w) + 44f);
    }

    /// <summary>The shown buttons, right to left from the control edge (the main one, given last, ends on it).</summary>
    private static void LayoutButtons(Row row)
    {
        if (row.Buttons == null) return;
        float x = 0f;
        for (int i = row.Buttons.Count - 1; i >= 0; i--)
        {
            var b = row.Buttons[i];
            b.Rt.gameObject.SetActive(b.Shown);
            if (!b.Shown) continue;
            b.Rt.sizeDelta = new Vector2(b.Width, ButtonH);
            b.Rt.anchoredPosition = new Vector2(-x, 0);
            x += b.Width + ButtonGap;
        }
    }

    private List<ButtonUi> ShownButtons(Row row) => row.Buttons?.Where(b => b.Shown).ToList() ?? new List<ButtonUi>();

    // ------------------------------------------------------------------ refreshing them

    private void RefreshSpecial(Row row)
    {
        var c = row.Custom;
        switch (row.Kind)
        {
            case ControlKind.Step:
            {
                string chip = Try(c?.Chip) ?? "", tone = Try(c?.ChipTone) ?? "", state = Try(c?.State) ?? "";
                if (chip != row.LastChip || tone != row.LastTone)
                {
                    row.LastChip = chip;
                    row.LastTone = tone;
                    var col = tone == "ok" ? Green : tone == "warn" ? Amber : Muted;
                    row.ChipText!.text = UiKit.Escape(chip);
                    row.ChipText.color = col;
                    row.ChipImg!.color = new Color(col.r, col.g, col.b, 0.75f);
                    row.ChipRt!.gameObject.SetActive(chip.Length > 0);
                    float tw = 0f, cw = 0f;
                    try { tw = row.TitleText!.GetPreferredValues(row.TitleText.text).x; cw = row.ChipText.GetPreferredValues(row.ChipText.text).x; } catch { }
                    row.ChipRt.sizeDelta = new Vector2(Mathf.Ceil(cw) + 20f, 22f);
                    row.ChipRt.anchoredPosition = new Vector2(LeftPad + Rail + tw + 12f, -6 - 15f);
                }
                if (state != row.LastState)
                {
                    row.LastState = state;
                    bool done = state == "done";
                    row.NumFill!.color = done ? Gold : WindowFill;
                    row.NumText!.color = done ? DarkText : Gold;
                    row.NumGlow!.enabled = state == "now";
                }
                break;
            }
            case ControlKind.Tiles:
            {
                List<IDictionary> tiles;
                try { tiles = (c?.Tiles?.Invoke() ?? Array.Empty<object>()).OfType<IDictionary>().ToList(); } catch { tiles = new List<IDictionary>(); }
                if (tiles.Count != row.TileCountBuilt) { _rebuildPending = true; return; } // budgets came or went
                for (int i = 0; i < tiles.Count && i < row.Tiles!.Count; i++)
                {
                    var d = tiles[i];
                    var ui = row.Tiles[i];
                    ui.Title.text = UiKit.Escape(d.Contains("title") ? d["title"] as string ?? "" : "");
                    ui.Value.text = UiKit.Escape(d.Contains("value") ? d["value"] as string ?? "" : "");
                    ui.Caption.text = UiKit.Escape(d.Contains("caption") ? d["caption"] as string ?? "" : "");
                    bool warn = d.Contains("warn") && d["warn"] is true;
                    ui.Edge.color = warn ? Amber : new Color(0, 0, 0, 0);
                    ui.Value.color = warn ? Amber : Body;
                    var f = d.Contains("meter") ? d["meter"] as double[] : null;
                    float used = f is { Length: > 0 } ? Mathf.Clamp01((float)f[0]) : 0f;
                    float pace = f is { Length: > 1 } ? Mathf.Clamp01((float)f[1]) : -1f;
                    if (f is { Length: > 1 } && f[1] < 0) pace = -1f;
                    float proj = f is { Length: > 2 } ? Mathf.Clamp01((float)f[2]) : used;
                    ui.Used.anchorMax = new Vector2(used, 1f);
                    ui.Used.gameObject.SetActive(used > 0.001f);
                    ui.Projected.anchorMax = new Vector2(Mathf.Max(used, proj), 1f);
                    ui.Projected.gameObject.SetActive(proj > 0.001f);
                    ui.Tick.gameObject.SetActive(pace >= 0f);
                    ui.Tick.anchorMin = new Vector2(Mathf.Max(0, pace), 0);
                    ui.Tick.anchorMax = new Vector2(Mathf.Max(0, pace), 1);
                }
                break;
            }
            case ControlKind.Change:
            {
                if (row.FromText != null) row.FromText.text = UiKit.Escape(Try(c?.From) ?? "");
                if (row.ToText != null)
                {
                    string tone = Try(c?.ToTone) ?? "";
                    row.ToText.text = UiKit.Escape(Try(c?.To) ?? "");
                    row.ToText.color = tone == "kept" ? Amber : tone == "same" ? Muted : Gold;
                }
                break;
            }
            case ControlKind.Buttons:
            case ControlKind.Button:
            {
                if (row.Buttons == null) break;
                var before = ShownButtons(row);
                var focused = row.ButtonFocus < before.Count ? before[row.ButtonFocus] : null;
                bool moved = false;
                foreach (var b in row.Buttons)
                {
                    bool shown = true;
                    try { shown = b.Def.Visible?.Invoke() ?? true; } catch { }
                    if (shown != b.Shown) { b.Shown = shown; moved = true; }
                    string? live = null;
                    try { live = b.Def.LabelLive?.Invoke(); } catch { }
                    if (live != null && live != b.LastLabel) { SetButtonLabel(b, live); moved = true; }
                }
                if (moved) LayoutButtons(row);
                // The highlight stays on the button it was on (review: Undo appearing after Apply took it over, so a second
                // press undid what was just applied); if that one went, the nearest that's left.
                var after = ShownButtons(row);
                int at = focused != null ? after.IndexOf(focused) : -1;
                row.ButtonFocus = at >= 0 ? at : Math.Clamp(row.ButtonFocus, 0, Math.Max(0, after.Count - 1));
                break;
            }
        }
    }

    private static string? Try(Func<string?>? f)
    {
        try { return f?.Invoke(); } catch { return null; }
    }

    /// <summary>
    /// The buttons' look each frame: the main one gold, the rest dark with a gold edge; brighter under the mouse or the pad's
    /// highlight; and for a moment after a press, a dip and a gold flash (the author: "animate in some way so the user knows
    /// when they clicked a button").
    /// </summary>
    private void ButtonVisuals(Row row, bool rowFocused, Vector2 mouse, float now)
    {
        if (row.Buttons == null) return;
        var shown = ShownButtons(row);
        for (int i = 0; i < shown.Count; i++)
        {
            var b = shown[i];
            if (b.Def.Enabled != null) { try { b.Disabled = !b.Def.Enabled(); } catch { b.Disabled = false; } } // 0.4.2: a page's "enabled"
            bool hover = !b.Disabled && UiKit.Over(b.Rt, mouse) && !_kbOpen && _dropRow == null;
            bool focus = !b.Disabled && rowFocused && Pad.Active && i == row.ButtonFocus;
            float t = (now - b.PressedAt) / PressTime;
            float k = t >= 0f && t < 1f ? Mathf.Sin(t * Mathf.PI) : 0f;
            Color baseFill = b.Def.Main ? Gold : ButtonFill;
            Color lit = b.Def.Main ? new Color(0.95f, 0.78f, 0.5f, 1f) : new Color(0.2f, 0.27f, 0.25f, 1f);
            Color fill = hover || focus ? lit : baseFill;
            b.Fill.color = Color.Lerp(fill, new Color(1f, 0.84f, 0.55f, 1f), k);
            b.Edge.color = b.Def.Main ? new Color(0, 0, 0, 0) : focus ? NameColor : hover ? Gold : GoldDim;
            b.Label.color = Color.Lerp(b.Def.Main ? DarkText : NameColor, DarkText, k);
            if (b.Disabled) { b.Fill.color = new Color(baseFill.r, baseFill.g, baseFill.b, 0.35f); b.Label.color = Muted; b.Edge.color = new Color(GoldDim.r, GoldDim.g, GoldDim.b, 0.4f); }
            float s = 1f - 0.07f * k;
            b.Rt.localScale = new Vector3(s, s, 1f);
        }
    }

    private void PressButton(Row row, ButtonUi b)
    {
        if (b.Disabled) return; // greyed out: nothing to do (0.4.2)
        b.PressedAt = Time.unscaledTime;
        try
        {
            if (b.Def.Click?.Invoke() is { } note) Say(UiKit.Escape(note));
            if (b.Def.Rebuild) { _rebuildPending = true; _rebuildAt = Time.unscaledTime + PressTime; }
        }
        catch (Exception e) { Say("That didn't work: " + UiKit.Escape(e.Message)); }
        RefreshAll();
    }

    private bool ClickButtons(Row row, Vector2 pos)
    {
        var shown = ShownButtons(row);
        for (int i = 0; i < shown.Count; i++)
        {
            if (!UiKit.Over(shown[i].Rt, pos)) continue;
            row.ButtonFocus = i;
            PressButton(row, shown[i]);
            return true;
        }
        return false;
    }

    /// <summary>Left/right through a row's buttons; false at the left end (the highlight goes back to the tree, as elsewhere).</summary>
    private bool PadButtons(Row row, int dx)
    {
        int count = ShownButtons(row).Count;
        int next = row.ButtonFocus + Math.Sign(dx);
        if (next < 0 || next >= count) return dx > 0; // at the right end nothing moves; at the left, back to the tree
        row.ButtonFocus = next;
        return true;
    }

    // ------------------------------------------------------------------ rows that come and go

    /// <summary>Rows a page shows only sometimes: hidden ones take no room (the rows close up), and come back where they were.</summary>
    private void RefreshVisibility()
    {
        bool changed = false;
        foreach (var r in _rows)
        {
            bool v = true;
            try { v = r.Custom?.Visible?.Invoke() ?? true; } catch { }
            if (v != r.Shown) { r.Shown = v; changed = true; }
        }
        if (!changed) return;
        float y = 0f;
        foreach (var r in _rows)
        {
            if (r.Rt.gameObject.activeSelf != r.Shown) r.Rt.gameObject.SetActive(r.Shown);
            if (!r.Shown) continue;
            r.Rt.anchoredPosition = new Vector2(0, -6 - y);
            y += r.Height;
        }
        _nextY = y;
        _contentHeight = _nextY + 18f;
        if (_content != null) _content.sizeDelta = new Vector2(0, _contentHeight);
        if (_focus >= 0 && _focus < _rows.Count && !_rows[_focus].Focusable) _focus = Focusable(_focus, 1);
    }
}
