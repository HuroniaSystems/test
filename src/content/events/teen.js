// Events for ages 13-17.
export default [
  {
    id: 'first_crush',
    minAge: 13,
    maxAge: 17,
    once: true,
    text: 'You have a crush on someone in your class.',
    choices: [
      { label: 'Ask them out', effects: (ctx) => (ctx.rng.chance(0.4 + ctx.char.stats.looks / 250)
          ? { stats: { happiness: 12 }, log: 'They said yes.', tone: 'good' }
          : { stats: { happiness: -8 }, log: 'They said no.', tone: 'bad' }) },
      { label: 'Say nothing', effects: { stats: { happiness: -2 }, log: 'You kept it to yourself.' } },
    ],
  },
  {
    id: 'skip_class',
    minAge: 14,
    maxAge: 17,
    weight: 2,
    frequency: 0.35,
    text: 'Friends want you to skip class and hang out.',
    choices: [
      { label: 'Skip it', effects: { stats: { happiness: 8, smarts: -4 }, log: 'You skipped class.' } },
      { label: 'Stay in class', effects: { stats: { smarts: 4, happiness: -2 }, log: 'You stayed and took notes.' } },
    ],
  },
  {
    id: 'part_time_offer',
    minAge: 15,
    maxAge: 17,
    once: true,
    condition: (ctx) => !ctx.char.job,
    text: 'A neighbour offers you weekend yard work.',
    choices: [
      { label: 'Take the work', effects: { money: 900, stats: { health: 3, happiness: -2 }, log: 'You made some pocket money.', tone: 'good' } },
      { label: 'Decline', effects: { stats: { happiness: 3 }, log: 'You enjoyed your weekends instead.' } },
    ],
  },
  {
    id: 'dropout_pressure',
    minAge: 16,
    maxAge: 17,
    once: true,
    weight: 0.7,
    condition: (ctx) => ctx.char.stats.smarts < 40 && ctx.char.inSchool,
    text: 'School is going badly. You are thinking about dropping out.',
    choices: [
      {
        label: 'Drop out',
        effects: {
          set: { inSchool: false },
          flags: { droppedOut: true },
          stats: { happiness: 5, smarts: -5 },
          log: 'You dropped out of high school.',
          tone: 'bad',
        },
      },
      { label: 'Push through', effects: { stats: { smarts: 5, happiness: -4 }, log: 'You stuck with school.' } },
    ],
  },
];
