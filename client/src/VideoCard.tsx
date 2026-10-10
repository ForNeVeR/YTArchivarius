// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

import type { Video } from './api';

function formatDuration(totalSeconds: number): string {
  const h = Math.floor(totalSeconds / 3600);
  const m = Math.floor((totalSeconds % 3600) / 60);
  const s = totalSeconds % 60;
  const mm = h > 0 ? String(m).padStart(2, '0') : String(m);
  return `${h > 0 ? `${h}:` : ''}${mm}:${String(s).padStart(2, '0')}`;
}

const timeFormat = new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit' });

export function VideoCard({ video }: { video: Video }) {
  const watchUrl = `https://www.youtube.com/watch?v=${video.id}`;
  const channelUrl = `https://www.youtube.com/channel/${video.channelId}`;
  const thumbnail = video.thumbnailUrl ?? `https://i.ytimg.com/vi/${video.id}/mqdefault.jpg`;

  return (
    <article className="card">
      <a className="thumb" href={watchUrl} target="_blank" rel="noreferrer">
        <img src={thumbnail} alt="" loading="lazy" />
        {video.durationSeconds != null && (
          <span className="duration">{formatDuration(video.durationSeconds)}</span>
        )}
        {video.liveStatus !== 'none' && (
          <span className={`badge ${video.liveStatus}`}>{video.liveStatus.toUpperCase()}</span>
        )}
      </a>
      <div className="meta">
        <a className="title" href={watchUrl} target="_blank" rel="noreferrer" title={video.title}>
          {video.title}
        </a>
        <div className="sub">
          <a href={channelUrl} target="_blank" rel="noreferrer">{video.channelTitle}</a>
          {' · '}
          <time dateTime={video.publishedAt}>{timeFormat.format(new Date(video.publishedAt))}</time>
        </div>
      </div>
    </article>
  );
}
