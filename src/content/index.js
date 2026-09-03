import events from './events/index.js';
import jobs from './jobs/index.js';
import addons from './addons/index.js';
import { registerEvents, registerJobs, registerAddons } from '../engine/registry.js';

export function loadContent() {
  registerEvents(events);
  registerJobs(jobs);
  registerAddons(addons);
}
