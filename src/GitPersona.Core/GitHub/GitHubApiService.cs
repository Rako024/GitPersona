using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace GitPersona.Core.GitHub;

/// <summary>
/// The few GitHub calls GitPersona needs: OAuth device flow sign-in, the signed-in user and their emails,
/// and the user's SSH keys. Tokens are passed per call and never logged.
/// </summary>
public sealed class GitHubApiService
{
    /// <summary>Manage the user's SSH keys and read their (private) email addresses. Nothing else.</summary>
    public const string Scopes = "write:public_key user:email";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly ILogger<GitHubApiService> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public GitHubApiService(HttpClient http, ILogger<GitHubApiService> logger, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _logger = logger;
        _delay = delay ?? Task.Delay;
    }

    public async Task<DeviceCode> StartDeviceFlowAsync(string host, string clientId, CancellationToken cancellationToken = default)
    {
        var endpoints = GitHubEndpoints.For(host);
        using var request = Form(endpoints.DeviceCode, new() { ["client_id"] = clientId, ["scope"] = Scopes });
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // An unknown client_id is answered with 404 {"error":"Not Found"}, typically a mistyped ID in Settings.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new DeviceFlowException(DeviceFlowError.InvalidClient, "GitHub does not know this OAuth App client ID.");
        }

        var body = await ReadJsonAsync<DeviceCodeResponse>(response, cancellationToken).ConfigureAwait(false);
        if (body.Error is { } error)
        {
            throw DeviceError(error, body.ErrorDescription);
        }

        if (body.DeviceCode is null || body.UserCode is null || body.VerificationUri is null)
        {
            throw new GitHubApiException("GitHub returned an incomplete device code response.", (int)response.StatusCode);
        }

        return new DeviceCode(body.DeviceCode, body.UserCode, new Uri(body.VerificationUri), body.ExpiresIn ?? 900, body.Interval ?? 5);
    }

    /// <summary>Polls until the user authorizes the device in the browser, honouring <c>slow_down</c>.</summary>
    /// <exception cref="DeviceFlowException">The code expired or the user declined.</exception>
    public async Task<string> WaitForDeviceTokenAsync(string host, string clientId, DeviceCode code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        var endpoints = GitHubEndpoints.For(host);
        var interval = TimeSpan.FromSeconds(Math.Max(code.Interval, 1));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(code.ExpiresIn);

        while (true)
        {
            await _delay(interval, cancellationToken).ConfigureAwait(false);
            using var request = Form(endpoints.AccessToken, new()
            {
                ["client_id"] = clientId,
                ["device_code"] = code.Code,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            });
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await ReadJsonAsync<AccessTokenResponse>(response, cancellationToken).ConfigureAwait(false);

            switch (body.Error)
            {
                case null when body.AccessToken is { Length: > 0 } token:
                    _logger.LogInformation("Device flow authorized on {Host}", host);
                    return token;
                case "authorization_pending":
                    break;
                case "slow_down":
                    // GitHub asks for 5 more seconds each time; it also sends the new interval.
                    interval = body.Interval is { } seconds ? TimeSpan.FromSeconds(seconds) : interval + TimeSpan.FromSeconds(5);
                    break;
                default:
                    throw DeviceError(body.Error ?? "unknown", body.ErrorDescription);
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new DeviceFlowException(DeviceFlowError.Expired, "The device code expired before it was authorized.");
            }
        }
    }

    /// <exception cref="GitHubUnauthorizedException">The token is not valid.</exception>
    public async Task<GitHubIdentity> GetIdentityAsync(string host, string token, CancellationToken cancellationToken = default)
    {
        using var response = await SendApiAsync(HttpMethod.Get, host, "user", token, null, cancellationToken).ConfigureAwait(false);
        var user = await ReadJsonAsync<GitHubUser>(response, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string>? scopes = response.Headers.TryGetValues("X-OAuth-Scopes", out var values)
            ? values.SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).ToList()
            : null;
        return new GitHubIdentity(user, scopes);
    }

    /// <returns>The emails, or <see langword="null"/> when the token may not read them (missing <c>user:email</c>).</returns>
    public async Task<IReadOnlyList<GitHubEmail>?> GetEmailsAsync(string host, string token, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendApiAsync(HttpMethod.Get, host, "user/emails", token, null, cancellationToken).ConfigureAwait(false);
            return await ReadJsonAsync<List<GitHubEmail>>(response, cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubApiException ex) when (ex.StatusCode is 403 or 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<GitHubKey>> GetKeysAsync(string host, string token, CancellationToken cancellationToken = default)
    {
        using var response = await SendApiAsync(HttpMethod.Get, host, "user/keys?per_page=100", token, null, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<List<GitHubKey>>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <exception cref="GitHubKeyInUseException">The key is registered on some account already.</exception>
    public async Task<GitHubKey> AddKeyAsync(string host, string token, string title, string publicKey, CancellationToken cancellationToken = default)
    {
        var payload = JsonContent.Create(new { title, key = publicKey.Trim() }, options: Json);
        using var response = await SendApiAsync(HttpMethod.Post, host, "user/keys", token, payload, cancellationToken).ConfigureAwait(false);
        var key = await ReadJsonAsync<GitHubKey>(response, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Uploaded SSH key {KeyId} '{Title}' to {Host}", key.Id, title, host);
        return key;
    }

    /// <summary>Whether two OpenSSH public key lines are the same key (comments are ignored).</summary>
    public static bool IsSameKey(string a, string b)
    {
        static string[] Parts(string key) => key.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var (x, y) = (Parts(a), Parts(b));
        return x.Length >= 2 && y.Length >= 2 && x[0] == y[0] && x[1] == y[1];
    }

    /// <summary>The private commit email GitHub attributes to the user (<c>12345+login@users.noreply.github.com</c>).</summary>
    public static string NoReplyEmail(GitHubUser user, string host)
    {
        ArgumentNullException.ThrowIfNull(user);
        return $"{user.Id}+{user.Login}@users.noreply.{host.Trim().ToLowerInvariant()}";
    }

    private async Task<HttpResponseMessage> SendApiAsync(HttpMethod method, string host, string path, string token, HttpContent? content, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, new Uri(GitHubEndpoints.For(host).Api, path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        AddUserAgent(request);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            request.Dispose();
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var error = await TryReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("GitHub {Method} {Path} on {Host} failed: {Status} {Message}", method, path, host, (int)response.StatusCode, error.Message);
            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new GitHubUnauthorizedException(error.Message ?? "The GitHub token is not valid."),
                HttpStatusCode.UnprocessableEntity when error.Mentions("already in use") =>
                    new GitHubKeyInUseException(error.Message ?? "The key is already in use."),
                _ => new GitHubApiException(error.Message ?? $"GitHub returned {(int)response.StatusCode}.", (int)response.StatusCode),
            };
        }
    }

    private static HttpRequestMessage Form(Uri uri, Dictionary<string, string> fields)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        AddUserAgent(request);
        return request;
    }

    private static void AddUserAgent(HttpRequestMessage request) =>
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GitPersona", typeof(GitHubApiService).Assembly.GetName().Version?.ToString(3) ?? "0"));

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false)
                ?? throw new GitHubApiException("GitHub returned an empty response.", (int)response.StatusCode);
        }
        catch (JsonException ex)
        {
            throw new GitHubApiException("GitHub returned a response GitPersona could not read.", (int)response.StatusCode, ex);
        }
    }

    private static async Task<ErrorResponse> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorResponse>(Json, cancellationToken).ConfigureAwait(false) ?? new ErrorResponse();
        }
        catch (JsonException)
        {
            return new ErrorResponse();
        }
    }

    private static DeviceFlowException DeviceError(string error, string? description) => new(
        error switch
        {
            "expired_token" => DeviceFlowError.Expired,
            "access_denied" => DeviceFlowError.AccessDenied,
            "device_flow_disabled" => DeviceFlowError.DeviceFlowDisabled,
            "incorrect_client_credentials" or "unsupported_grant_type" => DeviceFlowError.InvalidClient,
            _ => DeviceFlowError.Unknown,
        },
        description ?? error);

    private sealed record DeviceCodeResponse(
        string? DeviceCode,
        string? UserCode,
        string? VerificationUri,
        int? ExpiresIn,
        int? Interval,
        string? Error,
        string? ErrorDescription);

    private sealed record AccessTokenResponse(string? AccessToken, string? Error, string? ErrorDescription, int? Interval);

    private sealed record ErrorResponse
    {
        public string? Message { get; init; }

        public List<ErrorDetail>? Errors { get; init; }

        public bool Mentions(string text) =>
            (Message?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
            || (Errors?.Any(e => e.Message?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ?? false);
    }

    private sealed record ErrorDetail(string? Message, string? Field, string? Code);
}
