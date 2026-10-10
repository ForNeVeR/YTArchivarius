<!--
SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->

# YTArchivarius

A small local web app that lists the uploads of all your YouTube subscriptions, one **week per page**
(newest week first), grouped by day, 3 videos per row. Clicking a video opens it on YouTube in a new tab,
so you can use YouTube's own "Save → Watch Later" (the YouTube Data API cannot write to Watch Later).

- Backend: ASP.NET Core (.NET 10), minimal APIs, YouTube Data API v3 via the Google .NET client.
- Frontend: React + TypeScript (Vite), in `client/`.
- Nothing is stored except the OAuth token (`%LOCALAPPDATA%\YTArchivarius\token`). Fetched data is kept in
  memory only, while the server runs.

## Why?
YouTube is very slow if you have many subscriptions and/or you want to scroll the subscription list back to several months ago. This is mostly because it tries to build a full list of all videos in your subscriptions. There is no way to filter the subscription list by date. This small web application solves the problem by listing all the videos from the channels you subscribed with per-week paging. 

## Prerequisites

- .NET 10 SDK
- Node.js 20+ and npm
- A trusted ASP.NET Core HTTPS dev certificate: `dotnet dev-certs https --trust`

## 1. Google Cloud setup (one time, free)

The YouTube Data API only accepts OAuth tokens issued to a registered client, so you need your own
OAuth client ID. Your browser's YouTube session cannot be reused.

1. Open <https://console.cloud.google.com/> and create a new project (e.g. `YTArchivarius`).
2. **APIs & Services → Library**: search for **YouTube Data API v3** and click **Enable**.
3. **Google Auth Platform** (formerly "OAuth consent screen"): click **Get started**.
   - App name: anything (e.g. `YTArchivarius`); user support email: your email.
   - Audience: **External**.
   - Contact information: your email. Accept the policy and **Create**.
4. **Google Auth Platform → Audience → Test users → Add users**: add the Google account that owns
   your YouTube subscriptions.
5. **Google Auth Platform → Clients → Create client**:
   - Application type: **Web application**.
   - Authorized redirect URIs: `https://localhost:5001/auth/callback`
   - Create, then copy the **Client ID** and **Client secret**.
6. Store them as user secrets (kept outside the repo, in your user profile):

   ```powershell
   cd YTArchivarius
   dotnet user-secrets set "Google:ClientId" "<client id>"
   dotnet user-secrets set "Google:ClientSecret" "<client secret>"
   ```

### About the 7-day sign-in expiry

While the app's publishing status is **Testing**, Google expires refresh tokens after 7 days, so you
will have to sign in again once a week (the app detects this and shows the sign-in button again).
To avoid that, go to **Google Auth Platform → Audience** and click **Publish app**. The app remains
unverified, which only means that at sign-in you click **Advanced → Go to YTArchivarius (unsafe)** once.
Since you are the only user, that is fine.

## 2. Run

Single port (build the frontend once, then run the server):

```powershell
cd client
npm install
npm run build        # outputs to YTArchivarius/wwwroot
cd ..
dotnet run --project YTArchivarius
```

Open <https://localhost:5001>, click **Sign in with Google**, and allow read-only YouTube access.

Frontend development (hot reload), two terminals:

```powershell
dotnet run --project YTArchivarius     # API on https://localhost:5001
cd client; npm run dev                 # UI on http://localhost:5173, proxies /api and /auth
```

## Using it

- Page 1 is the current week (Monday–Sunday, local time); the last page is the week containing
  `YouTube:HistoryStart` from `YTArchivarius/appsettings.json` (default `2025-03-01`).
- Navigate with the pager, the page-number box, **Jump to week containing** (date picker), or the
  **←/→** keys. The page is in the URL (`?page=N`), so the browser's back and forward buttons work.
- Opened videos get a different title color (the browser's normal visited-link styling), so you can see
  where you left off.
- **Refresh** drops the in-memory cache, so new uploads show up. **Sign out** revokes and deletes the token.

## Quota

The API has no subscription-feed endpoint, so the server pages through every subscribed channel's
uploads playlist (1 quota unit per 50 videos per channel), only as far back as the oldest week you have
opened. Weeks newer than that are served from memory for free. The default quota is 10,000 units per
day, which resets at midnight Pacific time. The footer shows how many units the running server has used.
If you hit the limit, the page shows the API's `quotaExceeded` error; wait for the reset.

## Limitations

- Only **current** subscriptions are included. Channels you unsubscribed from are not.
- Private, deleted, and members-only videos are skipped.
- Shorts and live streams are listed like any other upload.

## License
The project is distributed under the terms of [the MIT license][docs.license].

The license indication in the project's sources is compliant with the [REUSE specification v3.3][reuse.spec].

[docs.license]: LICENSE.txt
[reuse.spec]: https://reuse.software/spec/
