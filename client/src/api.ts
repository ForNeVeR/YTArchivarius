// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

export interface Status {
  configured: boolean;
  signedIn: boolean;
  historyStart: string; // yyyy-MM-dd
  quotaUsed: number;
}

export interface Video {
  id: string;
  title: string;
  channelId: string;
  channelTitle: string;
  publishedAt: string;
  thumbnailUrl: string | null;
  durationSeconds: number | null;
  liveStatus: 'none' | 'live' | 'upcoming' | string;
}

export interface WeekResponse {
  videos: Video[];
  quotaUsed: number;
}

export class ApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message);
  }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init);
  if (!res.ok) {
    let detail = res.statusText;
    try {
      const body = await res.json();
      detail = [body.title, body.detail].filter(Boolean).join(': ') || detail;
    } catch {
      // Not a problem-details body; keep the status text.
    }
    throw new ApiError(res.status, `HTTP ${res.status} ${detail}`);
  }
  return (res.status === 204 ? undefined : await res.json()) as T;
}

export const getStatus = () => request<Status>('/api/status');

export const getWeek = (from: Date, to: Date, signal: AbortSignal) =>
  request<WeekResponse>(
    `/api/week?from=${encodeURIComponent(from.toISOString())}&to=${encodeURIComponent(to.toISOString())}`,
    { signal },
  );

export const refreshCache = () => request<void>('/api/refresh', { method: 'POST' });

export const logout = () => request<void>('/auth/logout', { method: 'POST' });

export const loginUrl = () => `/auth/login?returnUrl=${encodeURIComponent(window.location.href)}`;
