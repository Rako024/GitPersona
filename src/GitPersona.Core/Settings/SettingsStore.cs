using GitPersona.Core.Infrastructure;
using GitPersona.Core.Models;
using Microsoft.Extensions.Logging;

namespace GitPersona.Core.Settings;

/// <summary>Persists <see cref="AppSettings"/> in <c>%APPDATA%\GitPersona\settings.json</c>.</summary>
public sealed class SettingsStore(AppPaths paths, ILogger<SettingsStore> logger)
{
    public AppSettings Load()
    {
        var result = JsonFileStore.Read<AppSettings>(paths.SettingsFile);
        if (result.QuarantinedPath is not null)
        {
            logger.LogWarning("settings.json could not be parsed and was moved to {Path}", result.QuarantinedPath);
        }

        return result.Value ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        JsonFileStore.Write(paths.SettingsFile, settings);
    }
}
