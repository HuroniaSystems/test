// Addon: purchasable assets with yearly upkeep.
const CATALOG = [
  { id: 'bike', name: 'Bicycle', price: 400, upkeep: 20, happiness: 4 },
  { id: 'used_car', name: 'Used Car', price: 6000, upkeep: 900, happiness: 8, minAge: 18 },
  { id: 'condo', name: 'Condo', price: 180000, upkeep: 6000, happiness: 16, minAge: 21 },
  { id: 'yacht', name: 'Yacht', price: 900000, upkeep: 45000, happiness: 30, minAge: 25 },
];

export default {
  id: 'assets',
  name: 'Assets',
  catalog: CATALOG,
  actions: CATALOG.map((item) => ({
    id: `buy_${item.id}`,
    label: `Buy ${item.name} ($${item.price.toLocaleString()})`,
    minAge: item.minAge ?? 0,
    cost: item.price,
    available: (ctx) => !ctx.char.assets.some((a) => a.id === item.id),
    run: (ctx) => {
      ctx.char.assets.push({ ...item, value: item.price });
      return { stats: { happiness: item.happiness }, log: `You bought a ${item.name}.`, tone: 'good' };
    },
  })),
  onYearEnd: (ctx) => {
    const upkeep = ctx.char.assets.reduce((sum, a) => sum + a.upkeep, 0);
    if (!upkeep) return;
    ctx.char.money -= upkeep;
    ctx.log(`Paid $${upkeep.toLocaleString()} in upkeep on your assets.`);
    ctx.char.assets.forEach((a) => { a.value = Math.round(a.value * 0.93); });
  },
};
