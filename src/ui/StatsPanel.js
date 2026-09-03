import { STATS, EDUCATION_LABELS } from '../engine/constants.js';
import { money } from '../engine/utils.js';

export default {
  name: 'StatsPanel',
  props: { game: { type: Object, required: true } },
  setup() {
    return { STATS, EDUCATION_LABELS, money };
  },
  template: `
    <div class="panel">
      <div class="row spread">
        <div>
          <h2>{{ char.name }}</h2>
          <div class="muted small">
            Age {{ char.age }} · {{ char.country }} · {{ EDUCATION_LABELS[char.education] }}
          </div>
        </div>
        <div style="text-align:right">
          <div><strong>{{ money(char.money) }}</strong></div>
          <div class="muted small">{{ char.job ? char.job.title : 'Unemployed' }}</div>
        </div>
      </div>

      <div style="margin-top:12px">
        <div class="stat" v-for="stat in STATS" :key="stat.key">
          <div class="row spread small">
            <span>{{ stat.label }}</span>
            <span class="muted">{{ char.stats[stat.key] }}</span>
          </div>
          <div class="bar"><i :style="{ width: char.stats[stat.key] + '%' }"></i></div>
        </div>
      </div>
    </div>
  `,
  computed: {
    char() {
      return this.game.character;
    },
  },
};
