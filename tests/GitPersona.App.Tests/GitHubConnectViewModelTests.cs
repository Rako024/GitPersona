using System.Net;
using System.Net.Http;
using System.Text;
using GitPersona.App.Localization;
using GitPersona.App.ViewModels;
using GitPersona.Core.GitHub;
using GitPersona.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitPersona.App.Tests;

/// <summary>
/// Drives the connect dialog's logic against a scripted GitHub. Tokens stay in memory: the Core tests write to
/// the real Credential Manager at the same time, and two processes writing there at once can bring back entries
/// the other one deleted.
/// </summary>
public sealed class GitHubConnectViewModelTests : IDisposable
{
    private const string PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGitPersonaTestKey work@example.com";
    private readonly string _host = $"gp-{Guid.NewGuid():N}.invalid";
    private readonly ScriptedGitHub _github = new();
    private readonly HttpClient _http;
    private readonly GitHubTokenStore _tokens = new(new MemoryCredentialStore());
    private readonly GitHubApiService _api;

    public GitHubConnectViewModelTests()
    {
        _http = new HttpClient(_github);
        _api = new GitHubApiService(_http, NullLogger<GitHubApiService>.Instance, (_, _) => Task.CompletedTask);
    }

    public void Dispose() => _http.Dispose();

    private static LocalizationManager Loc => LocalizationManager.Instance;

    private GitHubConnectViewModel Create(string? expectedLogin = "work-account", string currentEmail = "old@corp.example") =>
        new(_api, _tokens, NullLogger.Instance, _host, expectedLogin, PublicKey, "work", currentEmail, clientId: null);

    private void GitHubKnows(string login, string? scopes = "write:public_key, user:email")
    {
        _github.On("GET", "user", HttpStatusCode.OK, $$"""{"id":42,"login":"{{login}}"}""", scopes);
        _github.On("GET", "user/emails", HttpStatusCode.OK, $$"""[{"email":"{{login}}@example.com","primary":true,"verified":true},{"email":"unverified@example.com","primary":false,"verified":false}]""");
    }

    [Fact]
    public async Task Uploads_the_key_saves_the_token_and_offers_the_accounts_emails()
    {
        GitHubKnows("work-account");
        _github.On("GET", "user/keys", HttpStatusCode.OK, "[]");
        _github.On("POST", "user/keys", HttpStatusCode.Created, """{"id":7,"key":"ssh-ed25519 AAAA","title":"t"}""");
        var vm = Create();

        await vm.SubmitTokenAsync("tok-work");

        Assert.Null(vm.ErrorText);
        Assert.Equal(ConnectStep.Done, vm.Step);
        Assert.Equal(Loc.Format("Connect_KeyUploaded", "work-account"), vm.KeyStatus);
        Assert.Equal("tok-work", _tokens.Read(_host, "work-account"));
        Assert.Contains($"\"title\":\"GitPersona \\u00B7 {Environment.MachineName} \\u00B7 work\"", _github.Bodies["POST user/keys"], StringComparison.Ordinal);

        // Verified emails and the noreply address; the unverified one is not offered; the current one is kept but not preselected.
        Assert.Equal(
            ["work-account@example.com", $"42+work-account@users.noreply.{_host}", "old@corp.example"],
            vm.Emails.Select(e => e.Email));
        Assert.Equal("work-account@example.com", vm.SelectedEmail?.Email);
        Assert.Contains("old@corp.example", vm.EmailNote, StringComparison.Ordinal);

        vm.DoneCommand.Execute(null);
        Assert.Equal(new ConnectResult("work-account", "work-account@example.com", vm.KeyStatus!), vm.Result);
    }

    [Fact]
    public async Task Signing_in_as_another_account_stops_until_the_user_decides()
    {
        GitHubKnows("personal-account");
        _github.On("GET", "user/keys", HttpStatusCode.OK, "[]");
        _github.On("POST", "user/keys", HttpStatusCode.Created, """{"id":7,"key":"k","title":"t"}""");
        var vm = Create(expectedLogin: "work-account");

        await vm.SubmitTokenAsync("tok-personal");

        Assert.Equal(ConnectStep.Mismatch, vm.Step);
        Assert.Contains("personal-account", vm.MismatchText, StringComparison.Ordinal);
        Assert.DoesNotContain("POST user/keys", _github.Bodies.Keys);
        Assert.Null(_tokens.Read(_host, "personal-account"));

        await vm.AcceptOtherAccountCommand.ExecuteAsync(null);

        Assert.Equal(ConnectStep.Done, vm.Step);
        Assert.Contains("POST user/keys", _github.Bodies.Keys);
        vm.DoneCommand.Execute(null);
        Assert.Equal("personal-account", vm.Result?.Login);
    }

    [Fact]
    public async Task A_key_already_on_the_account_is_not_uploaded_again()
    {
        GitHubKnows("work-account");
        _github.On("GET", "user/keys", HttpStatusCode.OK, """[{"id":1,"key":"ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGitPersonaTestKey","title":"laptop"}]""");
        var vm = Create(currentEmail: "work-account@example.com");

        await vm.SubmitTokenAsync("tok");

        Assert.Equal(ConnectStep.Done, vm.Step);
        Assert.Equal(Loc.Format("Connect_KeyAlreadyPresent", "work-account"), vm.KeyStatus);
        Assert.DoesNotContain("POST user/keys", _github.Bodies.Keys);
        Assert.Equal("work-account@example.com", vm.SelectedEmail?.Email);
        Assert.Null(vm.EmailNote);
    }

    [Fact]
    public async Task A_key_registered_on_another_account_is_explained()
    {
        GitHubKnows("work-account");
        _github.On("GET", "user/keys", HttpStatusCode.OK, "[]");
        _github.On("POST", "user/keys", HttpStatusCode.UnprocessableEntity, """{"message":"Validation Failed","errors":[{"message":"key is already in use"}]}""");
        var vm = Create();

        await vm.SubmitTokenAsync("tok");

        Assert.Equal(Loc["Connect_KeyInUse"], vm.ErrorText);
        Assert.Equal(ConnectStep.ChooseMethod, vm.Step);
    }

    [Fact]
    public async Task A_token_without_the_key_scope_is_refused()
    {
        GitHubKnows("work-account", scopes: "repo, gist");
        var vm = Create();

        await vm.SubmitTokenAsync("tok");

        Assert.Equal(Loc["Connect_MissingScope"], vm.ErrorText);
        Assert.Null(_tokens.Read(_host, "work-account"));
    }

    [Fact]
    public async Task A_saved_token_that_stopped_working_is_removed()
    {
        _tokens.Save(_host, "work-account", "revoked");
        _github.On("GET", "user", HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""");
        var vm = Create();
        Assert.True(vm.HasSavedLogin);

        await vm.UseSavedSignInCommand.ExecuteAsync(null);

        Assert.Equal(Loc["Connect_SavedExpired"], vm.ErrorText);
        Assert.Null(_tokens.Read(_host, "work-account"));
    }

    [Fact]
    public async Task Not_remembering_the_sign_in_stores_nothing()
    {
        GitHubKnows("work-account");
        _github.On("GET", "user/keys", HttpStatusCode.OK, "[]");
        _github.On("POST", "user/keys", HttpStatusCode.Created, """{"id":7,"key":"k","title":"t"}""");
        var vm = Create();
        vm.RememberToken = false;

        await vm.SubmitTokenAsync("tok");

        Assert.Equal(ConnectStep.Done, vm.Step);
        Assert.Null(_tokens.Read(_host, "work-account"));
    }

    /// <summary>Answers by "METHOD path" (relative to the API root) and records request bodies.</summary>
    private sealed class ScriptedGitHub : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Json, string? Scopes)> _routes = [];

        public Dictionary<string, string> Bodies { get; } = [];

        public void On(string method, string path, HttpStatusCode status, string json, string? scopes = null) =>
            _routes[$"{method} {path}"] = (status, json, scopes);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.Replace("/api/v3/", string.Empty, StringComparison.Ordinal).TrimStart('/');
            var key = $"{request.Method.Method} {path}";
            Bodies[key] = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (!_routes.TryGetValue(key, out var route))
            {
                throw new InvalidOperationException("Unexpected request " + key);
            }

            var response = new HttpResponseMessage(route.Status) { Content = new StringContent(route.Json, Encoding.UTF8, "application/json") };
            if (route.Scopes is not null)
            {
                response.Headers.TryAddWithoutValidation("X-OAuth-Scopes", route.Scopes);
            }

            return response;
        }
    }

    private sealed class MemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, (string UserName, string Secret)> _entries = new(StringComparer.OrdinalIgnoreCase);

        public void Save(string targetName, string userName, string secret) => _entries[targetName] = (userName, secret);

        public (string? UserName, string Secret)? Read(string targetName) =>
            _entries.TryGetValue(targetName, out var entry) ? (entry.UserName, entry.Secret) : null;

        public bool Delete(string targetName) => _entries.Remove(targetName);

        public IReadOnlyList<StoredCredential> Enumerate(string filter) =>
            [.. _entries
                .Where(e => e.Key.StartsWith(filter.TrimEnd('*'), StringComparison.OrdinalIgnoreCase))
                .Select(e => new StoredCredential(e.Key, e.Value.UserName, DateTimeOffset.Now))];
    }
}
