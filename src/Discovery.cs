using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace NivalisSwitchboard;

/// <summary>One of the author's mods the menu found: its config, its settings in order, and its page (if it has one).</summary>
internal sealed class FoundMod
{
    public string Guid = "", Name = "", Version = "";
    public ConfigFile Config = null!;
    public ModPage Page = new();
    public List<ConfigEntryBase> Entries = new();

    /// <summary>The groups in the page's order (its intros too), then any other section of the config, in the order bound.</summary>
    public List<(string name, string intro)> Groups()
    {
        var sections = Entries.Select(e => e.Definition.Section).Distinct().ToList();
        var list = Page.Groups.Where(g => sections.Contains(g.name) || HasRows(g.name)).ToList();
        foreach (var s in sections) if (!list.Any(g => g.name == s)) list.Add((s, ""));
        return list;
    }

    private bool HasRows(string group) => Page.Rows != null && Page.Groups.Any(g => g.name == group);

    /// <summary>The category a group is listed under in the tree (null: none).</summary>
    public string? CategoryOf(string group) => Page.Categories.TryGetValue(group, out var c) ? c : null;
}

/// <summary>
/// Finds the author's mods among the plugins BepInEx loaded: those whose DLL carries the marker (see CONTRACT.md). Read again
/// each time the menu opens, so a mod's own changes to its page show.
/// </summary>
internal static class Discovery
{
    private static readonly HashSet<string> Said = new();

    public static List<FoundMod> Find()
    {
        var found = new List<FoundMod>();
        Dictionary<string, BepInEx.PluginInfo>? plugins = null;
        try { plugins = IL2CPPChainloader.Instance?.Plugins; } catch (Exception e) { Log.Warn("The list of loaded mods couldn't be read: " + e.Message); }
        if (plugins == null) return found;
        foreach (var info in plugins.Values.ToList())
        {
            try
            {
                if (info?.Instance is not BasePlugin plugin) continue;
                var asm = plugin.GetType().Assembly;
                if (!Marker.Has(asm)) continue;
                var mod = new FoundMod
                {
                    Guid = info.Metadata.GUID, Name = info.Metadata.Name, Version = info.Metadata.Version?.ToString() ?? "",
                    Config = plugin.Config,
                };
                mod.Entries = plugin.Config.Select(kv => kv.Value).Where(e => e != null).ToList();
                mod.Page = ReadPage(asm, mod.Guid);
                if (mod.Page.Name is { Length: > 0 } name) mod.Name = name;
                found.Add(mod);
            }
            catch (Exception e) { Log.Warn($"Reading {info?.Metadata?.Name ?? "a mod"}'s settings failed: {e.Message}"); }
        }
        return found;
    }

    private static ModPage ReadPage(Assembly asm, string guid)
    {
        Type? type = null;
        try { type = asm.GetTypes().FirstOrDefault(t => t.Name == "SettingsMenuPage" && t.IsAbstract && t.IsSealed); }
        catch (ReflectionTypeLoadException e) { type = e.Types.FirstOrDefault(t => t != null && t.Name == "SettingsMenuPage"); }
        catch { }
        var describe = type?.GetMethod("Describe", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        if (describe == null) return new ModPage();
        ModPage page;
        try { page = Page.Read(describe.Invoke(null, null)); }
        catch (Exception e) { page = new ModPage(); page.Problems.Add("Describe() failed: " + (e.InnerException ?? e).Message); }
        foreach (var p in page.Problems)
            if (Said.Add(guid + p)) Log.Warn($"{guid}'s settings page: {p} (the plain control is used).");
        return page;
    }

    /// <summary>What BepInEx itself says of a setting: its range or list of values, and ConfigurationManagerAttributes' hints.</summary>
    public static (double? min, double? max, IReadOnlyList<string>? values, bool hidden, string? name, int order) Meta(ConfigEntryBase e)
    {
        double? min = null, max = null;
        IReadOnlyList<string>? values = null;
        bool hidden = false;
        string? name = null;
        int order = 0;
        try
        {
            var av = e.Description?.AcceptableValues;
            if (av != null)
            {
                var t = av.GetType();
                if (t.Name.StartsWith("AcceptableValueRange", StringComparison.Ordinal))
                {
                    min = Convert.ToDouble(t.GetProperty("MinValue")?.GetValue(av), CultureInfo.InvariantCulture);
                    max = Convert.ToDouble(t.GetProperty("MaxValue")?.GetValue(av), CultureInfo.InvariantCulture);
                }
                else if (t.Name.StartsWith("AcceptableValueList", StringComparison.Ordinal) && t.GetProperty("AcceptableValues")?.GetValue(av) is IEnumerable list)
                    values = list.Cast<object?>().Select(x => Convert.ToString(x, CultureInfo.InvariantCulture) ?? "").ToList();
            }
            foreach (var tag in e.Description?.Tags ?? Array.Empty<object>())
            {
                if (tag == null || tag.GetType().Name != "ConfigurationManagerAttributes") continue;
                var tt = tag.GetType();
                if (tt.GetField("Browsable")?.GetValue(tag) is false) hidden = true;
                if (tt.GetField("DispName")?.GetValue(tag) is string dn && dn.Length > 0) name = dn;
                if (tt.GetField("Order")?.GetValue(tag) is int o) order = o;
            }
        }
        catch { }
        return (min, max, values, hidden, name, order);
    }
}
