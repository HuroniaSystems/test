import { loadContent } from './content/index.js';
import { createGame } from './engine/game.js';
import App from './ui/App.js';

loadContent();

const game = createGame();

window.Vue.createApp({
  components: { App },
  setup: () => ({ game }),
  template: '<App :game="game" />',
}).mount('#app');

// Handy for tinkering in the console.
window.game = game;
