using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NivalisSwitchboard;

/// <summary>
/// 0.4.0: Save on every page (the author: "each page has a save button at the bottom of it so we now have to click save in
/// order for changes and overrides to apply"). A change becomes pending, shown with an amber UNSAVED tag, until Save writes
/// every pending change of the page (the same path a change took before) or Discard drops them. Leaving the page, or closing
/// the menu, with pending changes asks first. Buttons, pins and a page's what-if rows (`immediate`) act at once; a page can
/// opt out entirely (`saveButton: false`). Also the tree's categories (a page's groups under a folding heading).
/// </summary>
internal sealed partial class SettingsWindow
{
    private const float SaveBarH = 60f;
    private RectTransform? _saveBar;
    private TextMeshProUGUI? _saveText;
    private Row? _barRow;
    private bool _saveBarShown;
    /// <summary>What to do once the leave question is answered (null: not asking).</summary>
    private Action? _afterLeave;
    private RectTransform? _leaveOverlay;
    private TextMeshProUGUI? _leaveText;
    private Row? _leaveRow;

    /// <summary>This row's change waits for Save (a setting or a page's own value; never a button, a pin or a what-if row).</summary>
    private static bool Staging(Row row) =>
        row.Mod != null && row.Mod.Page.SaveButton && row.Custom?.Immediate != true && row.Custom?.PickOnly != true && row.Editable;

    private int StagedCount => _rows.Count(r => r.HasStaged);

    /// <summary>The bar under the rows: how many changes wait, Discard and Save (on the control edge, like every control).</summary>
    private void BuildSaveBar()
    {
        _saveBar = UiKit.Rect(_window!, "SaveBar", new Vector2(0, 0), new Vector2(1, 0), new Vector2(TreeW + 14, FooterH), new Vector2(-22, FooterH + SaveBarH));
        var line = UiKit.Rect(_saveBar, "Line", new Vector2(0, 1), new Vector2(1, 1), new Vector2(LeftPad, -1), new Vector2(-RightPad, 0));
        var li = line.gameObject.AddComponent<Image>();
        li.sprite = Sprites.Cell;
        li.color = new Color(GoldDim.r, GoldDim.g, GoldDim.b, 0.5f);
        li.raycastTarget = false;
        _saveText = UiKit.Text(_saveBar, "Count", 15, Muted, TextAlignmentOptions.MidlineLeft, _bodyFont);
        UiKit.Place(_saveText.rectTransform, Vector2.zero, new Vector2(0.55f, 1), new Vector2(LeftPad, 0), Vector2.zero);
        _saveText.enableWordWrapping = false;
        var area = UiKit.Rect(_saveBar, "Buttons", new Vector2(0.55f, 0), new Vector2(1, 1), new Vector2(14, 10), new Vector2(-ControlEdge, -10));
        _barRow = new Row { Rt = _saveBar };
        _barRow.Buttons = new List<ButtonUi>
        {
            MakeButton(area, new PageButton { Label = "Discard" }),
            MakeButton(area, new PageButton { Label = "Save", Main = true }),
        };
        LayoutButtons(_barRow);
        _barRow.ButtonFocus = 1;
        ShowSaveBar(false);
    }

    /// <summary>The bar is there for pages that save on Save; the rows end above it.</summary>
    private void ShowSaveBar(bool on)
    {
        _saveBarShown = on;
        if (_saveBar != null) _saveBar.gameObject.SetActive(on);
        if (_viewport != null) _viewport.offsetMin = new Vector2(TreeW + 14, FooterH + (on ? SaveBarH : 0f));
        if (!on && _zone == Zone.Bar) _zone = Zone.Rows;
    }

    private void RefreshSaveBar()
    {
        if (_saveText == null || _barRow?.Buttons == null) return;
        int n = StagedCount;
        _saveText.text = n == 0 ? "No unsaved changes" : $"<color=#E0A84E>{n} unsaved change{(n == 1 ? "" : "s")}</color>";
        foreach (var b in _barRow.Buttons) b.Disabled = n == 0;
    }

    /// <summary>0.4.1: the rows this one fills in (its <see cref="PageRow.Fills"/>), changed to match, waiting for Save with it.</summary>
    private void ApplyFills(Row row, object value)
    {
        var fills = row.Custom?.Fills;
        if (fills == null) return;
        System.Collections.IDictionary? map;
        try { map = fills(value); }
        catch (Exception e) { Log.Debug($"fills for {row.Title}: {e.Message}"); return; }
        if (map == null || map.Count == 0) return;
        var who = RowsWho();
        var shared = StageKeys.Shared(who);
        foreach (System.Collections.DictionaryEntry kv in map)
        {
            if (kv.Key is not string k || kv.Value == null) continue;
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                string key = StageKeys.For(who, i);
                if (r == row || shared.Contains(key) || (key != "i:" + k && key != "s:" + k) || !Staging(r)) continue;
                var v = Catalog.AsSetting(r.Type, kv.Value);
                if (v != null) Stage(r, v);
            }
        }
    }

    /// <summary>Holds a change for Save, or lets it go when it's back to what's saved.</summary>
    private void Stage(Row row, object value)
    {
        var saved = row.SavedValue;
        if (Equals(value, saved)) { row.HasStaged = false; row.Staged = null; }
        else { row.HasStaged = true; row.Staged = value; }
        RefreshRow(row);
        RefreshSaveBar();
    }

    /// <summary>Writes every change waiting on this page, in the page's order; then the page is shown as saved.</summary>
    private void SaveStaged()
    {
        CommitPending();
        CommitTexts();
        var waiting = _rows.Where(r => r.HasStaged).ToList();
        if (waiting.Count == 0) return;
        int saved = 0;
        foreach (var r in waiting)
        {
            var v = r.Staged;
            r.HasStaged = false;
            r.Staged = null;
            if (v != null && WriteValue(r, v)) saved++;
        }
        Say(saved == waiting.Count ? $"Saved {saved} change{(saved == 1 ? "" : "s")}." : $"Saved {saved} of {waiting.Count} changes: the footer said why for the others.");
        RefreshAll();
        RefreshSaveBar();
    }

    private void DiscardStaged()
    {
        int n = 0;
        foreach (var r in _rows.Where(r => r.HasStaged)) { r.HasStaged = false; r.Staged = null; n++; }
        // A field being typed in shows what's saved again, and isn't held again when it's left (review of 0.4.2).
        foreach (var r in _rows.Where(r => r.Input != null && r.Input.isFocused))
            try { string now = r.CurrentText(); r.Input!.text = now; r.TypedSeen = now; } catch { }
        if (n > 0) Say($"Discarded {n} change{(n == 1 ? "" : "s")}.");
        RefreshAll(force: true);
        RefreshSaveBar();
    }

    /// <summary>Who each row is, kept across a rebuild (pending changes survive rows coming and going): see <see cref="StageKeys.For"/>.</summary>
    private List<StageKeys.Who> RowsWho() =>
        _rows.Select(r => new StageKeys.Who(r.Entry != null ? r.Entry.Definition.Section + "/" + r.Entry.Definition.Key : null, r.Custom?.Id, r.Title)).ToList();

    private Dictionary<string, object> TakeStaged()
    {
        var staged = new Dictionary<string, object>();
        var who = RowsWho();
        for (int i = 0; i < _rows.Count; i++)
            if (_rows[i].HasStaged && _rows[i].Staged != null) staged[StageKeys.For(who, i)] = _rows[i].Staged!;
        return staged;
    }

    private void PutStaged(Dictionary<string, object> staged)
    {
        if (staged.Count == 0) return;
        int put = 0;
        var who = RowsWho();
        var shared = StageKeys.Shared(who);
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            string key = StageKeys.For(who, i);
            if (!shared.Contains(key) && staged.TryGetValue(key, out var v) && Staging(r)) { r.HasStaged = true; r.Staged = v; put++; }
        }
        int lost = staged.Count - put;
        if (lost > 0) Say(lost == 1 ? "An unsaved change was let go: the row it was on moved." : $"{lost} unsaved changes were let go: the rows they were on moved.");
        RefreshSaveBar();
    }

    // ------------------------------------------------------------------ leaving a page with changes waiting

    /// <summary>Does <paramref name="go"/> now, or after asking when this page has changes waiting.</summary>
    private void Guarded(Action go)
    {
        if (_kbOpen) CloseKeyboard(save: true); // typed but not yet Done (the menu's key): it waits with the rest, so leaving asks
        CommitPending();
        CommitTexts();
        if (StagedCount == 0 || _leaveOverlay == null) { go(); return; }
        _afterLeave = go;
        if (_leaveText != null)
        {
            int n = StagedCount;
            _leaveText.text = $"This page has {n} unsaved change{(n == 1 ? "" : "s")}. Save {(n == 1 ? "it" : "them")} before you go?";
        }
        if (_leaveRow != null) _leaveRow.ButtonFocus = 2;
        _leaveOverlay.gameObject.SetActive(true);
    }

    /// <summary>The menu asked to close (its key, Esc, its X, B): asks first when changes wait.</summary>
    public void AskClose() => Guarded(() => WantsClose = true);

    private void BuildLeaveDialog()
    {
        _leaveOverlay = UiKit.Rect(_window!, "Leave", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var dim = _leaveOverlay.gameObject.AddComponent<Image>();
        dim.sprite = Sprites.Cell;
        dim.color = new Color(0, 0, 0, 0.55f);
        var box = UiKit.Rect(_leaveOverlay, "Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        box.sizeDelta = new Vector2(620, 210);
        var bg = box.gameObject.AddComponent<Image>();
        bg.sprite = Sprites.Bubble;
        bg.type = Image.Type.Sliced;
        var title = UiKit.Text(box, "Title", 22, NameColor, TextAlignmentOptions.TopLeft, _titleFont);
        UiKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -54), new Vector2(-28, -22));
        title.fontStyle = FontStyles.Bold;
        title.text = "Unsaved changes";
        _leaveText = UiKit.Text(box, "Text", 17, Body, TextAlignmentOptions.TopLeft, _bodyFont);
        UiKit.Place(_leaveText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -110), new Vector2(-28, -60));
        _leaveText.enableWordWrapping = true;
        var area = UiKit.Rect(box, "Buttons", new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 22), new Vector2(-28, 22 + ButtonH));
        _leaveRow = new Row { Rt = box };
        _leaveRow.Buttons = new List<ButtonUi>
        {
            MakeButton(area, new PageButton { Label = "Keep editing" }),
            MakeButton(area, new PageButton { Label = "Discard" }),
            MakeButton(area, new PageButton { Label = "Save", Main = true }),
        };
        LayoutButtons(_leaveRow);
        _leaveOverlay.gameObject.SetActive(false);
    }

    private bool Asking => _leaveOverlay != null && _leaveOverlay.gameObject.activeSelf;

    /// <summary>The leave question has the window to itself while it's up.</summary>
    private void TickLeave(Vector2 pos, bool click, bool esc)
    {
        if (_leaveRow?.Buttons == null) return;
        if (esc || Pad.B) { Answer(0); Pad.Consume(); return; }
        if (click)
            for (int i = 0; i < _leaveRow.Buttons.Count; i++)
                if (UiKit.Over(_leaveRow.Buttons[i].Rt, pos)) { Answer(i); return; }
        if (Pad.Dx != 0) _leaveRow.ButtonFocus = Math.Clamp(_leaveRow.ButtonFocus + Math.Sign(Pad.Dx), 0, _leaveRow.Buttons.Count - 1);
        if (Pad.A) Answer(_leaveRow.ButtonFocus);
        if (Pad.Dx != 0 || Pad.A) Pad.Consume();
    }

    private void Answer(int choice)
    {
        var go = _afterLeave;
        _afterLeave = null;
        if (_leaveOverlay != null) _leaveOverlay.gameObject.SetActive(false);
        if (_leaveRow?.Buttons is { Count: > 2 } b) b[Math.Clamp(choice, 0, 2)].PressedAt = Time.unscaledTime;
        if (choice == 0 || go == null) return; // keep editing
        if (choice == 2) SaveStaged(); else DiscardStaged();
        go();
    }

    /// <summary>The bar's buttons pressed (mouse, or A while the pad's highlight is on the bar; Start saves from anywhere).</summary>
    private void PressBar(int index)
    {
        if (_barRow?.Buttons == null || StagedCount == 0) return;
        _barRow.Buttons[index].PressedAt = Time.unscaledTime;
        if (index == 1) SaveStaged(); else DiscardStaged();
    }

    // ------------------------------------------------------------------ the tree's categories

    /// <summary>Categories open in the tree (by mod and name); folded until opened, or until one of their groups is shown.</summary>
    private readonly HashSet<string> _catOpen = new();

    private static string CatKey(FoundMod mod, string cat) => mod.Guid + "|" + cat;

    private void ToggleCategory(int mod, string cat)
    {
        string key = CatKey(_mods[mod], cat);
        if (!_catOpen.Remove(key)) _catOpen.Add(key);
        BuildTree();
        _treeFocus = Math.Max(0, Pickable().FindIndex(t => t.Mod == mod && t.Category == cat));
        TreeScrollTo(_treeFocus);
    }
}
