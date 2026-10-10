// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Security.Cryptography;
using Google;
using Google.Apis.Auth.OAuth2.Responses;
using YTArchivarius.Auth;
using YTArchivarius.YouTube;

const string stateCookie = "yta_oauth_state";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<GoogleAuthService>();
builder.Services.AddSingleton<FeedCache>();

var app = builder.Build();

var historyStart = DateOnly.ParseExact(
    app.Configuration["YouTube:HistoryStart"] ?? "2025-03-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

app.UseStaticFiles();

app.MapGet("/api/status", async (GoogleAuthService auth, FeedCache feed, CancellationToken ct) =>
    new StatusResponse(
        auth.IsConfigured,
        await auth.GetCredentialAsync(ct) is not null,
        historyStart,
        feed.QuotaUsed));

app.MapGet("/api/week", async (DateTimeOffset from, DateTimeOffset to, GoogleAuthService auth, FeedCache feed) =>
{
    if (to <= from || to - from > TimeSpan.FromDays(8))
        return Results.Problem(statusCode: 400, detail: "'from' must be before 'to', and the range at most 8 days.");

    // Deliberately not tied to the request's abort token: if the user pages away, the work still fills the cache.
    var credential = await auth.GetCredentialAsync(CancellationToken.None);
    if (credential is null)
        return Results.Problem(statusCode: 401, detail: "Not signed in.");

    try
    {
        var videos = await feed.GetRangeAsync(credential, from, to, CancellationToken.None);
        return Results.Ok(new WeekResponse(videos, feed.QuotaUsed));
    }
    catch (TokenResponseException ex)
    {
        // Typically an expired or revoked refresh token (Testing-mode apps expire them after 7 days).
        await auth.ForgetTokenAsync(CancellationToken.None);
        return Results.Problem(statusCode: 401,
            detail: $"Google rejected the stored token ({ex.Error?.Error}: {ex.Error?.ErrorDescription}). Sign in again.");
    }
    catch (GoogleApiException ex)
    {
        return Results.Problem(statusCode: 502, title: "YouTube API error", detail: ex.Message);
    }
});

app.MapPost("/api/refresh", (FeedCache feed) =>
{
    feed.Clear();
    return Results.NoContent();
});

app.MapGet("/auth/login", (HttpContext context, GoogleAuthService auth, string? returnUrl) =>
{
    if (!auth.IsConfigured)
        return Results.Problem(statusCode: 400, detail: "Google:ClientId / Google:ClientSecret are not configured.");

    var target = IsLocalReturnUrl(returnUrl) ? returnUrl! : "/";
    var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    context.Response.Cookies.Append(stateCookie, $"{state}|{target}", new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        MaxAge = TimeSpan.FromMinutes(10),
    });

    return Results.Redirect(auth.BuildLoginUrl(state));
});

app.MapGet("/auth/callback", async (
    HttpContext context, GoogleAuthService auth, FeedCache feed,
    string? code, string? state, string? error, CancellationToken ct) =>
{
    var cookie = context.Request.Cookies[stateCookie];
    context.Response.Cookies.Delete(stateCookie);

    if (cookie?.Split('|', 2) is not [var expectedState, var target] || state != expectedState)
        return Results.Problem(statusCode: 400, detail: "OAuth state mismatch. Start the sign-in again.");
    if (error is not null)
        return Results.Problem(statusCode: 400, detail: $"Google sign-in failed: {error}");
    if (string.IsNullOrEmpty(code))
        return Results.Problem(statusCode: 400, detail: "Google did not return an authorization code.");

    await auth.ExchangeCodeAsync(code, ct);
    feed.Clear();
    return Results.Redirect(target);
});

app.MapPost("/auth/logout", async (GoogleAuthService auth, FeedCache feed, CancellationToken ct) =>
{
    await auth.SignOutAsync(ct);
    feed.Clear();
    return Results.NoContent();
});

app.MapFallbackToFile("index.html");

app.Run();

static bool IsLocalReturnUrl(string? url)
{
    if (string.IsNullOrEmpty(url))
        return false;
    if (url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\"))
        return true;

    return Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Scheme is "http" or "https"
           && uri.IsLoopback;
}
