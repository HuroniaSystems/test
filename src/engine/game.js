import { createCharacter } from './character.js';
import { applyEffect } from './effects.js';
import { allAddons, allEvents, allJobs, findJob } from './registry.js';
import { chance, randInt, weightedPick } from './rng.js';
import { resolve, clamp } from './utils.js';
import { stageForAge } from './constants.js';

const { reactive } = window.Vue;

export function createGame() {
  const state = reactive({
    started: false,
    character: null,
    entries: [],
    pendingEvent: null, // { event, text, choices }
    tab: 'life',
  });

  const game = {
    state,

    get character() {
      return state.character;
    },

    get stage() {
      return stageForAge(state.character?.age ?? 0);
    },

    // Every content hook receives this. Keep it stable - content files
    // are written against it.
    context() {
      return {
        game,
        char: state.character,
        stage: game.stage,
        log: game.log,
        apply: (effect) => applyEffect(game, effect),
        rng: { chance, randInt },
      };
    },

    log(text, tone = 'neutral') {
      if (!text) return;
      state.entries.unshift({
        id: `${state.entries.length}-${Math.random().toString(36).slice(2, 7)}`,
        age: state.character?.age ?? 0,
        text,
        tone,
      });
    },

    start(options = {}) {
      state.character = createCharacter(options);
      state.entries = [];
      state.pendingEvent = null;
      state.started = true;
      state.tab = 'life';
      game.log(
        `${state.character.name} was born in ${state.character.country}.`,
        'good',
      );
      for (const addon of allAddons()) addon.onCreate?.(game.context());
    },

    // --- yearly tick -------------------------------------------------

    ageUp() {
      const char = state.character;
      if (!char?.alive || state.pendingEvent) return;

      char.age += 1;
      const ctx = game.context();

      for (const addon of allAddons()) addon.onYearStart?.(ctx);

      game.tickHealth();
      game.tickSchool();
      game.tickJob();

      for (const addon of allAddons()) addon.onYearEnd?.(ctx);

      game.runEvents();

      if (char.alive) game.checkDeath();
    },

    tickHealth() {
      const char = state.character;
      const decay = char.age < 30 ? 0 : Math.floor((char.age - 30) / 12) + 1;
      if (decay > 0) {
        char.stats.health = clamp(char.stats.health - decay, 0, 100);
      }
    },

    tickSchool() {
      const char = state.character;
      if (char.age === 5) {
        char.inSchool = true;
        game.log('Started primary school.');
      }
      if (char.inSchool && char.age >= 6 && char.age <= 17) {
        char.stats.smarts = clamp(char.stats.smarts + randInt(0, 2), 0, 100);
      }
      if (char.age === 18 && char.inSchool && char.education === 'none') {
        char.education = 'highschool';
        char.inSchool = false;
        game.log('Graduated high school.', 'good');
      }
    },

    tickJob() {
      const char = state.character;
      if (!char.job) return;
      const job = findJob(char.job.id);
      char.job.years += 1;
      char.money += char.job.salary;
      game.log(
        `Worked as a ${char.job.title} and earned $${char.job.salary.toLocaleString()}.`,
      );
      job?.onYear?.(game.context());
    },

    // --- events ------------------------------------------------------

    eligibleEvents() {
      const ctx = game.context();
      const char = state.character;
      return allEvents().filter((event) => {
        if (event.once && char.seenEvents.includes(event.id)) return false;
        if (event.minAge != null && char.age < event.minAge) return false;
        if (event.maxAge != null && char.age > event.maxAge) return false;
        if (event.stage && event.stage !== game.stage) return false;
        if (event.condition && !event.condition(ctx)) return false;
        return true;
      });
    },

    runEvents() {
      const pool = game.eligibleEvents();
      if (!pool.length) return;

      const event = weightedPick(pool);
      if (!event) return;
      if (!chance(event.frequency ?? 0.55)) return;

      game.triggerEvent(event);
    },

    triggerEvent(event) {
      const ctx = game.context();
      state.character.seenEvents.push(event.id);

      const text = resolve(event.text, ctx);
      const choices = resolve(event.choices, ctx);

      if (choices?.length) {
        state.pendingEvent = { event, text, choices };
        return;
      }

      game.log(text, event.tone ?? 'neutral');
      applyEffect(game, resolve(event.effects, ctx));
    },

    chooseOption(choice) {
      const pending = state.pendingEvent;
      if (!pending) return;
      const ctx = game.context();

      game.log(`${pending.text} → ${choice.label}`);
      applyEffect(game, resolve(choice.effects, ctx));

      state.pendingEvent = null;
      if (state.character.alive) game.checkDeath();
    },

    // --- jobs --------------------------------------------------------

    availableJobs() {
      const ctx = game.context();
      return allJobs().filter((job) => {
        const char = state.character;
        if (char.age < (job.minAge ?? 16)) return false;
        if (job.requires?.education) {
          const order = ['none', 'highschool', 'college', 'graduate'];
          if (order.indexOf(char.education) < order.indexOf(job.requires.education)) return false;
        }
        if (job.requires?.smarts && char.stats.smarts < job.requires.smarts) return false;
        if (job.requires?.looks && char.stats.looks < job.requires.looks) return false;
        if (job.available && !job.available(ctx)) return false;
        return true;
      });
    },

    applyForJob(job) {
      const char = state.character;
      const odds = job.hireChance ?? 0.7;
      if (!chance(odds)) {
        game.log(`Applied to be a ${job.title} and was rejected.`, 'bad');
        return false;
      }
      char.job = { id: job.id, title: job.title, salary: job.salary, years: 0, level: 0 };
      game.log(`Hired as a ${job.title} for $${job.salary.toLocaleString()}/yr.`, 'good');
      job.onHire?.(game.context());
      return true;
    },

    quitJob() {
      const char = state.character;
      if (!char.job) return;
      game.log(`Quit job as a ${char.job.title}.`);
      char.job = null;
    },

    promote() {
      const char = state.character;
      if (!char.job) return;
      const job = findJob(char.job.id);
      const next = job?.promotions?.[char.job.level];
      if (!next) {
        game.log('There is nowhere left to climb at this job.');
        return;
      }
      const odds = 0.35 + char.job.years * 0.08 + char.stats.smarts / 400;
      if (!chance(Math.min(odds, 0.9))) {
        game.log('Asked for a promotion and was turned down.', 'bad');
        return;
      }
      char.job.level += 1;
      char.job.title = next.title;
      char.job.salary = next.salary;
      game.log(`Promoted to ${next.title} ($${next.salary.toLocaleString()}/yr).`, 'good');
    },

    // --- addon actions -----------------------------------------------

    addonActions(addon) {
      const ctx = game.context();
      return (addon.actions ?? []).filter((action) => {
        if (action.minAge != null && ctx.char.age < action.minAge) return false;
        if (action.maxAge != null && ctx.char.age > action.maxAge) return false;
        if (action.available && !action.available(ctx)) return false;
        return true;
      });
    },

    runAction(action) {
      const ctx = game.context();
      const char = state.character;
      const cost = resolve(action.cost, ctx) ?? 0;
      if (cost > char.money) {
        game.log(`Cannot afford ${action.label}.`, 'bad');
        return;
      }
      if (cost) char.money -= cost;
      applyEffect(game, action.run(ctx));
      if (char.alive) game.checkDeath();
    },

    // --- death -------------------------------------------------------

    checkDeath() {
      const char = state.character;
      const ageRisk = Math.max(0, (char.age - 55) / 90) ** 3;
      const healthRisk = char.stats.health <= 0
        ? 1
        : char.stats.health < 40
          ? (40 - char.stats.health) / 600
          : 0;
      if (chance(ageRisk + healthRisk)) {
        game.kill(char.stats.health <= 20 ? 'poor health' : 'natural causes');
      }
    },

    kill(cause) {
      const char = state.character;
      char.alive = false;
      char.causeOfDeath = cause;
      state.pendingEvent = null;
      game.log(`${char.name} died at age ${char.age} of ${cause}.`, 'bad');
    },

    reset() {
      state.started = false;
      state.character = null;
      state.pendingEvent = null;
      state.entries = [];
    },
  };

  return game;
}
