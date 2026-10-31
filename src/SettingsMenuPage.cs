using System;
using System.Collections.Generic;

namespace NivalisSwitchboard;

/// <summary>
/// The menu's own page, read like any mod's (0.4.2): its Key is a key you press (it was shown as a secret, since its name has
/// "key" in it), refused with the reason when it would fire while you type.
/// </summary>
public static class SettingsMenuPage
{
    public static object Describe() => new Dictionary<string, object?>
    {
        ["settings"] = new Dictionary<string, object?>
        {
            ["Menu/Key"] = new Dictionary<string, object?>
            {
                ["kind"] = "key",
                ["title"] = "Key",
                ["hint"] = "Opens and closes this menu (press one: F9, F10, Insert...)",
                ["validate"] = (Func<string, string?>)SettingsMenu.OwnKeyProblem,
            },
            ["Menu/MenuButton"] = new Dictionary<string, object?> { ["kind"] = "switch", ["title"] = "Menu button", ["hint"] = "On the title and pause menus, under Settings" },
            ["Menu/MenuButtonLabel"] = new Dictionary<string, object?> { ["kind"] = "text", ["title"] = "Menu button label", ["hint"] = "What that button says" },
            ["Menu/CenterPauseMenu"] = new Dictionary<string, object?> { ["kind"] = "switch", ["title"] = "Center the pause menu", ["hint"] = "Its buttons in the middle of the screen; more than fit scroll with the mouse wheel" },
        },
    };
}
