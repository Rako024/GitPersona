namespace GitPersona.Core.Infrastructure;

public sealed class ToolNotFoundException(string tool)
    : Exception($"'{tool}' was not found. Install Git for Windows or the Windows OpenSSH client.")
{
    public string Tool { get; } = tool;
}

/// <summary>
/// Finds git and the OpenSSH tools. Two OpenSSH builds usually coexist on Windows:
/// the Windows one (System32\OpenSSH, what a terminal runs) and the one bundled with Git for Windows
/// (what git runs unless <c>core.sshCommand</c> says otherwise).
/// </summary>
public sealed class ToolLocator
{
    private readonly string? _pathVariable;
    private readonly string _systemDirectory;
    private readonly Lazy<string?> _git;
    private readonly Lazy<string?> _gh;

    public ToolLocator()
        : this(Environment.GetEnvironmentVariable("PATH"), Environment.SystemDirectory)
    {
    }

    internal ToolLocator(string? pathVariable, string systemDirectory)
    {
        _pathVariable = pathVariable;
        _systemDirectory = systemDirectory;
        _git = new Lazy<string?>(() => FindOnPath("git.exe") ?? InProgramFiles("Git", "cmd", "git.exe"));
        _gh = new Lazy<string?>(() => FindOnPath("gh.exe") ?? InProgramFiles("GitHub CLI", "gh.exe"));
    }

    /// <summary>Full path of git.exe, or <see langword="null"/> when Git for Windows is not installed.</summary>
    public string? Git => _git.Value;

    /// <summary>Full path of the GitHub CLI, or <see langword="null"/> when it is not installed.</summary>
    public string? Gh => _gh.Value;

    /// <summary>Installation root of Git for Windows (the folder containing <c>cmd</c> and <c>usr</c>).</summary>
    public string? GitInstallRoot
    {
        get
        {
            for (var directory = Path.GetDirectoryName(Git); directory is not null; directory = Path.GetDirectoryName(directory))
            {
                if (Directory.Exists(Path.Combine(directory, "usr", "bin")))
                {
                    return directory;
                }
            }

            return null;
        }
    }

    public string RequireGit() => Git ?? throw new ToolNotFoundException("git");

    /// <summary><c>System32\OpenSSH\&lt;tool&gt;.exe</c> if present.</summary>
    public string? WindowsOpenSsh(string tool) => ExistingFile(Path.Combine(_systemDirectory, "OpenSSH", tool + ".exe"));

    /// <summary><c>&lt;Git&gt;\usr\bin\&lt;tool&gt;.exe</c> if present.</summary>
    public string? GitBundledOpenSsh(string tool) =>
        GitInstallRoot is { } root ? ExistingFile(Path.Combine(root, "usr", "bin", tool + ".exe")) : null;

    /// <summary>Keys are interchangeable between the two builds; prefer the Windows one, which the ssh-agent service understands.</summary>
    public string RequireSshKeygen() =>
        WindowsOpenSsh("ssh-keygen") ?? GitBundledOpenSsh("ssh-keygen") ?? throw new ToolNotFoundException("ssh-keygen");

    public string RequireSsh() =>
        WindowsOpenSsh("ssh") ?? GitBundledOpenSsh("ssh") ?? throw new ToolNotFoundException("ssh");

    private string? FindOnPath(string executable)
    {
        foreach (var directory in (_pathVariable ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (ExistingFile(Path.Combine(directory.Trim('"'), executable)) is { } found)
                {
                    return found;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry.
            }
        }

        return null;
    }

    private static string? InProgramFiles(params string[] parts) =>
        ExistingFile(Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), .. parts]));

    private static string? ExistingFile(string path) => File.Exists(path) ? Path.GetFullPath(path) : null;
}
