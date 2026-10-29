using System.Text;
using GitPersona.Core.GitConfig;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Models;
using GitPersona.Core.Profiles;
using GitPersona.Core.Ssh;

namespace GitPersona.Core.ConfigFiles;

public enum PlanIssueCode
{
    /// <summary>Warning: the profile has no SSH key yet, so it gets no Host block.</summary>
    MissingSshKey,

    /// <summary>Warning: the configured private key file does not exist.</summary>
    SshKeyNotFound,

    /// <summary>Warning: the user's own ssh config also defines this alias.</summary>
    AliasDefinedOutsideBlock,

    /// <summary>Blocking: <c>~/.gitconfig-&lt;id&gt;</c> exists but was not written by GitPersona.</summary>
    ForeignProfileConfigFile,

    /// <summary>Blocking: GitPersona's markers in the file are duplicated or unmatched.</summary>
    MalformedBlock,

    /// <summary>Blocking: the file is not valid UTF-8 and cannot be edited safely.</summary>
    UnreadableFile,
}

public sealed record PlanIssue(PlanIssueCode Code, string? ProfileId = null, string? Value = null)
{
    public bool IsBlocking => Code is PlanIssueCode.ForeignProfileConfigFile or PlanIssueCode.MalformedBlock or PlanIssueCode.UnreadableFile;
}

/// <summary>What applying the profiles would change. Nothing is applied while <see cref="IsBlocked"/>.</summary>
public sealed record ConfigPlan(ConfigChangeSet Changes, IReadOnlyList<PlanIssue> Issues)
{
    public bool IsBlocked => Issues.Any(issue => issue.IsBlocking);

    public bool HasChanges => Changes.HasChanges;
}

/// <summary>Computes the desired state of every file GitPersona manages from the saved profiles.</summary>
public sealed class ConfigPlanner(UserPaths paths)
{
    public ConfigPlan Plan(IReadOnlyList<Profile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var changes = new List<ConfigChange>();
        var issues = new List<PlanIssue>();

        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.SshKeyPath))
            {
                issues.Add(new PlanIssue(PlanIssueCode.MissingSshKey, profile.Id));
            }
            else if (!File.Exists(profile.SshKeyPath))
            {
                issues.Add(new PlanIssue(PlanIssueCode.SshKeyNotFound, profile.Id, profile.SshKeyPath));
            }
        }

        var sshConfig = PlanBlock(paths.SshConfigFile, SshConfigBuilder.Block(profiles), BlockPlacement.Top, ConfigFileRole.SshConfig, issues);
        if (sshConfig is not null)
        {
            changes.Add(sshConfig);
            if (sshConfig.Original is { } original)
            {
                var aliases = profiles.Select(p => p.HostAlias).Where(a => !string.IsNullOrWhiteSpace(a));
                issues.AddRange(SshConfigBuilder.AliasesDefinedElsewhere(original.Text, aliases)
                    .Select(alias => new PlanIssue(PlanIssueCode.AliasDefinedOutsideBlock, profiles.First(p => p.HostAlias.Equals(alias, StringComparison.OrdinalIgnoreCase)).Id, alias)));
            }
        }

        var globalGitConfig = PlanBlock(paths.GlobalGitConfigFile, GitConfigBuilder.GlobalBlock(profiles, paths), BlockPlacement.Bottom, ConfigFileRole.GlobalGitConfig, issues);
        if (globalGitConfig is not null)
        {
            changes.Add(globalGitConfig);
        }

        foreach (var profile in profiles)
        {
            if (PlanProfileFile(profile, issues) is { } change)
            {
                changes.Add(change);
            }
        }

        changes.AddRange(PlanStaleProfileFiles(profiles, issues));
        return new ConfigPlan(new ConfigChangeSet(changes), issues);
    }

    private static ConfigChange? PlanBlock(string path, IReadOnlyList<string> body, BlockPlacement placement, ConfigFileRole role, List<PlanIssue> issues)
    {
        if (!TryRead(path, issues, out var original))
        {
            return null;
        }

        try
        {
            var text = original?.Text ?? string.Empty;
            var updatedText = body.Count == 0 ? ManagedBlock.Remove(text) : ManagedBlock.Upsert(text, body, placement);
            if (original is null && body.Count == 0)
            {
                return null;
            }

            var updated = new TextFileContent(updatedText, original?.HasUtf8Bom ?? false);
            return new ConfigChange(path, original, updated, role.ToString()) { Role = role };
        }
        catch (MalformedManagedBlockException)
        {
            issues.Add(new PlanIssue(PlanIssueCode.MalformedBlock, Value: path));
            return null;
        }
    }

    private ConfigChange? PlanProfileFile(Profile profile, List<PlanIssue> issues)
    {
        var path = paths.ProfileGitConfigFile(profile.Id);
        if (!TryRead(path, issues, out var original))
        {
            return null;
        }

        if (original is not null && !GitConfigBuilder.IsManagedProfileFile(original.Text))
        {
            issues.Add(new PlanIssue(PlanIssueCode.ForeignProfileConfigFile, profile.Id, path));
            return null;
        }

        var updated = new TextFileContent(GitConfigBuilder.ProfileFile(profile));
        return new ConfigChange(path, original, updated, $"ProfileGitConfig:{profile.Id}")
        {
            Role = ConfigFileRole.ProfileGitConfig,
            ProfileId = profile.Id,
        };
    }

    /// <summary>Deletes <c>~/.gitconfig-&lt;id&gt;</c> files GitPersona wrote for profiles that no longer exist.</summary>
    private IEnumerable<ConfigChange> PlanStaleProfileFiles(IReadOnlyList<Profile> profiles, List<PlanIssue> issues)
    {
        if (!Directory.Exists(paths.HomeDirectory))
        {
            yield break;
        }

        var current = profiles.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        const string prefix = ".gitconfig-";
        foreach (var file in Directory.EnumerateFiles(paths.HomeDirectory, prefix + "*"))
        {
            var id = Path.GetFileName(file)[prefix.Length..];

            // Skips backups (".gitconfig-work.20261029-153012.bak") and anything else that is not a profile id.
            if (!ProfileValidator.IsValidId(id) || current.Contains(id))
            {
                continue;
            }

            if (TryRead(file, issues, out var original) && original is not null && GitConfigBuilder.IsManagedProfileFile(original.Text))
            {
                yield return new ConfigChange(file, original, null, $"ProfileGitConfig:{id}")
                {
                    Role = ConfigFileRole.ProfileGitConfig,
                    ProfileId = id,
                };
            }
        }
    }

    private static bool TryRead(string path, List<PlanIssue> issues, out TextFileContent? content)
    {
        try
        {
            content = TextFile.ReadIfExists(path);
            return true;
        }
        catch (DecoderFallbackException)
        {
            issues.Add(new PlanIssue(PlanIssueCode.UnreadableFile, Value: path));
            content = null;
            return false;
        }
    }
}
