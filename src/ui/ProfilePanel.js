import { money } from '../engine/utils.js';

export default {
  name: 'ProfilePanel',
  props: { game: { type: Object, required: true } },
  setup() {
    return { money };
  },
  computed: {
    char() {
      return this.game.character;
    },
    netWorth() {
      return this.char.money + this.char.assets.reduce((sum, a) => sum + a.value, 0);
    },
  },
  template: `
    <div class="panel">
      <h3>Relationships</h3>
      <div v-if="!char.relationships.length" class="muted small">Nobody yet.</div>
      <div class="list-item" v-for="rel in char.relationships" :key="rel.id">
        <div>
          <div>{{ rel.name }}</div>
          <div class="muted small">{{ rel.type }}</div>
        </div>
        <span class="muted small">{{ rel.closeness }}% close</span>
      </div>
    </div>

    <div class="panel">
      <h3>Assets</h3>
      <div class="muted small">Net worth: {{ money(netWorth) }}</div>
      <div v-if="!char.assets.length" class="muted small" style="margin-top:8px">You own nothing.</div>
      <div class="list-item" v-for="asset in char.assets" :key="asset.id">
        <span>{{ asset.name }}</span>
        <span class="muted small">{{ money(asset.value) }}</span>
      </div>
    </div>
  `,
};
