using GitPersona.Core.Infrastructure;
using GitPersona.Core.Models;
using Microsoft.Extensions.Logging;

namespace GitPersona.Core.Profiles;

/// <summary>Persists profiles in <c>%APPDATA%\GitPersona\profiles.json</c>. Holds no secrets.</summary>
public sealed class ProfileStore(AppPaths paths, ILogger<ProfileStore> logger)
{
    /// <summary>Set when the last <see cref="Load"/> found an unreadable file and moved it aside.</summary>
    public string? QuarantinedFile { get; private set; }

    public IReadOnlyList<Profile> Load()
    {
        var result = JsonFileStore.Read<ProfilesDocument>(paths.ProfilesFile);
        QuarantinedFile = result.QuarantinedPath;
        if (result.QuarantinedPath is not null)
        {
            logger.LogWarning("profiles.json could not be parsed and was moved to {Path}", result.QuarantinedPath);
        }

        return result.Value?.Profiles ?? [];
    }

    /// <exception cref="ProfileValidationException">Nothing was saved.</exception>
    public void Save(IReadOnlyList<Profile> profiles)
    {
        var issues = ProfileValidator.Validate(profiles);
        if (issues.Count > 0)
        {
            throw new ProfileValidationException(issues);
        }

        JsonFileStore.Write(paths.ProfilesFile, new ProfilesDocument { Profiles = profiles });
        logger.LogInformation("Saved {Count} profile(s)", profiles.Count);
    }

    private sealed record ProfilesDocument
    {
        public int Version { get; init; } = 1;

        public IReadOnlyList<Profile> Profiles { get; init; } = [];
    }
}
