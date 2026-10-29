namespace GitPersona.Core.Models;

/// <summary>One GitHub identity: who commits, which key pushes, and where it applies.</summary>
public sealed record Profile
{
    public const string DefaultHost = "github.com";

    /// <summary>Slug (<c>work</c>) used to derive file names and the default host alias.</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public string GitUserName { get; init; } = string.Empty;

    public string GitUserEmail { get; init; } = string.Empty;

    public string GitHubUsername { get; init; } = string.Empty;

    /// <summary>Real SSH host; <c>github.com</c> unless the profile targets GitHub Enterprise.</summary>
    public string Host { get; init; } = DefaultHost;

    /// <summary>SSH alias used in remote URLs, e.g. <c>git@github-work:owner/repo.git</c>.</summary>
    public string HostAlias { get; init; } = string.Empty;

    public string SshKeyPath { get; init; } = string.Empty;

    /// <summary>Root folders whose repositories use this profile.</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];

    /// <summary>GitHub owners/organizations whose repositories are cloned with this profile.</summary>
    public IReadOnlyList<string> Owners { get; init; } = [];

    /// <summary>Where new clones go; falls back to the first of <see cref="Folders"/>.</summary>
    public string? DefaultCloneFolder { get; init; }

    /// <summary>Accent colour as <c>#RRGGBB</c>.</summary>
    public string Color { get; init; } = "#3B82F6";

    /// <summary>Name of a Fluent System icon (WPF-UI <c>SymbolRegular</c>).</summary>
    public string Icon { get; init; } = "Person24";

    public static string DefaultAliasFor(string profileId) => $"github-{profileId}";
}
