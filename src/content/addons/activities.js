// Addon: repeatable yearly self-improvement actions.
export default {
  id: 'activities',
  name: 'Activities',
  actions: [
    {
      id: 'gym',
      label: 'Go to the gym ($400)',
      minAge: 12,
      cost: 400,
      run: (ctx) => ({
        stats: { health: ctx.rng.randInt(3, 8), looks: ctx.rng.randInt(1, 4) },
        log: 'You spent the year training.',
        tone: 'good',
      }),
    },
    {
      id: 'study',
      label: 'Study on your own',
      minAge: 8,
      run: (ctx) => ({
        stats: { smarts: ctx.rng.randInt(2, 6), happiness: -2 },
        log: 'You studied in your free time.',
      }),
    },
    {
      id: 'doctor',
      label: 'Visit the doctor ($250)',
      minAge: 5,
      cost: 250,
      run: (ctx) => ({
        stats: { health: ctx.rng.randInt(4, 10) },
        log: 'A checkup got you back on track.',
        tone: 'good',
      }),
    },
    {
      id: 'vacation',
      label: 'Take a vacation ($2,000)',
      minAge: 16,
      cost: 2000,
      run: () => ({ stats: { happiness: 14, health: 2 }, log: 'You took a real vacation.', tone: 'good' }),
    },
    {
      id: 'meditate',
      label: 'Meditate',
      minAge: 10,
      run: () => ({ stats: { happiness: 4, health: 1 }, log: 'You meditated daily.' }),
    },
  ],
};
