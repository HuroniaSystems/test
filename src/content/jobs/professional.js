// Degree-gated careers.
export default [
  {
    id: 'software_engineer',
    title: 'Junior Software Engineer',
    category: 'Professional',
    minAge: 21,
    salary: 88000,
    hireChance: 0.55,
    requires: { education: 'college', smarts: 60 },
    promotions: [
      { title: 'Software Engineer', salary: 130000 },
      { title: 'Staff Engineer', salary: 190000 },
      { title: 'Engineering Director', salary: 260000 },
    ],
  },
  {
    id: 'nurse',
    title: 'Registered Nurse',
    category: 'Professional',
    minAge: 21,
    salary: 76000,
    hireChance: 0.6,
    requires: { education: 'college', smarts: 55 },
    promotions: [
      { title: 'Charge Nurse', salary: 98000 },
      { title: 'Nurse Practitioner', salary: 140000 },
    ],
    onYear: (ctx) => {
      if (ctx.rng.chance(0.15)) {
        ctx.apply({ stats: { happiness: -4, health: -2 }, log: 'A brutal stretch of night shifts wore you down.' });
      }
    },
  },
  {
    id: 'lawyer',
    title: 'Associate Attorney',
    category: 'Professional',
    minAge: 25,
    salary: 120000,
    hireChance: 0.45,
    requires: { education: 'graduate', smarts: 70 },
    promotions: [
      { title: 'Senior Associate', salary: 180000 },
      { title: 'Partner', salary: 320000 },
    ],
  },
];
