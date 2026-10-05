// Motion QA on the skinned meshes: interpenetration, paws below the floor, and contacts that must touch
// (tongue on fur, paw on face, foot behind the ear).
//   node qa.mjs [breed,breed] [Clip,Clip] [--fps 15] [--res .03]
// Prints one line per clip and the worst problems; exits 1 if anything fails.
import { BREEDS } from './catgen.js';
import { buildCatModel } from './catmodel.js';
import { makeRig, makeClips, solveAt, applyPose, stand, jumpFloor } from './catmotion.js';
import { makeContact, LIMBS } from './catcontact.js';

const args = process.argv.slice(2), opt = (k, d) => args.includes(k) ? +args[args.indexOf(k) + 1] : d;
const pos = args.filter((a, i) => !a.startsWith('--') && !(i > 0 && args[i - 1].startsWith('--')));
const breeds = (pos[0] || 'korean_shorthair,scottish_fold,persian').split(',');
const only = pos[1] ? pos[1].split(',') : null;
const FPS = opt('--fps', 15), RES = opt('--res', .03);
export const LIMIT = { pen: .004, floor: -.004, touch: .003, deep: -.007 };   // metres

let failed = 0;
for (const id of breeds) {
  const b = BREEDS.find(x => x.id === id);
  const rig = makeRig(buildCatModel(b.shape, b.coat, { res: RES })), C = makeContact(rig);
  const longFur = b.shape.fur === 'long' || b.shape.fur === 'curly';        // (a curly coat is as deep: Selkirk Rex, LaPerm)
  const allClips = makeClips(rig, { only }), clips = allClips.filter(c => !only || only.includes(c.name)); clips.skipped = allClips.skipped;
  // point sets (every other vertex is plenty at this resolution)
  const limbPts = Object.fromEntries(LIMBS.map(L => [L, C.points(p => p === L, 'body', 2)]));
  const beanPts = Object.fromEntries(LIMBS.map(L => [L, C.points(p => p === L, 'face')]));
  const tailPts = C.points((p, i, m) => p === 'tail' && m.wmax[i] > .7, 'body', 2);
  const headPts = C.points((p, i, m) => p === 'head' && m.wmax[i] > .95, 'body', 3);
  const allPts = C.points(() => true, 'body', 3);
  const tongue = C.points(p => p === 'tongue', 'face');
  const setOf = a => a === 'tongue' ? tongue : LIMBS.includes(a) ? { mesh: 'body', ids: [...limbPts[a].ids] } : C.points(p => p === a, 'body', 2);
  // rest-pose depths: anything already inside in the bind pose (e.g. legs inside a long-haired breed's fur
  // skirt) is the model, not the motion - only deeper than that counts
  applyPose(rig, stand(rig)); C.update();
  const notSelf = L => p => p !== L && p !== L + 'u';
  const restL = Object.fromEntries(LIMBS.map(L => [L, C.depthEach(limbPts[L], notSelf(L))]));
  const restB = Object.fromEntries(LIMBS.map(L => [L, C.depthEach(beanPts[L], notSelf(L))]));
  console.log(`\n${b.ko} (${id})`); if (clips.skipped?.length) console.log('  (skipped for this body: ' + clips.skipped.map(x => x.clip + ' — ' + x.reason).join('; ') + ')');
  for (const clip of clips) {
    const issues = [], n = Math.max(2, Math.round(clip.dur * FPS));
    const runs = (clip.contacts || []).map(() => []);
    for (let f = 0; f <= n; f++) {
      const t = Math.min(clip.dur, f / FPS), P = solveAt(rig, clip, t); C.update();
      const at = t.toFixed(2) + 's';
      for (const L of LIMBS) {
        const dl = C.depth(limbPts[L], notSelf(L), restL[L]), db = C.depth(beanPts[L], notSelf(L), restB[L]);
        // a long coat is soft hair the legs move in: legs inside the body's fur volume are only a warning there
        // (and a fluffy leg brushing the next one, up to 12 mm, is hair in hair)
        const soft = (part, d = 0) => longFur && (part === 'torso' || (LIMBS.includes(part) || part.endsWith('u')) && d > -.012);
        if (dl.d < -LIMIT.pen) issues.push({ k: `${L} in ${dl.part}${soft(dl.part, dl.d) ? ' fur (warn)' : ''}`, d: dl.d, at, warn: soft(dl.part, dl.d) });
        if (db.d < -LIMIT.pen) issues.push({ k: `${L} beans in ${db.part}${soft(db.part, db.d) ? ' fur (warn)' : ''}`, d: db.d, at, warn: soft(db.part, db.d) });
      }
      const dt = C.depth(tailPts, p => p !== 'tail' && p !== 'torso' || false);
      if (dt.d < -LIMIT.pen) issues.push({ k: `tail in ${dt.part}`, d: dt.d, at });
      // the big head sinking onto its own chest is hidden inside the head (like the neck joint), so that is
      // only a warning, as is the head resting on a shoulder; the head pressing into a paw or leg fails
      const dh = C.depth(headPts, p => p !== 'head' && p !== 'torso' && !p.endsWith('u'));
      if (dh.d < -LIMIT.pen * 2) issues.push({ k: `head in ${dh.part}`, d: dh.d, at });
      const dc = C.depth(headPts, p => p === 'torso' || p.endsWith('u'));
      if (dc.d < -.012) issues.push({ k: `head on ${dc.part} (warn)`, d: dc.d, at, warn: true });
      // (jumps: the floor moves with the clip - the deck top up a jump, the floor below once off a deck)
      const fl = clip.jump ? jumpFloor(clip.jump, P) : 0;
      const who = {}, y = C.lowest(allPts, who) - fl;
      if (y < LIMIT.floor) issues.push({ k: `${who.part} below floor`, d: y, at });
      for (const L of LIMBS) { const yb = C.lowest(beanPts[L]) - fl; if (yb < LIMIT.floor) issues.push({ k: `${L} beans below floor`, d: yb, at }); }
      (clip.contacts || []).forEach((c, ci) => {
        const on = c.when(P, t);
        const r = runs[ci];
        if (!on) { if (r.length && r[r.length - 1].open) r[r.length - 1].open = false; return; }
        if (!r.length || !r[r.length - 1].open) r.push({ open: true, best: Infinity, deep: 0, t0: at });
        let g = C.gap(setOf(c.a), p => c.b.includes(p));
        if (LIMBS.includes(c.a)) { const gb = C.gap(beanPts[c.a], p => c.b.includes(p)); if (gb.d < g.d) g = gb; }   // (a paw touches with its beans too)
        const run = r[r.length - 1];
        run.best = Math.min(run.best, Math.max(0, g.d)); run.deep = Math.min(run.deep, g.d);   // (touching: within 3 mm outside, or pressed in no deeper than LIMIT.deep)
      });
    }
    // per contact: every run (each lick / each stroke) must touch, and must not sink in deeply
    (clip.contacts || []).forEach((c, ci) => {
      for (const r of runs[ci]) {
        if (r.best > LIMIT.touch) issues.push({ k: `${c.a}→${c.b.join('/')} misses`, d: r.best, at: r.t0 });
        if (r.deep < LIMIT.deep) issues.push({ k: `${c.a}→${c.b.join('/')} sinks in`, d: r.deep, at: r.t0 });
      }
      if (!runs[ci].length) issues.push({ k: `${c.a}→${c.b.join('/')} never happens`, d: 0, at: '-' });
    });
    // worst instance of each kind
    const worst = {};
    for (const i of issues) if (!worst[i.k] || Math.abs(i.d) > Math.abs(worst[i.k].d)) worst[i.k] = { ...i, n: (worst[i.k]?.n || 0) + 1 }; else worst[i.k].n++;
    const list = Object.values(worst);
    const cs = (clip.contacts || []).map((c, ci) => `${c.a}→${c.b.join('/')} ${runs[ci].length}x best ${(Math.max(...runs[ci].map(r => r.best)) * 1000).toFixed(0)}mm`).join('; ');
    const bad = list.filter(w => !w.warn).length;
    if (bad) failed++;
    console.log(`  ${bad ? 'FAIL' : list.length ? 'warn' : 'ok  '} ${clip.name.padEnd(12)} ${cs}`);
    for (const w of list) console.log(`         ${w.k.padEnd(26)} ${(w.d * 1000).toFixed(0).padStart(5)}mm  at ${w.at}  (${w.n} frames)`);
  }
}
process.exit(failed ? 1 : 0);
