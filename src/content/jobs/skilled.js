// Jobs requiring a high school diploma or trade experience.
export default [
  {
    id: 'electrician',
    title: 'Electrician',
    category: 'Skilled trade',
    minAge: 18,
    salary: 58000,
    hireChance: 0.6,
    requires: { education: 'highschool', smarts: 45 },
    promotions: [
      { title: 'Master Electrician', salary: 84000 },
      { title: 'Contractor', salary: 120000 },
    ],
  },
  {
    id: 'chef',
    title: 'Line Cook',
    category: 'Skilled trade',
    minAge: 18,
    salary: 40000,
    hireChance: 0.7,
    requires: { education: 'highschool' },
    promotions: [
      { title: 'Sous Chef', salary: 62000 },
      { title: 'Head Chef', salary: 95000 },
    ],
  },
];
