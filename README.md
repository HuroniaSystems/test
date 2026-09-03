# LifeSim

A BitLife-style life simulator built with Vue 3 and plain ES modules. No build
step: open it through any static server and it runs.

```bash
npx serve .        # or: python3 -m http.server
```

## Layout

```
index.html            entry page, loads vendored Vue + src/main.js
vendor/               Vue 3 global build (no CDN needed)
src/engine/           game rules, no content
  game.js             the year loop, events, jobs, actions, death
  registry.js         where all content is registered
  character.js        character shape + creation
  effects.js          the effect object format and how it is applied
  constants.js        stats, education levels, life stages
  rng.js utils.js     helpers
src/content/          all game content, one concern per file
  events/             childhood.js, teen.js, adult.js, random.js
  jobs/               entry.js, skilled.js, professional.js
  addons/             education.js, activities.js, assets.js,
                      relationships.js, crime.js
src/ui/               Vue components
```

## Adding content

Everything is data. Write a file, export it from the matching `index.js`
barrel, and it is live.

### Event

```js
// src/content/events/adult.js
{
  id: 'unique_id',
  minAge: 18, maxAge: 40,   // optional bounds
  once: true,               // fire at most once per life
  weight: 2,                // relative pick weight
  frequency: 0.4,           // chance it fires once picked
  condition: (ctx) => !!ctx.char.job,
  text: 'Something happened.',
  // Either a plain effect...
  effects: { stats: { happiness: 5 }, money: -100, log: 'Result.', tone: 'good' },
  // ...or choices, which open a modal.
  choices: [
    { label: 'Do it', effects: (ctx) => ({ stats: { health: -5 } }) },
  ],
}
```

### Job

```js
{
  id: 'barista', title: 'Barista', category: 'Entry level',
  minAge: 16, salary: 26000, hireChance: 0.9,
  requires: { education: 'highschool', smarts: 45 },
  promotions: [{ title: 'Shift Lead', salary: 34000 }],
  onHire: (ctx) => {},
  onYear: (ctx) => {},
}
```

### Addon

An addon is a plugin: a group of player actions plus optional lifecycle hooks.

```js
export default {
  id: 'gym_pass', name: 'Gym',
  onCreate: (ctx) => {},
  onYearStart: (ctx) => {},
  onYearEnd: (ctx) => {},
  actions: [
    {
      id: 'lift', label: 'Lift weights ($400)',
      minAge: 12, cost: 400,
      available: (ctx) => true,
      run: (ctx) => ({ stats: { health: 5 }, log: 'You lifted.' }),
    },
  ],
};
```

### The context object

Every hook receives the same `ctx`:

| field | what it is |
| --- | --- |
| `ctx.char` | the character (stats, money, job, flags, assets, relationships) |
| `ctx.game` | the game instance, for anything lower level |
| `ctx.stage` | `infant` / `child` / `teen` / `adult` / `senior` |
| `ctx.log(text, tone)` | write to the life log |
| `ctx.apply(effect)` | apply an effect object immediately |
| `ctx.rng` | `chance(p)`, `randInt(min, max)` |

### Effect object

```js
{
  stats: { health: -5 },      // clamped 0-100
  money: -250,
  flags: { retired: true },   // free-form character flags
  set: { inSchool: false },   // direct character fields
  apply: (ctx) => {},         // escape hatch
  log: 'Text for the log',
  tone: 'good' | 'bad' | 'neutral',
}
```

`window.game` is exposed in the console for poking at a run.

## Not built yet

Save/load, jail and legal consequences, children, illness lines, asset sales
and a stock market. The registry and effect format are the seams for all of it.
