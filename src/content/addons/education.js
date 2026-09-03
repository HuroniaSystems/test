// Addon: post-secondary education. Adds enrolment actions and advances
// the degree each year the character stays enrolled.
export default {
  id: 'education',
  name: 'Education',
  actions: [
    {
      id: 'enroll_college',
      label: 'Enrol in college ($12,000/yr)',
      minAge: 18,
      cost: 12000,
      available: (ctx) =>
        ctx.char.education === 'highschool' && !ctx.char.flags.enrolled,
      run: (ctx) => {
        ctx.char.flags.enrolled = 'college';
        ctx.char.flags.studyYears = 0;
        return { log: 'You enrolled in college.', tone: 'good', stats: { happiness: 4 } };
      },
    },
    {
      id: 'enroll_grad',
      label: 'Enrol in graduate school ($20,000/yr)',
      minAge: 21,
      cost: 20000,
      available: (ctx) =>
        ctx.char.education === 'college' && !ctx.char.flags.enrolled,
      run: (ctx) => {
        ctx.char.flags.enrolled = 'graduate';
        ctx.char.flags.studyYears = 0;
        return { log: 'You enrolled in graduate school.', tone: 'good' };
      },
    },
    {
      id: 'drop_out',
      label: 'Drop out',
      available: (ctx) => !!ctx.char.flags.enrolled,
      run: (ctx) => {
        ctx.char.flags.enrolled = null;
        return { stats: { happiness: -6 }, log: 'You dropped out of school.', tone: 'bad' };
      },
    },
  ],
  onYearEnd: (ctx) => {
    const { char } = ctx;
    if (!char.flags.enrolled) return;

    const program = char.flags.enrolled;
    const tuition = program === 'college' ? 12000 : 20000;
    const needed = program === 'college' ? 4 : 2;

    char.money -= tuition;
    char.flags.studyYears = (char.flags.studyYears ?? 0) + 1;
    ctx.apply({ stats: { smarts: 4, happiness: -2 } });

    if (char.flags.studyYears >= needed) {
      char.education = program;
      char.flags.enrolled = null;
      char.flags.studyYears = 0;
      ctx.log(
        program === 'college' ? 'You graduated college.' : 'You earned a graduate degree.',
        'good',
      );
    } else {
      ctx.log(`Completed year ${char.flags.studyYears} of ${program}.`);
    }
  },
};
