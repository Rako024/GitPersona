using System.Text.Json;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using Microsoft.Extensions.Logging;

namespace GitPersona.Core.GitHub;

public sealed record GhAccount(string Host, string Login, bool Active);

/// <summary>Lists and switches the accounts the GitHub CLI (<c>gh</c>) is signed in to.</summary>
public sealed class GhCliService(IProcessRunner runner, ToolLocator tools, ILogger<GhCliService> logger)
{
    private static readonly IReadOnlyDictionary<string, string?> NoPrompts = new Dictionary<string, string?> { ["GH_PROMPT_DISABLED"] = "1" };

    public bool IsInstalled => tools.Gh is not null;

    public async Task<IReadOnlyList<GhAccount>> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        if (tools.Gh is not { } gh)
        {
            return [];
        }

        var result = await runner.RunAsync(
            new ProcessRequest(gh, ["auth", "status", "--json", "hosts"]) { Environment = NoPrompts },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            return ParseAccounts(result.StandardOutput);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not read `gh auth status` (exit {ExitCode})", result.ExitCode);
            return [];
        }
    }

    /// <exception cref="InvalidOperationException">gh refused the switch.</exception>
    public async Task SwitchAsync(GhAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var gh = tools.Gh ?? throw new ToolNotFoundException("gh");
        var result = await runner.RunAsync(
            new ProcessRequest(gh, ["auth", "switch", "--hostname", account.Host, "--user", account.Login]) { Environment = NoPrompts },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.StandardError.Trim());
        }

        logger.LogInformation("gh switched to {Login} on {Host}", account.Login, account.Host);
    }

    /// <summary><c>{"hosts":{"github.com":[{"login":"octocat","active":true,...}]}}</c></summary>
    internal static IReadOnlyList<GhAccount> ParseAccounts(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("hosts", out var hosts) || hosts.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var accounts = new List<GhAccount>();
        foreach (var host in hosts.EnumerateObject())
        {
            foreach (var entry in host.Value.EnumerateArray())
            {
                if (entry.TryGetProperty("login", out var login) && login.GetString() is { Length: > 0 } name)
                {
                    var active = entry.TryGetProperty("active", out var flag) && flag.ValueKind == JsonValueKind.True;
                    accounts.Add(new GhAccount(host.Name, name, active));
                }
            }
        }

        return accounts;
    }
}
