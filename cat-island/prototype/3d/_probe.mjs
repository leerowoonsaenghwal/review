import { BREEDS } from './catgen.js';
import { buildCatModel } from './catmodel.js';
import { makeRig, makeClips } from './catmotion.js';
const b = BREEDS.find(x => x.id === process.argv[2]), rig = makeRig(buildCatModel(b.shape, b.coat, { res: .03 }));
const r = makeClips(rig).groomReport;
console.log(b.id, Object.entries(r).filter(([k, v]) => typeof v === 'number').map(([k, v]) => k + ' ' + (v * 1000).toFixed(0)).join(' | '), '| pawLickViol', JSON.stringify(r.pawLickInfo?.viol?.map(v => v.who + '>' + v.into + ' ' + (v.d * 1000).toFixed(0))));
