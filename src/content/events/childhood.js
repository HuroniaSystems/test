// Events for ages 0-12. Add more by appending to this array.
export default [
  {
    id: 'first_words',
    minAge: 1,
    maxAge: 2,
    once: true,
    frequency: 0.9,
    text: 'You said your first word.',
    tone: 'good',
    effects: { stats: { happiness: 4, smarts: 2 } },
  },
  {
    id: 'imaginary_friend',
    minAge: 4,
    maxAge: 8,
    once: true,
    text: 'You invented an imaginary friend who follows you everywhere.',
    choices: [
      {
        label: 'Play along',
        effects: { stats: { happiness: 6, smarts: 2 }, log: 'You had a great year of make believe.', tone: 'good' },
      },
      {
        label: 'Tell them to leave',
        effects: { stats: { happiness: -3 }, log: 'Your imaginary friend moved out.', tone: 'bad' },
      },
    ],
  },
  {
    id: 'school_bully',
    minAge: 7,
    maxAge: 12,
    once: true,
    weight: 2,
    text: 'A bigger kid at school keeps taking your lunch.',
    choices: [
      {
        label: 'Fight back',
        effects: { stats: { health: -8, happiness: 6 }, log: 'You fought back and earned some respect.', tone: 'good' },
      },
      {
        label: 'Tell a teacher',
        effects: { stats: { happiness: 2, smarts: 1 }, log: 'The teacher handled it.' },
      },
      {
        label: 'Give up the lunch',
        effects: { stats: { happiness: -6, health: -2 }, log: 'You went hungry all year.', tone: 'bad' },
      },
    ],
  },
  {
    id: 'library_card',
    minAge: 6,
    maxAge: 12,
    once: true,
    text: 'The local library offers you a free card.',
    choices: [
      { label: 'Read everything', effects: { stats: { smarts: 8, happiness: -1 }, log: 'You read the whole childrens section.', tone: 'good' } },
      { label: 'Pass', effects: { stats: { happiness: 2 }, log: 'You skipped the library.' } },
    ],
  },
  {
    id: 'broken_arm',
    minAge: 5,
    maxAge: 12,
    once: true,
    weight: 0.6,
    text: 'You fell out of a tree and broke your arm.',
    tone: 'bad',
    effects: { stats: { health: -10, happiness: -5 } },
  },
];
