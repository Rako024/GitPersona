using GitPersona.Core.Infrastructure;
using GitPersona.Core.Models;
using GitPersona.Core.Profiles;
using static GitPersona.Core.GitConfig.GitConfigSyntax;

namespace GitPersona.Core.GitConfig;

/// <summary>Builds GitPersona's part of <c>~/.gitconfig</c> and the per-profile <c>~/.gitconfig-&lt;id&gt;</c> files.</summary>
public static class GitConfigBuilder
{
    /// <summary>First line of every per-profile file; only files starting with it are ever rewritten or deleted.</summary>
    public const string ProfileFileHeader = "# Managed by GitPersona";

    /// <summary>
    /// Lines for the managed block of <c>~/.gitconfig</c>. Git applies includes in order and the last value wins, so:
    /// folder rules come first, shallow before deep (a nested folder's profile wins over its parent's);
    /// remote-alias rules come last (the identity always matches the account whose key pushes).
    /// </summary>
    /// <param name="hooksPath">GitPersona's hooks folder when the commit guard is on; set last so it wins.</param>
    /// <param name="sshCommand">The ssh git should run (Windows OpenSSH, for its agent), if any.</param>
    public static IReadOnlyList<string> GlobalBlock(IReadOnlyList<Profile> profiles, UserPaths paths, string? hooksPath = null, string? sshCommand = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(paths);
        var lines = new List<string>();

        var folderRules = profiles
            .SelectMany(profile => profile.Folders
                .Where(Path.IsPathFullyQualified)
                .Select(folder => (profile, folder: ProfileValidator.NormalizeFolder(folder))))
            .OrderBy(rule => rule.folder.Count(c => c == Path.DirectorySeparatorChar))
            .ThenBy(rule => rule.folder, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (folderRules.Count > 0)
        {
            lines.Add("# Identity by folder (deeper folders win)");
        }

        foreach (var (profile, folder) in folderRules)
        {
            AddInclude(lines, GitDirCondition(folder), profile, paths);
        }

        var aliasProfiles = profiles.Where(p => !string.IsNullOrWhiteSpace(p.HostAlias)).ToList();
        if (aliasProfiles.Count > 0)
        {
            lines.Add("# Identity by remote SSH alias (wins over folders)");
        }

        foreach (var profile in aliasProfiles)
        {
            foreach (var pattern in RemoteUrlPatterns(profile.HostAlias))
            {
                AddInclude(lines, $"hasconfig:remote.*.url:{pattern}", profile, paths);
            }
        }

        if (hooksPath is not null || sshCommand is not null)
        {
            lines.Add("[core]");
        }

        if (hooksPath is not null)
        {
            lines.Add("\t# Commit guard: checks the identity before each commit, then runs the repository's own hooks");
            lines.Add($"\thooksPath = {Value(ToGitPath(hooksPath))}");
        }

        if (sshCommand is not null)
        {
            lines.Add("\t# Windows OpenSSH, so passphrase-protected keys can come from the Windows ssh-agent");
            lines.Add($"\tsshCommand = {Value(sshCommand.Replace('\\', '/'))}");
        }

        return lines;
    }

    /// <summary>Remote URL globs that identify a repository as belonging to <paramref name="alias"/>.</summary>
    /// <remarks>
    /// <c>**</c> only spans directories right after a <c>/</c>, so the scp-like form needs <c>:*/**</c>
    /// (<c>git@alias:**</c> would match <c>git@alias:owner</c> but not <c>git@alias:owner/repo.git</c>).
    /// </remarks>
    public static IReadOnlyList<string> RemoteUrlPatterns(string alias) =>
        [$"git@{alias}:*/**", $"ssh://git@{alias}/**"];

    /// <summary>Full content of <c>~/.gitconfig-&lt;id&gt;</c>.</summary>
    public static string ProfileFile(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var lines = new List<string>
        {
            $"{ProfileFileHeader} for profile \"{profile.Id}\". Edit the profile in GitPersona; changes made here are overwritten.",
            "[user]",
            $"\tname = {Value(profile.GitUserName)}",
            $"\temail = {Value(profile.GitUserEmail)}",
        };

        if (!string.IsNullOrWhiteSpace(profile.GitHubUsername))
        {
            lines.Add("[github]");
            lines.Add($"\tuser = {Value(profile.GitHubUsername)}");

            // Lets Git Credential Manager pick the right account for HTTPS remotes too.
            lines.Add($"[credential {Subsection($"https://{profile.Host}")}]");
            lines.Add($"\tusername = {Value(profile.GitHubUsername)}");
        }

        return string.Join('\n', lines) + "\n";
    }

    public static bool IsManagedProfileFile(string text) =>
        text.StartsWith(ProfileFileHeader, StringComparison.Ordinal);

    private static void AddInclude(List<string> lines, string condition, Profile profile, UserPaths paths)
    {
        lines.Add($"[includeIf {Subsection(condition)}]");
        lines.Add($"\tpath = {Value(ToGitPath(paths.ProfileGitConfigFile(profile.Id)))}");
    }
}
