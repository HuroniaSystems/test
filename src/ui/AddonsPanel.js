import { allAddons } from '../engine/registry.js';
import { money, resolve } from '../engine/utils.js';

export default {
  name: 'AddonsPanel',
  props: { game: { type: Object, required: true } },
  setup() {
    return { money, resolve };
  },
  computed: {
    addons() {
      return allAddons()
        .map((addon) => ({ addon, actions: this.game.addonActions(addon) }))
        .filter((group) => group.actions.length);
    },
  },
  methods: {
    costOf(action) {
      return resolve(action.cost, this.game.context()) ?? 0;
    },
  },
  template: `
    <div class="panel" v-for="group in addons" :key="group.addon.id">
      <h3>{{ group.addon.name }}</h3>
      <div class="list-item" v-for="action in group.actions" :key="action.id">
        <span>{{ action.label }}</span>
        <button
          :disabled="costOf(action) > game.character.money"
          @click="game.runAction(action)"
        >Do it</button>
      </div>
    </div>
    <div class="panel" v-if="!addons.length">
      <div class="muted small">No actions available at your age.</div>
    </div>
  `,
};
