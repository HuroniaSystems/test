import StartScreen from './StartScreen.js';
import StatsPanel from './StatsPanel.js';
import LogPanel from './LogPanel.js';
import JobsPanel from './JobsPanel.js';
import AddonsPanel from './AddonsPanel.js';
import ProfilePanel from './ProfilePanel.js';
import EventModal from './EventModal.js';

const TABS = [
  { key: 'life', label: 'Life' },
  { key: 'jobs', label: 'Jobs' },
  { key: 'addons', label: 'Actions' },
  { key: 'profile', label: 'Profile' },
];

export default {
  name: 'App',
  components: { StartScreen, StatsPanel, LogPanel, JobsPanel, AddonsPanel, ProfilePanel, EventModal },
  props: { game: { type: Object, required: true } },
  setup() {
    return { TABS };
  },
  computed: {
    state() {
      return this.game.state;
    },
    char() {
      return this.game.character;
    },
  },
  template: `
    <StartScreen v-if="!state.started" @start="game.start($event)" />

    <template v-else>
      <StatsPanel :game="game" />

      <div class="tabs">
        <button
          v-for="tab in TABS"
          :key="tab.key"
          :class="{ active: state.tab === tab.key }"
          @click="state.tab = tab.key"
        >{{ tab.label }}</button>
      </div>

      <LogPanel v-if="state.tab === 'life'" :entries="state.entries" />
      <JobsPanel v-else-if="state.tab === 'jobs'" :game="game" />
      <AddonsPanel v-else-if="state.tab === 'addons'" :game="game" />
      <ProfilePanel v-else :game="game" />

      <div class="bottom-bar">
        <div class="inner row spread">
          <div class="small muted" v-if="char.alive">Age {{ char.age }}</div>
          <div class="small bad" v-else>Died at {{ char.age }} of {{ char.causeOfDeath }}</div>
          <button v-if="char.alive" class="primary" :disabled="!!state.pendingEvent" @click="game.ageUp()">
            Age up
          </button>
          <button v-else class="primary" @click="game.reset()">New life</button>
        </div>
      </div>

      <EventModal
        v-if="state.pendingEvent"
        :pending="state.pendingEvent"
        @choose="game.chooseOption($event)"
      />
    </template>
  `,
};
