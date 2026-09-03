// Low stakes flavour that can fire at almost any age.
export default [
  {
    id: 'good_weather',
    weight: 0.8,
    frequency: 0.2,
    text: 'It was a beautiful year of weather.',
    effects: { stats: { happiness: 3 } },
  },
  {
    id: 'lost_wallet',
    minAge: 12,
    weight: 0.8,
    frequency: 0.2,
    text: 'You found a wallet on the sidewalk.',
    choices: [
      { label: 'Return it', effects: { stats: { happiness: 6 }, log: 'The owner was grateful.', tone: 'good' } },
      { label: 'Keep the cash', effects: { money: 220, stats: { happiness: -3 }, log: 'You pocketed the cash.' } },
    ],
  },
  {
    id: 'stray_animal',
    minAge: 8,
    weight: 0.7,
    frequency: 0.18,
    text: 'A stray cat starts sleeping on your porch.',
    choices: [
      { label: 'Adopt it', effects: { stats: { happiness: 8 }, money: -300, log: 'You adopted the cat.', tone: 'good' } },
      { label: 'Shoo it away', effects: { stats: { happiness: -2 }, log: 'The cat moved on.' } },
    ],
  },
];
