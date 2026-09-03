import { randInt, pick } from './rng.js';

const FIRST_NAMES = ['Alex', 'Jordan', 'Sam', 'Riley', 'Casey', 'Morgan', 'Avery', 'Quinn', 'Rowan', 'Sky'];
const LAST_NAMES = ['Reed', 'Hart', 'Vance', 'Cole', 'Diaz', 'Okafor', 'Nakamura', 'Bauer', 'Lindqvist', 'Rossi'];
const COUNTRIES = ['United States', 'Canada', 'United Kingdom', 'Germany', 'Japan', 'Brazil'];

export function createCharacter(options = {}) {
  return {
    name: options.name || `${pick(FIRST_NAMES)} ${pick(LAST_NAMES)}`,
    country: options.country || pick(COUNTRIES),
    age: 0,
    alive: true,
    causeOfDeath: null,
    money: 0,
    stats: {
      health: randInt(70, 100),
      happiness: randInt(60, 100),
      smarts: randInt(20, 90),
      looks: randInt(20, 90),
    },
    education: 'none',
    inSchool: true,
    job: null, // { id, title, salary, years, level }
    // Free-form buckets content files can write into.
    flags: {},
    tags: [],
    assets: [],
    relationships: [],
    // Ids of `once: true` events already consumed.
    seenEvents: [],
  };
}

export const randomName = () => `${pick(FIRST_NAMES)} ${pick(LAST_NAMES)}`;
export { COUNTRIES };
