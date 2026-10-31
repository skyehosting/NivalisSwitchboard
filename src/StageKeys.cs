using System.Collections.Generic;

namespace NivalisSwitchboard;

/// <summary>0.4.0: who a row is, kept across a page's rebuild, so a change waiting for Save stays on its own row.</summary>
internal static class StageKeys
{
    /// <summary>One row of a page, in order: its setting (section/key) if it has one, the page's id for it, its title.</summary>
    public readonly record struct Who(string? Setting, string? Id, string Title);

    /// <summary>Its setting, its id, or else its title, its place among the rows with that title and how many there are:
    /// when rows sharing a title come or go, those keys all change, so a waiting change is dropped rather than put on another row.</summary>
    public static string For(IReadOnlyList<Who> rows, int index)
    {
        var row = rows[index];
        if (row.Setting != null) return "s:" + row.Setting;
        if (!string.IsNullOrEmpty(row.Id)) return "i:" + row.Id;
        int same = 0, all = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Setting != null || !string.IsNullOrEmpty(r.Id) || r.Title != row.Title) continue;
            all++;
            if (i < index) same++;
        }
        return "c:" + row.Title + "#" + same + "/" + all;
    }

    /// <summary>Keys more than one row has (a page giving two rows one id): a waiting change is never put on either.</summary>
    public static HashSet<string> Shared(IReadOnlyList<Who> rows)
    {
        var seen = new HashSet<string>();
        var shared = new HashSet<string>();
        for (int i = 0; i < rows.Count; i++)
            if (!seen.Add(For(rows, i))) shared.Add(For(rows, i));
        return shared;
    }
}
