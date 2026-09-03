// Events for ages 18+.
export default [
  {
    id: 'lottery_ticket',
    minAge: 18,
    weight: 1,
    frequency: 0.25,
    text: 'You bought a lottery ticket on a whim.',
    effects: (ctx) => (ctx.rng.chance(0.05)
      ? { money: 25000, stats: { happiness: 20 }, log: 'You won $25,000!', tone: 'good' }
      : { money: -20, log: 'The ticket was worthless.' }),
  },
  {
    id: 'raise_offer',
    minAge: 18,
    weight: 2,
    frequency: 0.3,
    condition: (ctx) => !!ctx.char.job && ctx.char.job.years >= 2,
    text: 'Your manager hints there may be room to negotiate your pay.',
    choices: [
      {
        label: 'Negotiate hard',
        effects: (ctx) => (ctx.rng.chance(0.45 + ctx.char.stats.smarts / 300)
          ? { apply: (c) => { c.char.job.salary = Math.round(c.char.job.salary * 1.15); }, stats: { happiness: 8 }, log: 'You got a 15% raise.', tone: 'good' }
          : { stats: { happiness: -6 }, log: 'The negotiation went badly.', tone: 'bad' }),
      },
      { label: 'Accept what you have', effects: { stats: { happiness: -1 }, log: 'You let it go.' } },
    ],
  },
  {
    id: 'layoff',
    minAge: 18,
    weight: 1,
    frequency: 0.2,
    condition: (ctx) => !!ctx.char.job,
    text: 'Your company announces layoffs.',
    effects: (ctx) => (ctx.rng.chance(0.3)
      ? { apply: (c) => { c.char.job = null; }, stats: { happiness: -12 }, log: 'You were laid off.', tone: 'bad' }
      : { stats: { happiness: -3 }, log: 'You survived the layoffs.' }),
  },
  {
    id: 'health_scare',
    minAge: 35,
    weight: 1.2,
    frequency: 0.25,
    text: 'You have been feeling unwell for weeks.',
    choices: [
      { label: 'See a doctor', effects: { money: -400, stats: { health: 8 }, log: 'The doctor caught it early.', tone: 'good' } },
      { label: 'Ignore it', effects: { stats: { health: -12 }, log: 'It got worse.', tone: 'bad' } },
    ],
  },
  {
    id: 'old_friend',
    minAge: 25,
    weight: 1.5,
    frequency: 0.3,
    text: 'An old friend reaches out after years of silence.',
    choices: [
      { label: 'Meet up', effects: { stats: { happiness: 9 }, money: -60, log: 'You caught up over dinner.', tone: 'good' } },
      { label: 'Leave it on read', effects: { stats: { happiness: -2 }, log: 'You never replied.' } },
    ],
  },
  {
    id: 'retirement',
    minAge: 65,
    once: true,
    frequency: 0.8,
    condition: (ctx) => !!ctx.char.job,
    text: 'You are old enough to retire.',
    choices: [
      { label: 'Retire', effects: { apply: (c) => { c.char.job = null; }, flags: { retired: true }, stats: { happiness: 12, health: 4 }, log: 'You retired.', tone: 'good' } },
      { label: 'Keep working', effects: { stats: { happiness: -4 }, log: 'You kept clocking in.' } },
    ],
  },
];
