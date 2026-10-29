using GitPersona.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace GitPersona.Core.ConfigFiles;

/// <summary>The file changed on disk after the diff was shown, so the previewed change is no longer accurate.</summary>
public sealed class ConfigConflictException(string filePath)
    : Exception($"'{filePath}' was modified after the preview was created. Review the changes again.")
{
    public string FilePath { get; } = filePath;
}

/// <summary>Writing stopped part way; <see cref="Applied"/> lists the files that were already written.</summary>
public sealed class ConfigApplyException(string filePath, IReadOnlyList<AppliedChange> applied, Exception innerException)
    : Exception($"Could not write '{filePath}': {innerException.Message}", innerException)
{
    public string FilePath { get; } = filePath;

    public IReadOnlyList<AppliedChange> Applied { get; } = applied;
}

/// <summary>The only code path that writes to the user's configuration files.</summary>
public sealed class ConfigWriter(BackupManager backups, ILogger<ConfigWriter> logger)
{
    /// <summary>
    /// Verifies every file still matches its previewed original, then backs up and writes each one.
    /// </summary>
    /// <exception cref="ConfigConflictException">Nothing was written.</exception>
    /// <exception cref="ConfigApplyException">Some files may have been written; see <see cref="ConfigApplyException.Applied"/>.</exception>
    public IReadOnlyList<AppliedChange> Apply(ConfigChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        var pending = changeSet.Pending;

        foreach (var change in pending)
        {
            if (TextFile.ReadIfExists(change.FilePath) != change.Original)
            {
                throw new ConfigConflictException(change.FilePath);
            }
        }

        var applied = new List<AppliedChange>();
        foreach (var change in pending)
        {
            try
            {
                var backupPath = backups.Backup(change.FilePath);
                if (change.Updated is null)
                {
                    File.Delete(change.FilePath);
                }
                else
                {
                    AtomicFileWriter.Write(change.FilePath, TextFile.Encode(change.Updated));
                }

                applied.Add(new AppliedChange(change.FilePath, change.Kind, backupPath));
                logger.LogInformation(
                    "{Kind} {File} (backup: {Backup})",
                    change.Kind,
                    change.FilePath,
                    backupPath ?? "none");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Failed to write {File}", change.FilePath);
                throw new ConfigApplyException(change.FilePath, applied, ex);
            }
        }

        return applied;
    }
}
