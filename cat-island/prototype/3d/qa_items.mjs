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
import fs from 'fs';
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
  // (--only: just the clips that scene needs - a long-haired breed's full set takes hours)
  const onlyArg = args.includes('--only') ? args[args.indexOf('--only') + 1] : null;
  const NEED = { Drink: ['Drink'], Jump: ['JumpUp', 'JumpDown'], cushion: ['Loaf', 'Sleep'], hideout: ['Loaf'], PawBat: ['PawBat'] };
  const want = onlyArg ? [...new Set(Object.entries(NEED).filter(([k]) => onlyArg.includes(k) || k.includes(onlyArg)).flatMap(([, v]) => v))] : ['Drink', 'JumpUp', 'JumpDown', 'Loaf', 'Sleep', 'FlopIdle', 'PawBat'];
  const clips = makeClips(rig, { only: want }), C = contactOf(rig);
  const clip = n => clips.find(c => c.name === n);
  console.log(`\n${b.ko} (${id})`);
  // world positions of every cat vertex (body + face, which holds the tongue and the beans)
  const verts = function* () {
    for (const mesh of ['body', 'face']) { const M = C.M[mesh], p = M.pos; for (let i = 0; i < M.n; i++) yield [p[3 * i], p[3 * i + 1], p[3 * i + 2], M.part[i]]; }
  };
  // one scene: item at `at` (x,y,z), cat clip played with the root lifted by `lift`; `touch(part)` = parts that
  // must touch the item (checked with `when`), `soft`: how far the cat may press into it
  const only = args.includes('--only') ? args[args.indexOf('--only') + 1] : null;
  const scene = (name, itemId, clipName, at, { lift = 0, touchParts = null, when = null, soft = PEN, ignore = [], yaw = 0, fps = FPS, quiet = false, move = null } = {}) => {
    if (only && !name.includes(only) && !quiet) return;
    const F0 = itemField(itemId), c = clip(clipName), n = Math.max(2, Math.round(c.dur * fps));
    const cy = Math.cos(yaw), sy = Math.sin(yaw), F = { d: (x, y, z) => F0.d(cy * x - sy * z, y, sy * x + cy * z) };   // (item turned by yaw about Y)
    let worst = { d: 0 }, runs = [], open = null;
    for (let k = 0; k <= n; k++) {
      const t = c.dur * k / n, P = c.pose(t);
      P.rootY = (P.rootY || 0) + lift;
      solveAt(rig, { ...c, pose: tt => { const Q = c.pose(tt); Q.rootY = (Q.rootY || 0) + lift; return Q; } }, t); C.update();
      let gapT = Infinity;
      for (const [x, y, z, part] of verts()) {
        const mv = move ? move(t) : null, d = F.d(x - at[0] - (mv ? mv[0] : 0), y - at[1], z - at[2] - (mv ? mv[2] : 0));
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
    if (quiet) return worst.d;
    if (bad) failed++;
    console.log(`  ${bad ? 'FAIL' : 'ok  '} ${name.padEnd(28)} ${worst.d < 0 ? `${worst.part} into ${itemId} ${(worst.d * 1000).toFixed(0)}mm at ${worst.t.toFixed(2)}s${worst.p ? ` (item space ${worst.p.map(v => (v * 100).toFixed(1)).join(', ')} cm)` : ''}` : 'nothing inside'}${when ? ` · contact ${runs.length - misses.length}/${runs.length}${misses.length ? ` (misses up to ${(Math.max(...misses.map(r => r.best)) * 1000).toFixed(0)}mm)` : ''}` : ''}`);
  };
  // the game's own numbers (assets/cats/<id>.clips.json): --fix-spots searches a placement where nothing is inside and writes it back
  const assetFile = new URL(`../../assets/cats/${id}.clips.json`, import.meta.url), asset = fs.existsSync(assetFile) ? JSON.parse(fs.readFileSync(assetFile, 'utf8')) : null;
  const fix = args.includes('--fix-spots') && asset, saveAsset = msg => { fs.writeFileSync(assetFile, JSON.stringify(asset, null, 1)); console.log(`  (${msg}: written to ${id}.clips.json)`); };
  // Drink: bowl where the clip says
  const dk = clip('Drink'), bw = dk?.drink?.bowl, lap = (P, t) => t < dk.drink.laps / dk.drink.rate && (t * dk.drink.rate % 1) > .3 && (t * dk.drink.rate % 1) < .55;   // (the tongue is down around .36-.46 of a lap: at 30 fps a lap has 1-2 frames near it)
  if (dk) for (const bowl of ['water_bowl', 'milk_bowl', 'food_bowl'])
    scene('Drink · ' + bowl, bowl, 'Drink', [bw.x, 0, bw.z], { touchParts: ['tongue'], when: lap, fps: 60 });
  // JumpUp: the tower deck sits where the old test box was (deck centre at D + 5 cm)
  const ju = clip('JumpUp');
  const deck = itemField('cat_tower_1').anchors.decks[0];
  if (ju) {
  const CARPET = -.005;   // (the decks and their rims are carpeted: a paw may press in this much, like the cushion's 15 mm)
  scene('JumpUp · cat_tower_1', 'cat_tower_1', 'JumpUp', [0, 0, ju.jump.deckBack + deck.size[1] / 2], { soft: CARPET });   // (deck's back edge at jump.deckBack)
  // JumpDown: the cat stands where JumpUp landed (root D in, the deck centre deckBack + depth/2 from the start),
  // turned round and stepped turnIn in towards the centre: in its root space the tower is rotated 180 degrees and
  // its base is H below
  const jd = clip('JumpDown');
  const ajd = asset?.clips?.find(c => c.name === 'JumpDown')?.jump;
  let turnIn = ajd?.turnIn ?? jd?.jump.turnIn ?? 0;
  const jdAt = ti => [0, jd.jump.H, ju.jump.D - ju.jump.deckBack - deck.size[1] / 2 + ti];
  if (jd && fix && ajd) {   // (short legs: the trailing hind paw can brush the deck rim - the cat starts its jump a little further in)
    const t0 = jd.jump.turnIn || 0;
    for (const dt of [0, .01, .02, .03, .04, -.01]) if (scene('', 'cat_tower_1', 'JumpDown', jdAt(t0 + dt), { yaw: Math.PI, quiet: true, fps: 30, soft: CARPET }) >= CARPET + .001) { const v = +(t0 + dt).toFixed(3); if (v !== ajd.turnIn) { ajd.turnIn = turnIn = v; saveAsset(`JumpDown turnIn ${v}`); } break; }
  }
  if (jd) scene('JumpDown · cat_tower_1', 'cat_tower_1', 'JumpDown', jdAt(turnIn), { yaw: Math.PI, soft: CARPET });
  }
  // lying on the cushion / inside the hideout: centred under the trunk, cat raised to the seat
  applyPose(rig, stand(rig)); rig.model.updateMatrixWorld(true);
  const mid = rig.B.Hips.getWorldPosition(rig.B.Hips.position.clone()).add(rig.B.Chest.getWorldPosition(rig.B.Chest.position.clone())).multiplyScalar(.5);
  const cu = itemField('cushion').anchors, ho = itemField('hideout').anchors;
  for (const cn of ['Loaf', 'Sleep']) if (clip(cn)) scene(cn + ' · cushion', 'cushion', cn, [mid.x, 0, mid.z], { lift: cu.top, soft: -.015 });   // (a cushion gives 1.5 cm)
  // (the hideout goes where the body is inside and the head out of the door: the offset is searched and printed)
  // the spot the game uses (assets/cats/<id>.clips.json itemSpots.hideout), else the default under the trunk.
  // --fix-spots: a breed whose head or ears reach the dome (big ears: Sphynx) is moved further out of the door until
  // nothing is inside, and the spot is written back to its clips.json (the game reads it from there)
  let hz = asset?.itemSpots?.hideout?.z ?? +(mid.z - (ho.catAhead ?? 0)).toFixed(3);
  if (fix && asset?.itemSpots?.hideout && clip('Loaf')) {
    const base = +(mid.z - (ho.catAhead ?? 0)).toFixed(3);
    for (const dz of [0, -.02, -.04, -.06, -.08, -.1, .02]) {   // (minus: the cat further out of the door, +z in item space is the door)
      const w = scene('', 'hideout', 'Loaf', [mid.x, 0, base + dz], { lift: ho.floor, soft: SOFT, quiet: true, fps: 4 });
      if (w >= -.006) { hz = +(base + dz).toFixed(3); break; }
    }
    if (hz !== asset.itemSpots.hideout.z) { asset.itemSpots.hideout.z = hz; saveAsset(`hideout spot z ${hz}`); }
  }
  if (clip('Loaf')) scene('Loaf · hideout', 'hideout', 'Loaf', [mid.x, 0, hz], { lift: ho.floor, soft: SOFT });
  // PawBat: the toy under the paw at the bottom of the tap
  const pb = clip('PawBat'), toy = pb && pb.toy;   // (where the game puts the toy: worked out with the clip)
  if (!pb) { if (want.includes('PawBat')) console.log('  (PawBat left out for this breed: no toy scene)'); }
  else {
    // the game's toy spot (clips.json itemSpots.mouse_toy); --fix-spots: a big paw (long fur) that sinks into the toy
    // gets the toy moved out along the cat's facing, the first spot where nothing sinks in and the tap still touches
    const at = asset?.itemSpots?.mouse_toy, tw = (P, t) => t > pb.dur * .42 && t < pb.dur * .58;
    // the game rolls the toy away from the cat right after the tap (CatBrain bat -> ItemJiggle.Kick 18 cm, out in .77 s):
    // the paw following through after the tap meets the floor where the toy was, not the toy
    const tk = pb.dur * .58, roll = t => { if (t <= tk) return null; const u = (t - tk) / 2.2, k = u < .35 ? 1 - (1 - u / .35) ** 3 : 1; return [0, 0, .18 * k]; };
    let tx = at?.x ?? toy.x, tz = at?.z ?? toy.z;
    if (fix && at) {
      for (const dz of [0, .01, .02, .03, .04, .05, .06]) {
        const w = scene('', 'mouse_toy', 'PawBat', [toy.x, 0, toy.z + dz], { soft: SOFT, yaw: toy.yaw, quiet: true, ignore: [], move: roll });
        if (w >= SOFT + .002) { const nz = +(toy.z + dz).toFixed(3); if (nz !== at.z) { at.z = tz = nz; tx = at.x; saveAsset(`mouse_toy spot z ${nz}`); } break; }
      }
    }
    scene('PawBat · mouse_toy', 'mouse_toy', 'PawBat', [tx, 0, tz], { touchParts: ['FL'], when: tw, soft: SOFT, yaw: toy.yaw, move: roll });
  }
}
console.log(failed ? `\n${failed} FAIL` : '\nall ok');
process.exit(failed ? 1 : 0);
