using GitPersona.Core.Git;
using GitPersona.Core.Infrastructure;

namespace GitPersona.Core.Ssh;

public enum SshCommandSource
{
    /// <summary>The GIT_SSH_COMMAND environment variable.</summary>
    GitSshCommandVariable,

    /// <summary>core.sshCommand in the global or system git config.</summary>
    CoreSshCommand,

    /// <summary>The GIT_SSH environment variable.</summary>
    GitSshVariable,

    /// <summary>Nothing configured: Git for Windows runs its own bundled OpenSSH.</summary>
    GitBundled,

    /// <summary>Nothing configured and no bundled ssh: the Windows OpenSSH client.</summary>
    WindowsOpenSsh,
}

/// <param name="Arguments">Extra arguments from a configured command line, e.g. <c>ssh -i key</c>.</param>
public sealed record SshCommand(string FileName, IReadOnlyList<string> Arguments, SshCommandSource Source)
{
    public bool IsWindowsOpenSsh => FileName.Contains(@"\System32\OpenSSH\", StringComparison.OrdinalIgnoreCase)
        || FileName.Contains("/System32/OpenSSH/", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Works out which ssh git runs, with git's own precedence.</summary>
public sealed class GitSshResolver(GitService git, ToolLocator tools, UserPaths paths)
{
    /// <summary>GIT_SSH_COMMAND, then core.sshCommand, then GIT_SSH, then plain "ssh" (Git's bundled one).</summary>
    public async Task<SshCommand?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (Environment.GetEnvironmentVariable("GIT_SSH_COMMAND") is { Length: > 0 } fromVariable)
        {
            return Parse(fromVariable, SshCommandSource.GitSshCommandVariable);
        }

        if (await ConfiguredAsync(cancellationToken).ConfigureAwait(false) is { } configured)
        {
            return Parse(configured, SshCommandSource.CoreSshCommand);
        }

        if (Environment.GetEnvironmentVariable("GIT_SSH") is { Length: > 0 } program)
        {
            return new SshCommand(program, [], SshCommandSource.GitSshVariable);
        }

        return tools.GitBundledOpenSsh("ssh") is { } bundled ? new SshCommand(bundled, [], SshCommandSource.GitBundled)
            : tools.WindowsOpenSsh("ssh") is { } windows ? new SshCommand(windows, [], SshCommandSource.WindowsOpenSsh)
            : null;
    }

    /// <summary>Splits a command line the way git's shell would for the simple cases: quotes and spaces.</summary>
    internal SshCommand Parse(string commandLine, SshCommandSource source)
    {
        var parts = Tokenize(commandLine);
        if (parts.Count == 0)
        {
            return new SshCommand(commandLine, [], source);
        }

        // A bare "ssh" resolves through Git for Windows' PATH, where its bundled ssh comes first.
        var program = parts[0] is "ssh" or "ssh.exe"
            ? tools.GitBundledOpenSsh("ssh") ?? tools.WindowsOpenSsh("ssh") ?? parts[0]
            : parts[0];
        return new SshCommand(program, parts.Skip(1).ToList(), source);
    }

    internal static List<string> Tokenize(string commandLine)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        char? quote = null;
        foreach (var c in commandLine)
        {
            if (quote is not null)
            {
                if (c == quote)
                {
                    quote = null;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private async Task<string?> ConfiguredAsync(CancellationToken cancellationToken)
    {
        // The managed ~/.gitconfig wins over the system config; neither depends on the current directory.
        string[][] sources = File.Exists(paths.GlobalGitConfigFile) ? [["--file", paths.GlobalGitConfigFile], ["--system"]] : [["--system"]];
        foreach (var source in sources)
        {
            var result = await git.RunAsync(["config", .. source, "--get", "core.sshCommand"], cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.Succeeded && result.StandardOutput.Trim() is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }
}
