// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

// Weeks run Monday 00:00 to the next Monday 00:00, in the browser's local time.
// Page 1 is the current week; page N is N-1 weeks earlier.

const WEEK_MS = 7 * 24 * 60 * 60 * 1000;

export function startOfWeek(date: Date): Date {
  const result = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  const daysSinceMonday = (result.getDay() + 6) % 7;
  result.setDate(result.getDate() - daysSinceMonday);
  return result;
}

export function addDays(date: Date, days: number): Date {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  return result;
}

/** Parses "yyyy-MM-dd" as a local date (Date's own parser would treat it as UTC). */
export function parseLocalDate(value: string): Date {
  const [y, m, d] = value.split('-').map(Number);
  return new Date(y, m - 1, d);
}

// Math.round absorbs the one-hour DST shifts between two local Mondays.
export function pageForDate(date: Date, today: Date): number {
  return Math.round((startOfWeek(today).getTime() - startOfWeek(date).getTime()) / WEEK_MS) + 1;
}

export function weekForPage(page: number, today: Date): { from: Date; to: Date } {
  const from = addDays(startOfWeek(today), -7 * (page - 1));
  return { from, to: addDays(from, 7) };
}

export function isoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
