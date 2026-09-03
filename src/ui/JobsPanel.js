import { money } from '../engine/utils.js';

export default {
  name: 'JobsPanel',
  props: { game: { type: Object, required: true } },
  setup() {
    return { money };
  },
  computed: {
    char() {
      return this.game.character;
    },
    grouped() {
      const groups = {};
      for (const job of this.game.availableJobs()) {
        const key = job.category ?? 'Other';
        (groups[key] ??= []).push(job);
      }
      return groups;
    },
  },
  template: `
    <div class="panel" v-if="char.job">
      <h3>Current job</h3>
      <div class="row spread">
        <div>
          <div>{{ char.job.title }}</div>
          <div class="muted small">{{ money(char.job.salary) }}/yr · {{ char.job.years }} yr(s)</div>
        </div>
        <div class="row">
          <button @click="game.promote()">Ask for promotion</button>
          <button @click="game.quitJob()">Quit</button>
        </div>
      </div>
    </div>

    <div class="panel">
      <h3>Job listings</h3>
      <div v-if="!Object.keys(grouped).length" class="muted small">
        No jobs are open to you yet. Get older, or get educated.
      </div>
      <div v-for="(jobs, category) in grouped" :key="category" style="margin-top:10px">
        <div class="muted small">{{ category }}</div>
        <div class="list-item" v-for="job in jobs" :key="job.id">
          <div>
            <div>{{ job.title }}</div>
            <div class="muted small">{{ money(job.salary) }}/yr</div>
          </div>
          <button :disabled="char.job && char.job.id === job.id" @click="game.applyForJob(job)">Apply</button>
        </div>
      </div>
    </div>
  `,
};
