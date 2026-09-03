import { COUNTRIES, randomName } from '../engine/character.js';

export default {
  name: 'StartScreen',
  emits: ['start'],
  data() {
    return { name: randomName(), country: COUNTRIES[0], COUNTRIES };
  },
  template: `
    <div class="panel">
      <h1>LifeSim</h1>
      <p class="muted small">A modular life simulator. Age up, make choices, see where you end up.</p>
      <div class="row wrap" style="margin-top:12px">
        <input v-model="name" placeholder="Name" />
        <select v-model="country">
          <option v-for="c in COUNTRIES" :key="c" :value="c">{{ c }}</option>
        </select>
        <button @click="name = randomName()">Random name</button>
      </div>
      <button class="primary" style="margin-top:14px" @click="$emit('start', { name, country })">
        Start life
      </button>
    </div>
  `,
  methods: { randomName },
};
