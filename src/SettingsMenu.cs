using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NivalisSwitchboard;

/// <summary>
/// The menu, as the author's mods and the game see it: Open (by a mod's GUID, to show its settings first), Close, IsOpen.
/// Called by reflection from the mods (see CONTRACT.md), from the title / pause menu button, and by its key. Only one
/// window takes input while it's up: the mods hear "opened" and "closed" from their pages.
/// </summary>
public static class SettingsMenu
{
    private static readonly SettingsWindow Window = new();
    private static List<FoundMod> _mods = new();
    private static Key _key = Key.F9;

    public static bool IsOpen { get; private set; }
    internal static int OpenedFrame = -1, ClosedFrame = -1;
    internal static Func<string> KeySetting = () => "F9";

    /// <summary>The menu's own key, as shown ("F9").</summary>
    internal static string KeyLabel => _key.ToString();

    /// <summary>Opens the menu (on this mod's settings, when it's one of those found). True when it's open.</summary>
    public static bool Open(string? modGuid)
    {
        try
        {
            if (IsOpen) { Window.Show(modGuid); return true; }
            var (title, body) = UiKit.PickFonts();
            Window.Build(title, body);
            _mods = Discovery.Find();
            IsOpen = true;
            OpenedFrame = Time.frameCount;
            MenuButton.HoldSelection(true);
            Window.Open(_mods, modGuid);
            foreach (var m in _mods) Call(m, m.Page.Opened, "opened");
            return true;
        }
        catch (Exception e)
        {
            Log.Warn("The settings menu couldn't open: " + e);
            IsOpen = false;
            return false;
        }
    }

    public static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        ClosedFrame = Time.frameCount;
        try { Window.Close(); } catch (Exception e) { Log.Debug("closing: " + e.Message); }
        Lock.Release();
        MenuButton.HoldSelection(false);
        foreach (var m in _mods) Call(m, m.Page.Closed, "closed");
    }

    private static void Call(FoundMod m, Action? a, string what)
    {
        if (a == null) return;
        try { a(); } catch (Exception e) { Log.Warn($"{m.Name} on the menu {what}: {e.Message}"); }
    }

    /// <summary>The menu's own key, read from its settings (a letter, digit or Space would close it while typing: F9 then).</summary>
    internal static void ParseKey()
    {
        string k = (KeySetting() ?? "F9").Trim();
        if (k.Length == 1 && char.IsDigit(k[0])) k = "Digit" + k;
        _key = Enum.TryParse<Key>(k, true, out var parsed) && parsed != Key.None ? parsed : Key.F9;
        if (TypesCharacter(_key) || _key == Key.Escape)
        {
            Log.Warn($"The settings menu's key can't be a letter, digit, Space or Esc ({k}); using F9.");
            _key = Key.F9;
        }
    }

    /// <summary>0.4.2: why a key can't open this menu (a letter, digit, Space or Esc would fire while you type), or null.</summary>
    public static string? OwnKeyProblem(string name)
    {
        string k = (name ?? "").Trim();
        if (k.Length == 1 && char.IsDigit(k[0])) k = "Digit" + k;
        if (!Enum.TryParse<Key>(k, true, out var key) || key == Key.None) return $"\"{name}\" isn't a key the menu can use: try F9, F10 or Insert.";
        return TypesCharacter(key) || key == Key.Escape
            ? $"The menu's key can't be a letter, digit, Space or Esc (it would open while you type): try F9, F10 or Insert."
            : null;
    }

    internal static bool TypesCharacter(Key k) => k is >= Key.A and <= Key.Z || k is >= Key.Digit1 and <= Key.Digit0 || k == Key.Space;

    /// <summary>The menu's own rule for any key setting it catches: never its own key.</summary>
    internal static string? KeyProblem(string name) =>
        Enum.TryParse<Key>(name, true, out var k) && k == _key ? $"{name} opens this menu." : null;

    private static bool KeyPressed()
    {
        try { var kb = Keyboard.current; return kb != null && kb[_key].wasPressedThisFrame; }
        catch { _key = Key.F9; return false; }
    }

    private static int _pauseLayoutWarned;

    /// <summary>Once a frame.</summary>
    internal static void Update()
    {
        try { Pad.Tick(); } catch { }
        if (MenuButton.RefreshSoon) try { MenuButton.Refresh(); } catch (Exception e) { Log.Debug("menu button: " + e.Message); }
        if (KeyPressed() && Time.frameCount != ClosedFrame && Time.frameCount != OpenedFrame)
        {
            if (IsOpen)
            {
                Window.AskClose(); // asks first when a page has unsaved changes
                if (Window.WantsClose) Close();
                return;
            }
            Open(null);
            return;
        }
        if (!IsOpen) return;
        Lock.Ensure();
        if (Time.frameCount == OpenedFrame) return; // the press that opened it doesn't act in it
        try { Window.Tick(); }
        catch (Exception e) { Log.Warn("The settings menu: " + e.Message); }
        if (Window.WantsClose) Close();
    }

    /// <summary>
    /// Once a frame, after the game's own updates: the menu button given a resting button's shape (FB-56), then the pause
    /// menu measured and centred (FB-55), so both see the buttons as the game left them this frame.
    /// </summary>
    internal static void LateUpdate()
    {
        try { MenuButton.LateTick(); } catch (Exception e) { Log.Debug("menu button shape: " + e.Message); }
        try { PauseMenuLayout.Tick(); }
        catch (Exception e)
        {
            // Put back as the game had it and left alone for the session, rather than half done every frame (review).
            PauseMenuLayout.Broken = true;
            try { PauseMenuLayout.Restore(); } catch { }
            if (_pauseLayoutWarned++ == 0) Log.Warn("Centring the pause menu failed; it's left as the game has it: " + e.Message);
        }
    }

    internal static void Stop()
    {
        try { Close(); } catch { }
        try { Window.Destroy(); } catch { }
    }
}
