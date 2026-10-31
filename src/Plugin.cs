using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

// The menu lists itself too (its key and its button): the same marker the author's mods carry.
[assembly: System.Reflection.AssemblyMetadata("NivalisSettingsMenu", "1")]

namespace NivalisSwitchboard;

[BepInPlugin(Guid, Name, Version)]
[BepInProcess("Nivalis Nights.exe")]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "NivalisSwitchboard"; // its settings file: NivalisSwitchboard.cfg
    public const string Name = "Switchboard"; // the author: "re name it to Switchboard in the settings"
    public const string Version = "0.5.5";

    private Harmony? _harmony;

    public override void Load()
    {
        NivalisSwitchboard.Log.Source = Log;
        var key = Config.Bind("Menu", "Key", "F9", "Opens and closes the settings menu. Examples: F9, F10, Insert.");
        var button = Config.Bind("Menu", "MenuButton", true,
            "A button for the menu on the game's title menu and pause menu (under Settings), so a controller can open it.");
        var label = Config.Bind("Menu", "MenuButtonLabel", "Switchboard", "What that button says.");
        var centre = Config.Bind("Menu", "CenterPauseMenu", true,
            "Centres the pause menu's buttons on the screen. When there are more than fit, the mouse wheel scrolls them, fading toward the edges.");
        PauseMenuLayout.Enabled = () => centre.Value;
        SettingsMenu.KeySetting = () => key.Value;
        MenuButton.Enabled = () => button.Value;
        MenuButton.Label = () => string.IsNullOrWhiteSpace(label.Value) ? "Switchboard" : label.Value.Trim();
        SettingsMenu.ParseKey();
        Config.SettingChanged += (_, _) => { SettingsMenu.ParseKey(); MenuButton.RefreshSoon = true; }; // the button on the game's thread (a reload of the file may come from another)

        _harmony = new Harmony(Guid);
        // One patch at a time: if a game update renames a method, only that hook is lost.
        foreach (var t in typeof(Plugin).Assembly.GetTypes())
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try { _harmony.CreateClassProcessor(t).Patch(); }
            catch (Exception e) { Log.LogWarning($"Hook {t.Name} unavailable ({e.GetBaseException().Message})."); }
        }
        AddComponent<MenuPump>();
        Log.LogInfo($"{Name} {Version} ready: {SettingsMenu.KeyLabel} opens it.");
    }

    public override bool Unload()
    {
        SettingsMenu.Stop();
        PauseMenuLayout.Restore();
        try { _harmony?.UnpatchSelf(); } catch { }
        return true;
    }
}

/// <summary>Unity calls into the menu through this one injected behaviour.</summary>
public sealed class MenuPump : MonoBehaviour
{
    public MenuPump(IntPtr ptr) : base(ptr) { }

    public void Update() => SettingsMenu.Update();

    /// <summary>After the game's own updates (its buttons' animations): the menu button's shape, then the pause menu's place.</summary>
    public void LateUpdate() => SettingsMenu.LateUpdate();
}
