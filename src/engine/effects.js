import { clamp } from './utils.js';

// An "effect" is a plain object any content file can return:
// { stats: { happiness: +5 }, money: -100, flags: { grounded: true },
//   log: 'Text', tone: 'good' | 'bad' | 'neutral' }
export function applyEffect(game, effect) {
  if (!effect) return;

  if (Array.isArray(effect)) {
    effect.forEach((e) => applyEffect(game, e));
    return;
  }

  const char = game.character;

  if (effect.stats) {
    for (const [key, delta] of Object.entries(effect.stats)) {
      char.stats[key] = clamp((char.stats[key] ?? 0) + delta, 0, 100);
    }
  }

  if (typeof effect.money === 'number') char.money += effect.money;

  if (effect.flags) Object.assign(char.flags, effect.flags);

  if (effect.set) Object.assign(char, effect.set);

  if (typeof effect.apply === 'function') effect.apply(game.context());

  if (effect.log) game.log(effect.log, effect.tone ?? 'neutral');
}

export function summarizeEffect(effect) {
  if (!effect) return '';
  const parts = [];
  for (const [key, delta] of Object.entries(effect.stats ?? {})) {
    parts.push(`${delta > 0 ? '+' : ''}${delta} ${key}`);
  }
  if (typeof effect.money === 'number' && effect.money !== 0) {
    parts.push(`${effect.money > 0 ? '+' : '-'}$${Math.abs(effect.money).toLocaleString()}`);
  }
  return parts.join(', ');
}
