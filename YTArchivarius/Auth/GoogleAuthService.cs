// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;

namespace YTArchivarius.Auth;

/// <summary>
/// Single-user OAuth flow. The token (including the refresh token) is the only thing persisted,
/// in %LOCALAPPDATA%\YTArchivarius\token.
/// </summary>
public sealed class GoogleAuthService
{
    private const string UserKey = "me";

    private readonly GoogleAuthorizationCodeFlow? _flow;

    public GoogleAuthService(IConfiguration config)
    {
        RedirectUri = config["Google:RedirectUri"] is { Length: > 0 } uri ? uri : "https://localhost:5001/auth/callback";

        var clientId = config["Google:ClientId"];
        var clientSecret = config["Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return;

        var tokenDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YTArchivarius", "token");

        _flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = [YouTubeService.Scope.YoutubeReadonly],
            DataStore = new FileDataStore(tokenDir, fullPath: true),
        });
    }

    public bool IsConfigured => _flow is not null;

    public string RedirectUri { get; }

    private GoogleAuthorizationCodeFlow Flow =>
        _flow ?? throw new InvalidOperationException("Google client ID/secret are not configured.");

    public string BuildLoginUrl(string state)
    {
        var request = (GoogleAuthorizationCodeRequestUrl)Flow.CreateAuthorizationCodeRequest(RedirectUri);
        request.State = state;
        request.AccessType = "offline";
        // Forces a refresh token to be issued even if the app was authorized before.
        request.Prompt = "consent";
        return request.Build().AbsoluteUri;
    }

    public Task ExchangeCodeAsync(string code, CancellationToken ct) =>
        Flow.ExchangeCodeForTokenAsync(UserKey, code, RedirectUri, ct);

    public async Task<UserCredential?> GetCredentialAsync(CancellationToken ct)
    {
        if (_flow is null)
            return null;

        var token = await _flow.LoadTokenAsync(UserKey, ct);
        return token is null ? null : new UserCredential(_flow, UserKey, token);
    }

    public async Task SignOutAsync(CancellationToken ct)
    {
        if (_flow is null)
            return;

        var token = await _flow.LoadTokenAsync(UserKey, ct);
        if (token is null)
            return;

        try
        {
            await _flow.RevokeTokenAsync(UserKey, token.RefreshToken ?? token.AccessToken, ct);
        }
        catch (TokenResponseException)
        {
            // Already revoked or expired; deleting the local copy is all that is left to do.
        }

        await _flow.DeleteTokenAsync(UserKey, ct);
    }

    public Task ForgetTokenAsync(CancellationToken ct) =>
        _flow?.DeleteTokenAsync(UserKey, ct) ?? Task.CompletedTask;
}
