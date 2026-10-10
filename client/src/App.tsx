// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiError, getStatus, getWeek, loginUrl, logout, refreshCache, type Status, type Video } from './api';
import { Pager } from './Pager';
import { VideoCard } from './VideoCard';
import { addDays, isoDate, pageForDate, parseLocalDate, weekForPage } from './weeks';

const dayFormat = new Intl.DateTimeFormat('en-GB', {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  year: 'numeric',
});

function pageFromUrl(): number {
  const page = Number(new URLSearchParams(window.location.search).get('page'));
  return Number.isInteger(page) && page >= 1 ? page : 1;
}

function groupByDay(videos: Video[]): { day: string; videos: Video[] }[] {
  const groups = new Map<string, Video[]>();
  for (const video of videos) {
    const day = dayFormat.format(new Date(video.publishedAt));
    const group = groups.get(day);
    if (group) group.push(video);
    else groups.set(day, [video]);
  }
  return [...groups].map(([day, videos]) => ({ day, videos }));
}

export function App() {
  const [status, setStatus] = useState<Status | null>(null);
  const [statusError, setStatusError] = useState<string | null>(null);

  const loadStatus = useCallback(() => {
    getStatus()
      .then((s) => {
        setStatus(s);
        setStatusError(null);
      })
      .catch((e: Error) => setStatusError(e.message));
  }, []);

  useEffect(loadStatus, [loadStatus]);

  if (statusError) return <main className="notice error">Cannot reach the server: {statusError}</main>;
  if (!status) return <main className="notice">Loading…</main>;
  if (!status.configured) return <SetupNotice />;
  if (!status.signedIn) {
    return (
      <main className="notice">
        <h1>YTArchivarius</h1>
        <p>Sign in with the Google account that owns your YouTube subscriptions.</p>
        <a className="button" href={loginUrl()}>Sign in with Google</a>
      </main>
    );
  }

  return <Feed status={status} onSignedOut={loadStatus} />;
}

function Feed({ status, onSignedOut }: { status: Status; onSignedOut: () => void }) {
  const today = useMemo(() => new Date(), []);
  const total = useMemo(() => pageForDate(parseLocalDate(status.historyStart), today), [status.historyStart, today]);

  const [page, setPage] = useState(() => Math.min(pageFromUrl(), total));
  const [videos, setVideos] = useState<Video[]>([]);
  const [quotaUsed, setQuotaUsed] = useState(status.quotaUsed);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [jumpDate, setJumpDate] = useState('');

  const week = useMemo(() => weekForPage(page, today), [page, today]);

  const go = useCallback(
    (target: number) => {
      const clamped = Math.min(Math.max(target, 1), total);
      window.history.pushState(null, '', `?page=${clamped}`);
      setPage(clamped);
      window.scrollTo(0, 0);
    },
    [total],
  );

  useEffect(() => {
    const onPopState = () => setPage(Math.min(pageFromUrl(), total));
    window.addEventListener('popstate', onPopState);
    return () => window.removeEventListener('popstate', onPopState);
  }, [total]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.target instanceof HTMLInputElement || e.altKey || e.ctrlKey || e.metaKey) return;
      if (e.key === 'ArrowLeft') go(page - 1);
      if (e.key === 'ArrowRight') go(page + 1);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [go, page]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    getWeek(week.from, week.to, controller.signal)
      .then((res) => {
        setVideos(res.videos);
        setQuotaUsed(res.quotaUsed);
        setLoading(false);
      })
      .catch((e: Error) => {
        if (controller.signal.aborted) return;
        if (e instanceof ApiError && e.status === 401) {
          onSignedOut();
          return;
        }
        setVideos([]);
        setError(e.message);
        setLoading(false);
      });
    return () => controller.abort();
  }, [week, reloadKey, onSignedOut]);

  const refresh = async () => {
    await refreshCache();
    setReloadKey((k) => k + 1);
  };

  const signOut = async () => {
    await logout();
    onSignedOut();
  };

  const jump = () => {
    if (jumpDate) go(pageForDate(parseLocalDate(jumpDate), today));
  };

  const groups = groupByDay(videos);

  return (
    <div className="page">
      <header>
        <h1>YTArchivarius</h1>
        <div className="week-label">
          Week {page} of {total} · Mon {isoDate(week.from)} – Sun {isoDate(addDays(week.to, -1))}
          {!loading && !error && <> · {videos.length} videos</>}
        </div>
        <div className="actions">
          <label>
            Jump to week containing{' '}
            <input
              type="date"
              min={status.historyStart}
              max={isoDate(today)}
              value={jumpDate}
              onChange={(e) => setJumpDate(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && jump()}
            />
          </label>
          <button onClick={jump} disabled={!jumpDate}>Go</button>
          <button onClick={refresh} title="Forget cached data and fetch again from YouTube">Refresh</button>
          <button onClick={signOut}>Sign out</button>
        </div>
      </header>

      <Pager page={page} total={total} onGo={go} />

      {loading && (
        <p className="notice">Fetching… the first visit to an older week can take a few seconds.</p>
      )}
      {error && <p className="notice error">{error}</p>}
      {!loading && !error && videos.length === 0 && <p className="notice">No videos this week.</p>}

      {!loading &&
        groups.map((group) => (
          <section key={group.day}>
            <h2>
              {group.day} <span className="count">({group.videos.length})</span>
            </h2>
            <div className="grid">
              {group.videos.map((video) => (
                <VideoCard key={video.id} video={video} />
              ))}
            </div>
          </section>
        ))}

      {!loading && videos.length > 0 && <Pager page={page} total={total} onGo={go} />}

      <footer>YouTube API quota used by this server process: {quotaUsed} units (daily limit is 10,000 by default)</footer>
    </div>
  );
}

function SetupNotice() {
  return (
    <main className="notice">
      <h1>YTArchivarius: Google client not configured</h1>
      <p>
        The server has no Google OAuth client ID/secret. Follow the setup steps in <code>README.md</code>, then set
        them and restart the server:
      </p>
      <pre>
        {`cd YTArchivarius
dotnet user-secrets set "Google:ClientId" "<client id>"
dotnet user-secrets set "Google:ClientSecret" "<client secret>"`}
      </pre>
    </main>
  );
}
