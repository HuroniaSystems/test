import { randomName } from '../../engine/character.js';

// Addon: simple relationship list with per-relationship interactions.
export default {
  id: 'relationships',
  name: 'Relationships',
  onCreate: (ctx) => {
    ctx.char.relationships.push(
      { id: 'mother', name: randomName(), type: 'Mother', closeness: 80 },
      { id: 'father', name: randomName(), type: 'Father', closeness: 75 },
    );
  },
  actions: [
    {
      id: 'make_friend',
      label: 'Make a new friend',
      minAge: 5,
      run: (ctx) => {
        if (!ctx.rng.chance(0.55 + ctx.char.stats.looks / 400)) {
          return { stats: { happiness: -2 }, log: 'You tried to make a friend and it fell flat.' };
        }
        const friend = { id: `friend_${Date.now()}`, name: randomName(), type: 'Friend', closeness: ctx.rng.randInt(40, 70) };
        ctx.char.relationships.push(friend);
        return { stats: { happiness: 6 }, log: `You became friends with ${friend.name}.`, tone: 'good' };
      },
    },
    {
      id: 'find_partner',
      label: 'Look for a partner',
      minAge: 16,
      available: (ctx) => !ctx.char.relationships.some((r) => r.type === 'Partner'),
      run: (ctx) => {
        if (!ctx.rng.chance(0.4 + ctx.char.stats.looks / 300)) {
          return { stats: { happiness: -3 }, log: 'You struck out on the dating scene.' };
        }
        const partner = { id: `partner_${Date.now()}`, name: randomName(), type: 'Partner', closeness: ctx.rng.randInt(50, 85) };
        ctx.char.relationships.push(partner);
        return { stats: { happiness: 14 }, log: `You started dating ${partner.name}.`, tone: 'good' };
      },
    },
    {
      id: 'spend_time',
      label: 'Spend time with family',
      run: (ctx) => {
        ctx.char.relationships.forEach((r) => {
          r.closeness = Math.min(100, r.closeness + ctx.rng.randInt(3, 9));
        });
        return { stats: { happiness: 5 }, log: 'You spent the year close to the people around you.', tone: 'good' };
      },
    },
  ],
  onYearEnd: (ctx) => {
    ctx.char.relationships.forEach((r) => {
      r.closeness = Math.max(0, r.closeness - ctx.rng.randInt(0, 3));
    });
  },
};
