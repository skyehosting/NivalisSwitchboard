using System;
using System.Collections.Generic;
using Nivalis;

namespace NivalisSwitchboard;

/// <summary>
/// Holds the game still while the menu is up, with the game's own override locks (movement, camera, interactions, menu
/// shortcuts, saving) and the UI cursor. Only when nothing else has the cursor up already (the game's pause menu, a chat
/// of People of Nivalis): then that one holds the game, and the menu takes nothing. Whatever it took, it gives back.
/// </summary>
internal static class Lock
{
    private static readonly List<OverrideableBool.OverrideLock> Locks = new();
    private static readonly Il2CppSystem.Object Owner = new();
    private static bool _cursor;

    /// <summary>The menu holds the UI cursor: the game's own releases are ignored meanwhile.</summary>
    internal static bool HoldingCursor;
    private static bool _ownRelease;

    public static bool Engaged { get; private set; }

    private static bool CursorUp()
    {
        try { return CursorModeManager.IsCursorActive; } catch { return true; }
    }

    /// <summary>Each frame the menu is up: takes the game if nothing else holds it (any more).</summary>
    public static void Ensure()
    {
        if (Engaged) { KeepCursor(); return; }
        if (CursorUp()) return;
        Engaged = true;
        try
        {
            var pm = Singleton<PlayerManager>._instance; // never the Instance property: that makes one
            var pc = pm != null && !pm.WasCollected ? pm.LocalPlayer?.Character : null;
            Try(() => pc?.Controller?.DisableMovement(Owner));
            Try(() => pc?.Controller?.LockCamera(Owner));
            Try(() => pc?.Interaction?.DisableInteractions(Owner));
        }
        catch (Exception e) { Log.Debug("player locks: " + e.Message); }
        try
        {
            var input = Singleton<PlayerInputManager>._instance;
            if (input != null && input.WasCollected) input = null;
            Try(() => input?.PreventAllMenuShortcuts?.Override(Owner));
            Try(() => input?.PreventSaving?.Override(Owner));
        }
        catch (Exception e) { Log.Debug("menu locks: " + e.Message); }
        try { HoldingCursor = true; CursorModeManager.RequestUiCursor(); _cursor = true; }
        catch (Exception e) { Log.Debug("cursor: " + e.Message); }
    }

    private static void KeepCursor()
    {
        try { if (_cursor && !CursorModeManager.IsCursorActive) CursorModeManager.RequestUiCursor(); } catch { }
    }

    public static void Release()
    {
        if (!Engaged) return;
        Engaged = false;
        foreach (var l in Locks)
        {
            try { if (l != null && !l.IsReleased) l.Release(); }
            catch (Exception e) { Log.Debug("release lock: " + e.Message); }
        }
        Locks.Clear();
        HoldingCursor = false;
        if (_cursor)
        {
            _ownRelease = true;
            try { CursorModeManager.ReleaseUiCursor(); } catch (Exception e) { Log.Debug("cursor release: " + e.Message); }
            finally { _ownRelease = false; }
            _cursor = false;
        }
    }

    private static void Try(Func<OverrideableBool.OverrideLock?> take)
    {
        try { var l = take(); if (l != null) Locks.Add(l); }
        catch (Exception e) { Log.Debug("lock: " + e.Message); }
    }

    /// <summary>The game drops the UI cursor when the input device changes; while the menu holds it, that's ignored.</summary>
    [HarmonyLib.HarmonyPatch(typeof(CursorModeManager), nameof(CursorModeManager.ReleaseUiCursor))]
    private static class KeepUiCursor
    {
        private static bool Prefix() => !HoldingCursor || _ownRelease;
    }

    // While the menu is up, the game's own shortcuts (scanner, journal, map, inventory, shopping list) don't fire on the
    // d-pad or the keys typed into a field. TabRightListener is left alone: its native body is shared.
    [HarmonyLib.HarmonyPatch(typeof(PlayerInputManager), nameof(PlayerInputManager.ScanListener))]
    private static class NoScan { private static bool Prefix() => !SettingsMenu.IsOpen; }

    [HarmonyLib.HarmonyPatch(typeof(PlayerInputManager), nameof(PlayerInputManager.JournalShortcutListener))]
    private static class NoJournal { private static bool Prefix() => !SettingsMenu.IsOpen; }

    [HarmonyLib.HarmonyPatch(typeof(PlayerInputManager), nameof(PlayerInputManager.MapToggleListener))]
    private static class NoMap { private static bool Prefix() => !SettingsMenu.IsOpen; }

    [HarmonyLib.HarmonyPatch(typeof(PlayerInputManager), nameof(PlayerInputManager.InventoryShortcutListener))]
    private static class NoInventory { private static bool Prefix() => !SettingsMenu.IsOpen; }

    [HarmonyLib.HarmonyPatch(typeof(PlayerInputManager), nameof(PlayerInputManager.ShoppingListShortcutListener))]
    private static class NoShoppingList { private static bool Prefix() => !SettingsMenu.IsOpen; }

    [HarmonyLib.HarmonyPatch(typeof(PlayerInputManager), nameof(PlayerInputManager.ShowMoreShortcutListener))]
    private static class NoShowMore { private static bool Prefix() => !SettingsMenu.IsOpen; }

    [HarmonyLib.HarmonyPatch(typeof(PlayerInputManager), nameof(PlayerInputManager.TabLeftListener))]
    private static class NoTabLeft { private static bool Prefix() => !SettingsMenu.IsOpen; }
}
