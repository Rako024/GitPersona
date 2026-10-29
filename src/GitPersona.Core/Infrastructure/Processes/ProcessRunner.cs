using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace GitPersona.Core.Infrastructure.Processes;

public interface IProcessRunner
{
    /// <summary>Runs the command to completion. A non-zero exit code is returned, not thrown.</summary>
    /// <param name="errorLines">Receives each stderr line as it arrives (git and ssh report progress there).</param>
    /// <exception cref="ProcessStartException">The executable could not be started.</exception>
    /// <exception cref="ProcessTimeoutException">The command exceeded <see cref="ProcessRequest.Timeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ProcessResult> RunAsync(
        ProcessRequest request,
        IProgress<string>? errorLines = null,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        IProgress<string>? errorLines = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = Describe(request);

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in request.Environment)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (output)
            {
                output.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (error)
            {
                error.AppendLine(e.Data);
            }

            errorLines?.Report(e.Data);
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "Could not start {Command}", command);
            throw new ProcessStartException(command, ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await WriteStandardInputAsync(process, request.StandardInput).ConfigureAwait(false);

        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Cancelled {Command} after {Elapsed} ms", command, stopwatch.ElapsedMilliseconds);
                throw;
            }

            logger.LogWarning("Timed out {Command} after {Timeout}", command, request.Timeout);
            throw new ProcessTimeoutException(command, request.Timeout, Snapshot(error));
        }

        stopwatch.Stop();
        var result = new ProcessResult(process.ExitCode, Snapshot(output), Snapshot(error), stopwatch.Elapsed);
        logger.LogInformation(
            "Ran {Command} -> exit {ExitCode} in {Elapsed} ms",
            command,
            result.ExitCode,
            stopwatch.ElapsedMilliseconds);
        if (!result.Succeeded)
        {
            logger.LogDebug("stderr of {Command}: {Error}", command, Mask(result.StandardError, request.SecretsToMask));
        }

        return result;
    }

    /// <summary>A loggable command line with secrets masked.</summary>
    public static string Describe(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parts = request.Arguments.Prepend(request.FileName)
            .Select(part => part.Length == 0 || part.Any(char.IsWhiteSpace) ? $"\"{part}\"" : part);
        return Mask(string.Join(' ', parts), request.SecretsToMask);
    }

    private static string Mask(string text, IReadOnlyCollection<string> secrets)
    {
        foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s)))
        {
            text = text.Replace(secret, "***", StringComparison.Ordinal);
        }

        return text;
    }

    private static async Task WriteStandardInputAsync(Process process, string? input)
    {
        try
        {
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input).ConfigureAwait(false);
            }

            // Always close stdin so tools that wait for input (ssh, credential helpers) see EOF instead of hanging.
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process exited before reading its input; its exit code tells the story.
        }
    }

    private static string Snapshot(StringBuilder builder)
    {
        lock (builder)
        {
            return builder.ToString();
        }
    }

    private void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "Could not stop process {ProcessId}", process.Id);
        }
    }
}
