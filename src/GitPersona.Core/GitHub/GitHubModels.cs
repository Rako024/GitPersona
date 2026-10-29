namespace GitPersona.Core.GitHub;

/// <summary>OAuth and REST base URLs for github.com or a GitHub Enterprise Server host.</summary>
public sealed record GitHubEndpoints(Uri Web, Uri Api)
{
    public static GitHubEndpoints For(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var web = new Uri($"https://{host.Trim()}/");
        var api = host.Trim().Equals("github.com", StringComparison.OrdinalIgnoreCase)
            ? new Uri("https://api.github.com/")
            : new Uri(web, "api/v3/");
        return new GitHubEndpoints(web, api);
    }

    public Uri DeviceCode => new(Web, "login/device/code");

    public Uri AccessToken => new(Web, "login/oauth/access_token");

    /// <summary>Page where a classic token with the scopes GitPersona needs can be created.</summary>
    public Uri NewTokenPage => new(Web, $"settings/tokens/new?scopes={Uri.EscapeDataString(GitHubApiService.Scopes.Replace(' ', ','))}&description=GitPersona");

    public Uri NewOAuthAppPage => new(Web, "settings/applications/new");
}

/// <summary>The OAuth App the device flow signs in with.</summary>
public static class GitHubOAuthApp
{
    /// <summary>
    /// GitPersona's own OAuth App on github.com (device flow enabled). A client ID is not a secret: the
    /// device flow uses no client secret.
    /// </summary>
    public const string BuiltInClientId = "Ov23li0ri4pypxL7BeCi";

    /// <returns>
    /// The client ID from the settings; otherwise the built-in one on github.com, and <see langword="null"/>
    /// on other hosts, where an OAuth App has to be registered on that server first.
    /// </returns>
    public static string? ClientIdFor(string host, string? configured) =>
        !string.IsNullOrWhiteSpace(configured) ? configured.Trim()
        : host.Trim().Equals("github.com", StringComparison.OrdinalIgnoreCase) ? BuiltInClientId
        : null;
}

/// <param name="Interval">Minimum seconds between polls.</param>
public sealed record DeviceCode(string Code, string UserCode, Uri VerificationUri, int ExpiresIn, int Interval);

public sealed record GitHubUser(long Id, string Login, string? Name, string? Email);

/// <param name="Scopes">From <c>X-OAuth-Scopes</c>; <see langword="null"/> for fine-grained tokens, which do not report scopes.</param>
public sealed record GitHubIdentity(GitHubUser User, IReadOnlyList<string>? Scopes);

public sealed record GitHubEmail(string Email, bool Primary, bool Verified, string? Visibility);

public sealed record GitHubKey(long Id, string Key, string Title);

public enum DeviceFlowError
{
    Expired,
    AccessDenied,
    DeviceFlowDisabled,
    InvalidClient,
    Unknown,
}

public class GitHubApiException(string message, int? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int? StatusCode { get; } = statusCode;
}

/// <summary>The token is invalid, expired or was revoked.</summary>
public sealed class GitHubUnauthorizedException(string message) : GitHubApiException(message, 401);

/// <summary>GitHub registers a public key on one account only; this one is already taken (possibly as a deploy key).</summary>
public sealed class GitHubKeyInUseException(string message) : GitHubApiException(message, 422);

public sealed class DeviceFlowException(DeviceFlowError error, string message) : GitHubApiException(message)
{
    public DeviceFlowError Error { get; } = error;
}
