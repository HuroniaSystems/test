export const STATS = [
  { key: 'health', label: 'Health' },
  { key: 'happiness', label: 'Happiness' },
  { key: 'smarts', label: 'Smarts' },
  { key: 'looks', label: 'Looks' },
];

export const EDUCATION_LEVELS = ['none', 'highschool', 'college', 'graduate'];

export const EDUCATION_LABELS = {
  none: 'No diploma',
  highschool: 'High school',
  college: 'College degree',
  graduate: 'Graduate degree',
};

export const LIFE_STAGES = [
  { key: 'infant', label: 'Infant', maxAge: 4 },
  { key: 'child', label: 'Child', maxAge: 12 },
  { key: 'teen', label: 'Teen', maxAge: 17 },
  { key: 'adult', label: 'Adult', maxAge: 64 },
  { key: 'senior', label: 'Senior', maxAge: 999 },
];

export const stageForAge = (age) => LIFE_STAGES.find((s) => age <= s.maxAge).key;
