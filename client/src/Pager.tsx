// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

import { useState, type FormEvent } from 'react';

interface PagerProps {
  page: number;
  total: number;
  onGo: (page: number) => void;
}

function pageWindow(page: number, total: number): (number | '…')[] {
  const pages = new Set([1, total]);
  for (let p = page - 2; p <= page + 2; p++) {
    if (p >= 1 && p <= total) pages.add(p);
  }
  const sorted = [...pages].sort((a, b) => a - b);
  const result: (number | '…')[] = [];
  sorted.forEach((p, i) => {
    if (i > 0 && p - sorted[i - 1] > 1) result.push('…');
    result.push(p);
  });
  return result;
}

export function Pager({ page, total, onGo }: PagerProps) {
  const [input, setInput] = useState('');

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const target = Number(input);
    if (Number.isInteger(target) && target >= 1 && target <= total) {
      onGo(target);
      setInput('');
    }
  };

  return (
    <nav className="pager">
      <button disabled={page <= 1} onClick={() => onGo(1)} title="Newest week">«</button>
      <button disabled={page <= 1} onClick={() => onGo(page - 1)} title="Newer week (←)">‹</button>
      {pageWindow(page, total).map((p, i) =>
        p === '…' ? (
          <span key={`gap-${i}`} className="gap">…</span>
        ) : (
          <button key={p} className={p === page ? 'current' : undefined} onClick={() => onGo(p)}>
            {p}
          </button>
        ),
      )}
      <button disabled={page >= total} onClick={() => onGo(page + 1)} title="Older week (→)">›</button>
      <button disabled={page >= total} onClick={() => onGo(total)} title="Oldest week">»</button>
      <form onSubmit={submit}>
        <input
          type="number"
          min={1}
          max={total}
          placeholder="page"
          value={input}
          onChange={(e) => setInput(e.target.value)}
        />
        <button type="submit">Go</button>
      </form>
    </nav>
  );
}
