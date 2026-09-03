import { summarizeEffect } from '../engine/effects.js';

export default {
  name: 'EventModal',
  props: { pending: { type: Object, required: true } },
  emits: ['choose'],
  setup() {
    return { summarizeEffect };
  },
  template: `
    <div class="modal-backdrop">
      <div class="modal">
        <h3>Something happened</h3>
        <p>{{ pending.text }}</p>
        <div class="choices">
          <button
            v-for="(choice, i) in pending.choices"
            :key="i"
            @click="$emit('choose', choice)"
          >{{ choice.label }}</button>
        </div>
      </div>
    </div>
  `,
};
