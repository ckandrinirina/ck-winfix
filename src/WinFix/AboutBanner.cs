using System.Reflection;

namespace WinFix;

/// <summary>The author line every program prints first; shared with WinFixUsb.</summary>
public static class AboutBanner
{
    public static string Line(string product, string version) =>
        $"{product} {version} — by ANDRINIRINA Erick · github.com/ckandrinirina";

    public static void Write(TextWriter output, string product, Assembly assembly) =>
        output.WriteLine(Line(product, VersionOf(assembly)));

    public static string VersionOf(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return StripCommit(informational ?? assembly.GetName().Version?.ToString() ?? "0.0.0");
    }

    /// <summary>SourceLink appends "+commit" to the informational version.</summary>
    public static string StripCommit(string version)
    {
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }
}
