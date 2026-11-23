using System.Reflection;

namespace WCS.Application.Common;

/// <summary>
/// Application version, Git commit and build time of the running binary. They are stamped into the host assembly (WCS.Api) at BUILD time from the repository's VERSION file and the
/// build arguments (see WCS.Api.csproj, deployment/docker/backend/Dockerfile); nothing is read from the working tree while the application runs. Missing metadata reads "unknown".
/// </summary>
public sealed record BuildInfo(string Version, string GitCommit, string BuildTime)
{
    public const string Unknown = "unknown";

    public static BuildInfo Current { get; } = Read(Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly);

    public static BuildInfo Read(Assembly asm)
    {
        string Meta(string key) => asm.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } v ? v : Unknown;
        return new BuildInfo(Meta("AppVersion"), Meta("GitCommit"), Meta("BuildTime"));
    }
}
