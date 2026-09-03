export const clamp = (n, min, max) => Math.min(max, Math.max(min, n));

export const money = (n) =>
  (n < 0 ? '-' : '') + '$' + Math.abs(Math.round(n)).toLocaleString();

export const resolve = (value, ctx) =>
  typeof value === 'function' ? value(ctx) : value;
