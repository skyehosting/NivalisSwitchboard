# How a mod appears in Nivalis Switchboard

For the author's own mods. Neither the mod nor the menu references the other: a mod works without the menu, and the menu
finds the mod by itself.

## 1. The marker (required)

One line anywhere in the mod (e.g. at the top of Plugin.cs):

```csharp
[assembly: System.Reflection.AssemblyMetadata("NivalisSettingsMenu", "1")]
```

The menu looks through the plugins BepInEx loaded and lists every one whose DLL carries this. Its tab tree shows the
mod's name, then each section of its BepInEx config as a group, each setting with a control made from what BepInEx
knows: on/off = a switch, an enum or `AcceptableValueList` = a dropdown, `AcceptableValueRange` = a bar, other numbers
and text = fields. A setting whose name holds Key, Password, Secret or Token is shown only as "set / not set".
`ConfigurationManagerAttributes` in a setting's tags are read too (Browsable = false hides it, DispName names it, Order
sorts it). A change is applied at once (`BoxedValue`), and saved with `ConfigFile.Save()` unless the mod says it saves
itself.

## 2. A page (optional: for anything more)

A `public static class SettingsMenuPage` in the mod (any namespace), with:

```csharp
public static IDictionary<string, object?> Describe()
```

Read once each time the menu opens. Every key is optional:

| key | type | what |
|---|---|---|
| `name` | string | the name shown (default: the plugin's name) |
| `savesItself` | bool | the mod writes its own file on SettingChanged (People of Nivalis does) |
| `listHidden` | bool | 0.5.5: list the mod's settings tagged `Browsable = false` (hidden from other settings menus) all the same; without it, only those the page gives a hint with a `kind` are listed. Use it with `savesItself`: otherwise the menu saves the file through BepInEx, the very thing the tag keeps other menus from doing |
| `groups` | list of `{ name, intro }` | the groups in order, with a line about each |
| `settings` | dictionary `"Section/Key"` → hint | how a setting is shown (below) |
| `rows` | `Func<string, IEnumerable>` | for a group's name, extra rows that aren't settings (below) |
| `opened`, `closed` | `Action` | the menu opened / closed (People of Nivalis hides its chat meanwhile) |
| `changed` | `Action<string>` | a setting of the mod changed in the menu (its key) |

A **hint**: `kind` (switch, slider, choice, text, number, secret, key, hidden), `min`, `max`, `step`, `unit`
("%", "m", "s"), `choices` (list of strings), `title`, `hint`, and for a key `validate`: `Func<string, string?>` (the key's
name → why it can't be used, or null).

A **row**: the hint's fields plus `before` / `after` (a setting's key in that group: where the row goes), `help`,
`type` ("bool", "double", "string"), `get`: `Func<object?>`, `set`: `Func<object, string?>` (null when saved, or why not),
`click`: `Func<string?>` (a button; its text is `label`), `preview`: `Action<object?>` (a bar moving: its value; null when
it stops or isn't looked at), `seeThrough` (the window fades while that bar moves), `pickOnly` (a dropdown whose choice
does something), `rebuild` (rows come or go after a change: asked for again).

Also on a row: kind `section` (a heading starting a part of the page; the highlight passes over it), kind `meter`
(`meter`: `Func<double[]>` giving the used, pace and projected fractions of its bar, and `get` its words), `pinned`:
`Func<bool>` and `pin`: `Action<bool>` (a pin at its right end; Y on a controller), `badge`: `Func<string?>` (a short amber
tag after its name, like "changed") with `badgeHelp`: `Func<string?>` (the footer's sentence for it), and `helpLive`:
`Func<string?>` (its footer text worked out each time it's shown), `hintLive`: `Func<string?>` (the line under its name,
worked out each time the rows refresh). A setting's hint takes `badge` and `badgeHelp` too.

Any row can have `visible`: `Func<bool>` (shown only while it says so; the rows close up around a hidden one).

### Guided pages (0.3.0)

| kind | what | fields |
|---|---|---|
| `tiles` | a status strip: equal tiles, three to a line | `tiles`: `Func<IEnumerable>` of dictionaries with `title`, `value` (shown right after the meter), `caption`, `meter` (`double[]` used, pace, projected; a pace below 0 hides the tick), `warn` (bool: amber) |
| `step` | a numbered step: the number on a rail, a gold title, a line, a chip | `number`, `title`, `hint` (or `hintLive`), `chip` and `chipTone` (`Func<string?>`: "ok" green, "warn" amber, else grey), `state` (`Func<string?>`: "done" fills the number, "now" lights it) |
| `subsection` | a small heading inside a step, a line above it | `title` |
| `changes` | the captions over a list of changes | `title`, `fromLabel`, `toLabel`, `endLabel` |
| `change` | one change: the setting, now -> then, a pin | `title`, `from`, `to`, `toTone` (`Func<string?>`: "kept" amber, "same" grey), `pinned`/`pin`, `badge`; 0.5.4: `step` (`Func<string?, int, string?>`: the "now" value and -1/+1 -> the next value, null at its end) with `set` (`Func<object, string?>`, given that value as a string): its value is stepped right there, with - and + beside it or left/right on a controller (at once, even on a page with Save; `rebuild` builds the rows again after it) |
| `buttons` | a row of buttons, right-aligned to where the controls end | `buttons`: list of `{ label, labelLive, click, main (gold, put it last), rebuild, visible }` |

### Save, categories and notes (0.4.0)

Changes wait for Save: a row's edit is shown with an amber UNSAVED tag, and a bar under the rows counts them, with
Discard and Save (Start on a pad saves). Save writes each one the way a change was written before (`set`, or the setting
and `changed`). Leaving the page or closing the menu with changes waiting asks first (Keep editing, Discard, Save).
Buttons, pins and dropdowns that `pickOnly` act at once, and so does a row with `immediate: true` (a what-if control).
A page that wants every change saved at once says `saveButton: false`. A group may name a `category` in `groups`: the tree
lists it under a folding heading of that name (folded until opened or until one of its groups is shown). A `buttons` row
may have a `hint` (or `hintLive`): a short note on its left ("Sends one short request: a few tokens").
A change waiting for Save stays on its row when the page's rows are asked for again (`rebuild`). A row bound to a setting is
found by its setting; give any other row an `id` (a string, the same every time) when it shares its title with another
row ("Chance" on each voice provider). Without one, a waiting change on a row whose title is shared is let go (the footer
says so) when rows with that title come or go, rather than put on another row. Two rows with one `id` share it: a waiting
change is put on neither.
0.4.2: a button may have `enabled` (`Func<bool>`): greyed out and not pressable while it says false. Typed numbers and text
wait for Save as they're typed; a key waits until its field is left.
0.4.1: a row may have `fills`, a function given the value just chosen that returns a dictionary of other rows to change
with it (each named by its `id`, or by a setting's "Section/Key") and their new values, or null. Those rows change at once and
wait for Save with it (a provider picked: its address and model). On a page without Save, nothing is filled in: the row's
own `set` does what it does.

Every row follows the same two edges: each control (bar, field, dropdown, switch, a row of buttons) ends on the control
edge, and the narrow column after it holds only what belongs to a control: a bar's value, a tile's %, a pin. Rows after the
first `step` are indented past its rail. Buttons dip and flash when pressed; one that rebuilds the rows waits for it.

Only plain .NET types and System delegates cross between the two mods. A page the menu can't read is noted in the log
and the plain controls are used.

## 3. Opening the menu from a mod (optional)

```csharp
// Among the loaded DLLs (BepInEx loads plugins from their paths, so Type.GetType by name alone may not find it).
var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "NivalisSwitchboard");
var api = asm?.GetType("NivalisSwitchboard.SettingsMenu");
api?.GetMethod("Open")?.Invoke(null, new object?[] { "YourPluginGuid" }); // true when it opened
```

`IsOpen` (a static bool property) says whether it's up. When the menu isn't installed, `api` is null.
