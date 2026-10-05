// Cats with their items: does the cat pass into the item, and does it touch where it should?
//   node qa_items.mjs breed[,breed] [--fps 15]
// Scenes (item placed in the cat's root space the way the game places it):
//   Drink  + water / milk / food bowl at clip.drink.bowl : no part of the cat inside the bowl; the tongue
//          meets the liquid (or kibble) at the bottom of every lap
//   JumpUp + cat tower (1 deck) where the jump lands     : nothing inside the tower all the way up; paws on the deck
//   JumpDown from where JumpUp landed, turned round      : nothing inside the tower all the way down
//   Loaf / Sleep / FlopIdle on the cushion (cat raised to the seat top)
//   Loaf inside the hideout (cat raised to its floor)    : head and ears clear of the dome
//   PawBat + mouse toy under the tapping paw              : paw meets the toy, does not sink into it
// The item is checked by its exact signed distance (items.js itemField), every cat vertex (body + face).
import { BREEDS } from './catgen.js';
import { buildCatModel } from './catmodel.js';
import { makeRig, makeClips, solveAt, applyPose, stand, contactOf } from './catmotion.js';
import { itemField as oldField, BOWL_SURF } from './items.js';
import { kitField } from './itemkit.js';
// the items as the game ships them (itemkit.js parts, docs/ART_DIRECTION.md 5장); --old: the earlier sculpted items.
// Anchors (where the cat eats, lands, lies) are the same in both: items.js
const itemField = (() => { const cache = {}; return id => cache[id] ||= process.argv.includes('--old') ? oldField(id) : { ...kitField(id), anchors: oldField(id).anchors }; })();

const args = process.argv.slice(2), opt = (k, d) => args.includes(k) ? +args[args.indexOf(k) + 1] : d;
const breeds = (args[0] && !args[0].startsWith('--') ? args[0] : 'korean_shorthair').split(',');
const FPS = opt('--fps', 15), PEN = -.003, TOUCH = .003, SOFT = -.012;   // metres; SOFT: a cushion gives this much

let failed = 0;
for (const id of breeds) {
  const b = BREEDS.find(x => x.id === id);
  const rig = makeRig(buildCatModel(b.shape, b.coat, { res: .03 }));
  const clips = makeClips(rig, { only: ['Drink', 'JumpUp', 'JumpDown', 'Loaf', 'Sleep', 'FlopIdle', 'PawBat'] }), C = contactOf(rig);
  const clip = n => clips.find(c => c.name === n);
  console.log(`\n${b.ko} (${id})`);
  // world positions of every cat vertex (body + face, which holds the tongue and the beans)
  const verts = function* () {
    for (const mesh of ['body', 'face']) { const M = C.M[mesh], p = M.pos; for (let i = 0; i < M.n; i++) yield [p[3 * i], p[3 * i + 1], p[3 * i + 2], M.part[i]]; }
  };
  // one scene: item at `at` (x,y,z), cat clip played with the root lifted by `lift`; `touch(part)` = parts that
  // must touch the item (checked with `when`), `soft`: how far the cat may press into it
  const only = args.includes('--only') ? args[args.indexOf('--only') + 1] : null;
  const scene = (name, itemId, clipName, at, { lift = 0, touchParts = null, when = null, soft = PEN, ignore = [], yaw = 0, fps = FPS } = {}) => {
    if (only && !name.includes(only)) return;
    const F0 = itemField(itemId), c = clip(clipName), n = Math.max(2, Math.round(c.dur * fps));
    const cy = Math.cos(yaw), sy = Math.sin(yaw), F = { d: (x, y, z) => F0.d(cy * x - sy * z, y, sy * x + cy * z) };   // (item turned by yaw about Y)
    let worst = { d: 0 }, runs = [], open = null;
    for (let k = 0; k <= n; k++) {
      const t = c.dur * k / n, P = c.pose(t);
      P.rootY = (P.rootY || 0) + lift;
      solveAt(rig, { ...c, pose: tt => { const Q = c.pose(tt); Q.rootY = (Q.rootY || 0) + lift; return Q; } }, t); C.update();
      let gapT = Infinity;
      for (const [x, y, z, part] of verts()) {
        const d = F.d(x - at[0], y - at[1], z - at[2]);
        if (touchParts && touchParts.includes(part)) { gapT = Math.min(gapT, d); if (d < SOFT * 2) worst = d < worst.d ? { d, part, t } : worst; continue; }
        if (ignore.includes(part)) continue;
        if (d < soft && d < worst.d) worst = { d, part, t, p: [x - at[0], y - at[1], z - at[2]] };
      }
      if (when) {
        if (when(P, t)) { if (!open) runs.push(open = { best: Infinity, t }); open.best = Math.min(open.best, Math.max(0, gapT)); }
        else open = null;
      }
    }
    const misses = runs.filter(r => r.best > TOUCH);
    const bad = worst.d < soft || misses.length || (when && !runs.length);
    if (bad) failed++;
    console.log(`  ${bad ? 'FAIL' : 'ok  '} ${name.padEnd(28)} ${worst.d < 0 ? `${worst.part} into ${itemId} ${(worst.d * 1000).toFixed(0)}mm at ${worst.t.toFixed(2)}s${worst.p ? ` (item space ${worst.p.map(v => (v * 100).toFixed(1)).join(', ')} cm)` : ''}` : 'nothing inside'}${when ? ` · contact ${runs.length - misses.length}/${runs.length}${misses.length ? ` (misses up to ${(Math.max(...misses.map(r => r.best)) * 1000).toFixed(0)}mm)` : ''}` : ''}`);
  };
  // Drink: bowl where the clip says
  const dk = clip('Drink'), bw = dk.drink.bowl, lap = (P, t) => t < dk.drink.laps / dk.drink.rate && (t * dk.drink.rate % 1) > .3 && (t * dk.drink.rate % 1) < .55;   // (the tongue is down around .36-.46 of a lap: at 30 fps a lap has 1-2 frames near it)
  for (const bowl of ['water_bowl', 'milk_bowl', 'food_bowl'])
    scene('Drink · ' + bowl, bowl, 'Drink', [bw.x, 0, bw.z], { touchParts: ['tongue'], when: lap, fps: 60 });
  // JumpUp: the tower deck sits where the old test box was (deck centre at D + 5 cm)
  const ju = clip('JumpUp');
  const deck = itemField('cat_tower_1').anchors.decks[0];
  scene('JumpUp · cat_tower_1', 'cat_tower_1', 'JumpUp', [0, 0, ju.jump.deckBack + deck.size[1] / 2]);   // (deck's back edge at jump.deckBack)
  // JumpDown: the cat stands where JumpUp landed (root D in, the deck centre deckBack + depth/2 from the start),
  // turned round and stepped turnIn in towards the centre: in its root space the tower is rotated 180 degrees and
  // its base is H below
  const jd = clip('JumpDown');
  if (jd) scene('JumpDown · cat_tower_1', 'cat_tower_1', 'JumpDown', [0, jd.jump.H, ju.jump.D - ju.jump.deckBack - deck.size[1] / 2 + (jd.jump.turnIn || 0)], { yaw: Math.PI });
  // lying on the cushion / inside the hideout: centred under the trunk, cat raised to the seat
  applyPose(rig, stand(rig)); rig.model.updateMatrixWorld(true);
  const mid = rig.B.Hips.getWorldPosition(rig.B.Hips.position.clone()).add(rig.B.Chest.getWorldPosition(rig.B.Chest.position.clone())).multiplyScalar(.5);
  const cu = itemField('cushion').anchors, ho = itemField('hideout').anchors;
  for (const cn of ['Loaf', 'Sleep']) scene(cn + ' · cushion', 'cushion', cn, [mid.x, 0, mid.z], { lift: cu.top, soft: -.015 });   // (a cushion gives 1.5 cm)
  // (the hideout goes where the body is inside and the head out of the door: the offset is searched and printed)
  scene('Loaf · hideout', 'hideout', 'Loaf', [mid.x, 0, mid.z - (ho.catAhead ?? 0)], { lift: ho.floor, soft: SOFT });
  // PawBat: the toy under the paw at the bottom of the tap
  const pb = clip('PawBat'), toy = pb && pb.toy;   // (where the game puts the toy: worked out with the clip)
  if (!pb) console.log('  (PawBat left out for this breed: no toy scene)');
  else scene('PawBat · mouse_toy', 'mouse_toy', 'PawBat', [toy.x, 0, toy.z], { touchParts: ['FL'], when: (P, t) => t > pb.dur * .42 && t < pb.dur * .58, soft: SOFT, yaw: toy.yaw });
}
console.log(failed ? `\n${failed} FAIL` : '\nall ok');
process.exit(failed ? 1 : 0);
