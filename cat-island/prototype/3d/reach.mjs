// 그루밍 닿기 거리만 빠르게: 동작을 만들 때 계산되는 "닿아야 할 곳까지 남은 거리"(report)를 품종별로 출력한다.
// 프레임별 전체 점검(qa.mjs, verify)은 하지 않으므로 수십 초면 끝난다. 동작 코드를 고칠 때 먼저 이것으로 본다.
//   node reach.mjs [breed,breed] [--clips=ScratchEar] [--verify] [--res .03]
//   (--verify also plays each clip through frame by frame with the shipping rules and prints what was left out)
import { BREEDS } from './catgen.js';
import { buildCatModel } from './catmodel.js';
import { makeRig, makeClips } from './catmotion.js';

const args = process.argv.slice(2), opt = (k, d) => args.includes(k) ? +args[args.indexOf(k) + 1] : d;
const breeds = (args.find(a => !a.startsWith('--') && !/^[.\d]+$/.test(a)) || 'korean_shorthair').split(',');
const RES = opt('--res', .03);
for (const id of breeds) {
  const b = BREEDS.find(x => x.id === id), t0 = Date.now();
  const rig = makeRig(buildCatModel(b.shape, b.coat, { res: RES, ...(args.includes('--classic') ? { style: 'classic' } : {}) }));   // (--classic: the earlier look, to compare)
  const t1 = Date.now();
  const want = (args.find(a => a.startsWith('--clips=')) || '--clips=GroomFace,ScratchEar,NibbleClaws').slice(8).split(',');
  const verify = args.includes('--verify');
  const clips = makeClips(rig, { only: want, groomOnly: want, noVerify: !verify });
  const r = clips.groomReport || {};
  const mm = v => v == null ? '-' : (v * 1000).toFixed(1) + 'mm';
  if (verify) console.log(`  skipped: ${JSON.stringify(clips.skipped || [])}`);
  if (r.scratchPose) console.log(`  scratch pose: ${JSON.stringify(r.scratchPose)}`);
  console.log(`${id.padEnd(20)} scratch=${mm(r.scratch)} pawLick=${mm(r.pawLick)} wash=${mm(r.wash)} nibble=${mm(r.nibble)}  (model ${((t1 - t0) / 1000).toFixed(0)}s, clips ${((Date.now() - t1) / 1000).toFixed(0)}s)`);
}
