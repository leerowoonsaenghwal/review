// probe: settled Flop pose values over time (hip height, roll, legs), and the world height of hips/chest
import { BREEDS } from './catgen.js';
import { buildCatModel } from './catmodel.js';
import { makeRig, makeClips, solveAt } from './catmotion.js';
import * as THREE from 'three';
const b = BREEDS.find(x => x.id === (process.argv[2] || 'korean_shorthair')), clipName = process.argv[3] || 'Flop';
const rig = makeRig(buildCatModel(b.shape, b.coat, { res: .03 }));
const clip = makeClips(rig, { only: [clipName] }).find(c => c.name === clipName);
const w = n => rig.B[n].getWorldPosition(new THREE.Vector3());
for (let t = 0; t <= clip.dur + 1e-6; t += clip.dur / 14) {
  const P = clip.pose(t); solveAt(rig, clip, t);
  const f = k => (+P[k]).toFixed(3);
  console.log(`t ${t.toFixed(2)} hipY ${f('hipY')} hipX ${f('hipX')} roll ${f('hipRoll')} sp ${f('spRoll')} | hips@${w('Hips').y.toFixed(3)} chest@${w('Chest').y.toFixed(3)} head@${w('Head').y.toFixed(3)} | FLy ${f('FLy')} FLfk ${f('FLfk')} HLy ${f('HLy')} HLfk ${f('HLfk')}`);
}
