namespace GitPersona.Core.Infrastructure.Processes;

/// <summary>An external command to run. Arguments are passed verbatim, never through a shell.</summary>
public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public string? WorkingDirectory { get; init; }

    /// <summary>Use <see cref="Timeout.InfiniteTimeSpan"/> for long operations that rely on cancellation instead.</summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>Variables to set; a <see langword="null"/> value removes the variable.</summary>
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();

    /// <summary>Written to stdin, which is then closed. Stdin is closed immediately when this is <see langword="null"/>.</summary>
    public string? StandardInput { get; init; }

    /// <summary>Values (tokens, passphrases) replaced by <c>***</c> wherever the command is logged.</summary>
    public IReadOnlyCollection<string> SecretsToMask { get; init; } = [];
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration)
{
    public bool Succeeded => ExitCode == 0;
}
