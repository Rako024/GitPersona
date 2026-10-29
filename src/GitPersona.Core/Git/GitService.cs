using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;

namespace GitPersona.Core.Git;

/// <summary>Runs git non-interactively: it fails instead of waiting for a password prompt nobody can see.</summary>
public sealed class GitService(IProcessRunner runner, ToolLocator tools)
{
    public Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null,
        IProgress<string>? errorLines = null,
        CancellationToken cancellationToken = default)
    {
        var variables = new Dictionary<string, string?>(environment ?? new Dictionary<string, string?>())
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
        };
        var request = new ProcessRequest(tools.RequireGit(), arguments)
        {
            WorkingDirectory = workingDirectory,
            Environment = variables,
            Timeout = timeout ?? ProcessRequest.DefaultTimeout,
        };
        return runner.RunAsync(request, errorLines, cancellationToken);
    }

    /// <summary>The effective value of <paramref name="key"/> in <paramref name="repository"/>, or <see langword="null"/> when unset.</summary>
    public async Task<string?> GetConfigAsync(
        string repository,
        string key,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["config", "--get", key], repository, environment, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result.StandardOutput.TrimEnd('\r', '\n') : null;
    }
}
