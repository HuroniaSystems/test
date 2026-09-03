// Central content registry. Everything the game knows about lives here,
// registered from `src/content/index.js`. Adding content later means
// dropping a file in and exporting it from that barrel - nothing else.
const registry = {
  events: [],
  jobs: [],
  addons: [],
};

export function registerEvents(list) {
  registry.events.push(...list.filter(Boolean));
}

export function registerJobs(list) {
  registry.jobs.push(...list.filter(Boolean));
}

export function registerAddons(list) {
  registry.addons.push(...list.filter(Boolean));
}

export const allEvents = () => registry.events;
export const allJobs = () => registry.jobs;
export const allAddons = () => registry.addons;

export const findJob = (id) => registry.jobs.find((j) => j.id === id);
export const findAddon = (id) => registry.addons.find((a) => a.id === id);
