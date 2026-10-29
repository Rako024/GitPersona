using System.Net;
using System.Text;
using GitPersona.Core.GitHub;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitPersona.Core.Tests.GitHub;

public sealed class GitHubApiServiceTests : IDisposable
{
    private readonly FakeHandler _handler = new();
    private readonly HttpClient _http;
    private readonly List<TimeSpan> _delays = [];
    private readonly GitHubApiService _api;

    public GitHubApiServiceTests()
    {
        _http = new HttpClient(_handler);
        _api = new GitHubApiService(_http, NullLogger<GitHubApiService>.Instance, (delay, _) =>
        {
            _delays.Add(delay);
            return Task.CompletedTask;
        });
    }

    public void Dispose() => _http.Dispose();

    private static readonly DeviceCode Code = new("dev-123", "ABCD-1234", new Uri("https://github.com/login/device"), 900, 5);

    [Fact]
    public async Task Starting_the_device_flow_sends_the_client_id_and_minimal_scopes()
    {
        _handler.Respond(HttpStatusCode.OK, """{"device_code":"dev-123","user_code":"ABCD-1234","verification_uri":"https://github.com/login/device","expires_in":899,"interval":5}""");

        var code = await _api.StartDeviceFlowAsync("github.com", "Ov23liClient");

        Assert.Equal(new DeviceCode("dev-123", "ABCD-1234", new Uri("https://github.com/login/device"), 899, 5), code);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal("https://github.com/login/device/code", request.Uri.ToString());
        Assert.Equal("client_id=Ov23liClient&scope=write%3Apublic_key+user%3Aemail", request.Body);
        Assert.Equal("application/json", request.Accept);
    }

    [Fact]
    public async Task Polling_waits_while_pending_slows_down_when_asked_and_returns_the_token()
    {
        _handler.Respond(HttpStatusCode.OK, """{"error":"authorization_pending"}""");
        _handler.Respond(HttpStatusCode.OK, """{"error":"slow_down","interval":10}""");
        _handler.Respond(HttpStatusCode.OK, """{"error":"slow_down"}""");
        _handler.Respond(HttpStatusCode.OK, """{"access_token":"gho_secret","token_type":"bearer","scope":"write:public_key,user:email"}""");

        var token = await _api.WaitForDeviceTokenAsync("github.com", "Ov23liClient", Code);

        Assert.Equal("gho_secret", token);
        Assert.Equal([5, 5, 10, 15], _delays.Select(d => d.TotalSeconds));
        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", _handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("device_code=dev-123", _handler.Requests[0].Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("expired_token", DeviceFlowError.Expired)]
    [InlineData("access_denied", DeviceFlowError.AccessDenied)]
    [InlineData("device_flow_disabled", DeviceFlowError.DeviceFlowDisabled)]
    [InlineData("incorrect_client_credentials", DeviceFlowError.InvalidClient)]
    public async Task Polling_reports_why_the_flow_ended(string error, DeviceFlowError expected)
    {
        _handler.Respond(HttpStatusCode.OK, $$"""{"error":"{{error}}","error_description":"described"}""");

        var ex = await Assert.ThrowsAsync<DeviceFlowException>(() => _api.WaitForDeviceTokenAsync("github.com", "id", Code));

        Assert.Equal(expected, ex.Error);
    }

    [Fact]
    public async Task Device_flow_disabled_is_reported_when_starting()
    {
        _handler.Respond(HttpStatusCode.BadRequest, """{"error":"device_flow_disabled","error_description":"Device Flow must be explicitly enabled for this App"}""");

        var ex = await Assert.ThrowsAsync<DeviceFlowException>(() => _api.StartDeviceFlowAsync("github.com", "id"));

        Assert.Equal(DeviceFlowError.DeviceFlowDisabled, ex.Error);
    }

    [Fact]
    public async Task An_unknown_client_id_is_reported_as_invalid_client()
    {
        // What github.com actually answers for a client_id that does not exist.
        _handler.Respond(HttpStatusCode.NotFound, """{"error":"Not Found"}""");

        var ex = await Assert.ThrowsAsync<DeviceFlowException>(() => _api.StartDeviceFlowAsync("github.com", "Ov23liTypo"));

        Assert.Equal(DeviceFlowError.InvalidClient, ex.Error);
    }

    [Fact]
    public async Task Reads_the_user_and_the_token_scopes()
    {
        _handler.Respond(HttpStatusCode.OK, """{"id":583231,"login":"octocat","name":"The Octocat","email":null}""",
            headers: new() { ["X-OAuth-Scopes"] = "user:email, write:public_key" });

        var identity = await _api.GetIdentityAsync("github.com", "tok");

        Assert.Equal(new GitHubUser(583231, "octocat", "The Octocat", null), identity.User);
        Assert.Equal(["user:email", "write:public_key"], identity.Scopes);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal("https://api.github.com/user", request.Uri.ToString());
        Assert.Equal("Bearer tok", request.Authorization);
        Assert.Equal("2022-11-28", request.ApiVersion);
        Assert.StartsWith("GitPersona/", request.UserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fine_grained_tokens_have_no_scope_list()
    {
        _handler.Respond(HttpStatusCode.OK, """{"id":1,"login":"me"}""");

        Assert.Null((await _api.GetIdentityAsync("github.com", "tok")).Scopes);
    }

    [Fact]
    public async Task An_invalid_token_is_reported_as_unauthorized()
    {
        _handler.Respond(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""");

        var ex = await Assert.ThrowsAsync<GitHubUnauthorizedException>(() => _api.GetIdentityAsync("github.com", "bad"));

        Assert.Equal("Bad credentials", ex.Message);
    }

    [Fact]
    public async Task Uploads_a_key()
    {
        _handler.Respond(HttpStatusCode.Created, """{"id":42,"key":"ssh-ed25519 AAAAkey","title":"GitPersona · PC · work"}""");

        var key = await _api.AddKeyAsync("github.com", "tok", "GitPersona · PC · work", "ssh-ed25519 AAAAkey me@example.com\n");

        Assert.Equal(42, key.Id);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.github.com/user/keys", request.Uri.ToString());
        using var body = System.Text.Json.JsonDocument.Parse(request.Body!);
        Assert.Equal("GitPersona · PC · work", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("ssh-ed25519 AAAAkey me@example.com", body.RootElement.GetProperty("key").GetString());
    }

    [Fact]
    public async Task A_key_registered_elsewhere_is_reported_as_in_use()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity,
            """{"message":"Validation Failed","errors":[{"resource":"PublicKey","code":"custom","field":"key","message":"key is already in use"}]}""");

        await Assert.ThrowsAsync<GitHubKeyInUseException>(() => _api.AddKeyAsync("github.com", "tok", "t", "ssh-ed25519 AAAA"));
    }

    [Fact]
    public async Task Missing_email_scope_yields_no_email_list()
    {
        _handler.Respond(HttpStatusCode.NotFound, """{"message":"Not Found"}""");

        Assert.Null(await _api.GetEmailsAsync("github.com", "tok"));
    }

    [Fact]
    public async Task Reads_emails()
    {
        _handler.Respond(HttpStatusCode.OK, """[{"email":"me@example.com","primary":true,"verified":true,"visibility":"private"}]""");

        var emails = await _api.GetEmailsAsync("github.com", "tok");

        Assert.Equal([new GitHubEmail("me@example.com", true, true, "private")], emails);
    }

    [Fact]
    public void Enterprise_hosts_use_their_own_api_and_oauth_urls()
    {
        var endpoints = GitHubEndpoints.For("git.acme.example");

        Assert.Equal("https://git.acme.example/api/v3/user", new Uri(endpoints.Api, "user").ToString());
        Assert.Equal("https://git.acme.example/login/device/code", endpoints.DeviceCode.ToString());
        Assert.Equal("https://api.github.com/user/keys", new Uri(GitHubEndpoints.For("GitHub.com").Api, "user/keys").ToString());
    }

    [Theory]
    [InlineData("github.com", null, GitHubOAuthApp.BuiltInClientId)]
    [InlineData(" GitHub.com ", "  ", GitHubOAuthApp.BuiltInClientId)]
    [InlineData("github.com", " Ov23own ", "Ov23own")]
    [InlineData("git.acme.example", null, null)]
    [InlineData("git.acme.example", "Ov23acme", "Ov23acme")]
    public void Uses_the_configured_client_id_or_the_built_in_one_on_github_com(string host, string? configured, string? expected)
    {
        Assert.Equal(expected, GitHubOAuthApp.ClientIdFor(host, configured));
    }

    [Fact]
    public void Keys_compare_by_type_and_body_only()
    {
        Assert.True(GitHubApiService.IsSameKey("ssh-ed25519 AAAA me@a", "ssh-ed25519 AAAA"));
        Assert.False(GitHubApiService.IsSameKey("ssh-ed25519 AAAA", "ssh-ed25519 BBBB"));
        Assert.False(GitHubApiService.IsSameKey("garbage", "garbage"));
    }

    [Fact]
    public void Builds_the_noreply_commit_email()
    {
        Assert.Equal("583231+octocat@users.noreply.github.com", GitHubApiService.NoReplyEmail(new GitHubUser(583231, "octocat", null, null), "github.com"));
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, string? Accept, string? Authorization, string? ApiVersion, string? UserAgent);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<RecordedRequest> Requests { get; } = [];

        public void Respond(HttpStatusCode status, string json, Dictionary<string, string>? headers = null)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            foreach (var (name, value) in headers ?? [])
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            _responses.Enqueue(response);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Accept.FirstOrDefault()?.MediaType,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-GitHub-Api-Version", out var versions) ? versions.Single() : null,
                request.Headers.UserAgent.ToString()));
            return _responses.Count > 0 ? _responses.Dequeue() : throw new InvalidOperationException("Unexpected request " + request.RequestUri);
        }
    }
}
