namespace GitPersona.Core.Infrastructure.Processes;

public abstract class ProcessRunException : Exception
{
    protected ProcessRunException(string command, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Command = command;
    }

    /// <summary>The command line with secrets masked.</summary>
    public string Command { get; }
}

/// <summary>The executable could not be started, usually because it is not installed or not on PATH.</summary>
public sealed class ProcessStartException(string command, Exception innerException)
    : ProcessRunException(command, $"Could not start '{command}': {innerException.Message}", innerException);

public sealed class ProcessTimeoutException(string command, TimeSpan timeout, string partialError)
    : ProcessRunException(command, $"'{command}' did not finish within {timeout.TotalSeconds:0.#} s and was stopped.")
{
    public TimeSpan Timeout { get; } = timeout;

    public string PartialStandardError { get; } = partialError;
}
