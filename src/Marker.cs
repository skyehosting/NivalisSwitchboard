using System.Linq;
using System.Reflection;

namespace NivalisSwitchboard;

/// <summary>The marker a mod's DLL carries to be listed: <c>[assembly: AssemblyMetadata("NivalisSettingsMenu", "1")]</c>. No Unity.</summary>
public static class Marker
{
    public const string Key = "NivalisSettingsMenu";

    public static bool Has(Assembly asm)
    {
        try { return asm.GetCustomAttributes<AssemblyMetadataAttribute>().Any(a => a.Key == Key); }
        catch { return false; }
    }
}
