// Jobs with no education requirement.
export default [
  {
    id: 'barista',
    title: 'Barista',
    category: 'Entry level',
    minAge: 16,
    salary: 26000,
    hireChance: 0.9,
    promotions: [
      { title: 'Shift Lead', salary: 34000 },
      { title: 'Store Manager', salary: 52000 },
    ],
  },
  {
    id: 'delivery_driver',
    title: 'Delivery Driver',
    category: 'Entry level',
    minAge: 18,
    salary: 32000,
    hireChance: 0.85,
    promotions: [{ title: 'Route Supervisor', salary: 46000 }],
    onYear: (ctx) => {
      if (ctx.rng.chance(0.08)) {
        ctx.apply({ stats: { health: -6 }, log: 'You had a minor accident on your route.', tone: 'bad' });
      }
    },
  },
  {
    id: 'retail_clerk',
    title: 'Retail Clerk',
    category: 'Entry level',
    minAge: 16,
    salary: 24000,
    hireChance: 0.95,
    promotions: [{ title: 'Assistant Manager', salary: 38000 }],
  },
];
