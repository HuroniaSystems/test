// Small deterministic-friendly RNG helpers. Swap `random` for a seeded
// generator later if reproducible runs are wanted.
export const random = () => Math.random();

export const randInt = (min, max) => min + Math.floor(random() * (max - min + 1));

export const chance = (p) => random() < p;

export const pick = (arr) => arr[Math.floor(random() * arr.length)];

export function weightedPick(entries, weightOf = (e) => e.weight ?? 1) {
  const total = entries.reduce((sum, e) => sum + weightOf(e), 0);
  if (total <= 0) return null;
  let roll = random() * total;
  for (const entry of entries) {
    roll -= weightOf(entry);
    if (roll <= 0) return entry;
  }
  return entries[entries.length - 1];
}
