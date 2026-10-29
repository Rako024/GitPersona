using GitPersona.Core.Infrastructure;

namespace GitPersona.Core.ConfigFiles;

/// <summary>A pending rewrite of one file, shown to the user as a diff before it is applied.</summary>
/// <param name="Original">Content when the change was computed; <see langword="null"/> if the file did not exist.</param>
/// <param name="Updated">Content to write; <see langword="null"/> deletes the file.</param>
public sealed record ConfigChange(string FilePath, TextFileContent? Original, TextFileContent? Updated, string Description)
{
    public bool HasChanges => Original != Updated;

    public ConfigChangeKind Kind => (Original, Updated) switch
    {
        (null, not null) => ConfigChangeKind.Create,
        (not null, null) => ConfigChangeKind.Delete,
        _ => ConfigChangeKind.Modify,
    };
}

public enum ConfigChangeKind
{
    Create,
    Modify,
    Delete,
}

public sealed record ConfigChangeSet(IReadOnlyList<ConfigChange> Changes)
{
    public IReadOnlyList<ConfigChange> Pending => Changes.Where(change => change.HasChanges).ToList();

    public bool HasChanges => Changes.Any(change => change.HasChanges);
}

/// <param name="BackupPath">Copy of the previous content, or <see langword="null"/> for a newly created file.</param>
public sealed record AppliedChange(string FilePath, ConfigChangeKind Kind, string? BackupPath);
