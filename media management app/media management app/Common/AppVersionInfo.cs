using System.Reflection;

namespace media_management_app.Common;

/// <summary>
/// Build version stamped by MinVer from the git tag, e.g. <c>1.2.0+abc1234</c> for a release or
/// <c>1.2.1-alpha.0.3+abc1234</c> for a dev build. Reads the entry assembly attribute, so it works in single-file publish.
/// </summary>
public static class AppVersionInfo
{
    public static string Informational { get; } =
        (Assembly.GetEntryAssembly() ?? typeof(AppVersionInfo).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}
