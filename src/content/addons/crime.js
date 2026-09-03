// Addon: high risk, high reward actions.
export default {
  id: 'crime',
  name: 'Crime',
  actions: [
    {
      id: 'pickpocket',
      label: 'Pickpocket someone',
      minAge: 12,
      run: (ctx) => {
        if (ctx.rng.chance(0.65)) {
          const take = ctx.rng.randInt(30, 400);
          return { money: take, stats: { happiness: 3 }, log: `You lifted $${take} from a stranger.` };
        }
        return { stats: { happiness: -8, health: -3 }, money: -200, log: 'You were caught and fined.', tone: 'bad' };
      },
    },
    {
      id: 'burglary',
      label: 'Break into a house',
      minAge: 16,
      run: (ctx) => {
        if (ctx.rng.chance(0.45)) {
          const take = ctx.rng.randInt(1000, 15000);
          return { money: take, log: `You got away with $${take.toLocaleString()}.`, tone: 'good' };
        }
        ctx.char.flags.criminalRecord = true;
        return { stats: { happiness: -18, health: -6 }, log: 'You were arrested and now have a record.', tone: 'bad' };
      },
    },
  ],
};
