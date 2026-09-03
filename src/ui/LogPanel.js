export default {
  name: 'LogPanel',
  props: { entries: { type: Array, required: true } },
  template: `
    <div class="panel log">
      <h3>Life log</h3>
      <div v-if="!entries.length" class="muted small">Nothing has happened yet.</div>
      <div class="entry" v-for="entry in entries" :key="entry.id">
        <div class="age">Age {{ entry.age }}</div>
        <div :class="entry.tone">{{ entry.text }}</div>
      </div>
    </div>
  `,
};
