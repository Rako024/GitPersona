using System.Diagnostics;
using GitPersona.Core.Infrastructure.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitPersona.Core.Tests.Infrastructure;

public class ProcessRunnerTests
{
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    [Fact]
    public async Task Captures_output_and_exit_code()
    {
        var result = await _runner.RunAsync(new ProcessRequest(Cmd, ["/c", "echo hello& exit 3"]));

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("hello", result.StandardOutput.Trim());
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Passes_environment_overrides()
    {
        var request = new ProcessRequest(Cmd, ["/c", "echo %GITPERSONA_TEST%"])
        {
            Environment = new Dictionary<string, string?> { ["GITPERSONA_TEST"] = "from-test" },
        };

        var result = await _runner.RunAsync(request);

        Assert.Equal("from-test", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task Writes_standard_input_and_closes_it()
    {
        var request = new ProcessRequest(Path.Combine(Environment.SystemDirectory, "sort.exe"), [])
        {
            StandardInput = "banana\napple\n",
        };

        var result = await _runner.RunAsync(request);

        Assert.Equal(["apple", "banana"], result.StandardOutput.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Reports_stderr_lines_as_they_arrive()
    {
        var lines = new List<string>();
        var progress = new SynchronousProgress(lines.Add);

        await _runner.RunAsync(new ProcessRequest(Cmd, ["/c", "echo one 1>&2& echo two 1>&2"]), progress);

        Assert.Equal(["one", "two"], lines.Select(l => l.Trim()));
    }

    [Fact]
    public async Task Stops_a_command_that_exceeds_its_timeout()
    {
        var request = new ProcessRequest("ping", ["-n", "30", "127.0.0.1"]) { Timeout = TimeSpan.FromMilliseconds(300) };
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<ProcessTimeoutException>(() => _runner.RunAsync(request));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Cancellation_stops_the_command()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var request = new ProcessRequest("ping", ["-n", "30", "127.0.0.1"]) { Timeout = Timeout.InfiniteTimeSpan };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _runner.RunAsync(request, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task A_missing_executable_raises_a_start_exception()
    {
        await Assert.ThrowsAsync<ProcessStartException>(
            () => _runner.RunAsync(new ProcessRequest("gitpersona-no-such-tool", [])));
    }

    [Fact]
    public void Describe_masks_secrets_and_quotes_arguments_with_spaces()
    {
        var request = new ProcessRequest("ssh-keygen", ["-N", "s3cret pass", "-f", "C:\\Users\\A B\\key"])
        {
            SecretsToMask = ["s3cret pass"],
        };

        Assert.Equal("ssh-keygen -N \"***\" -f \"C:\\Users\\A B\\key\"", ProcessRunner.Describe(request));
    }

    private sealed class SynchronousProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value)
        {
            lock (this)
            {
                report(value);
            }
        }
    }
}
