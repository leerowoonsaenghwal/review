// Per-texel coat colours for the game texture (called by blender_finish.py).
//   node coat_texels.mjs <breed> <pos.f32> <size> <out.f32> [coat.json]
// pos.f32: size*size*3 float32, the bind-pose surface position behind each texel (Blender object space of the
// sculpted body; NaN where no surface). out.f32: size*size*3 float32 sRGB colours (0..1), NaN where the texel is
// left to the vertex-colour bake (no surface, or a part added outside the field: ears, tufts).
// The colours come from the same coatAt() the vertex colours use, so a stripe's edge is as sharp as a texel
// rather than blurred and stair-stepped across the ~1.6 cm sculpt triangles.
import fs from 'fs';
import * as THREE from 'three';
import { BREEDS } from './catgen.js';
import { buildCatModel } from './catmodel.js';

const [id, posFile, sizeArg, outFile, coatFile] = process.argv.slice(2);
const N = +sizeArg;
const b = BREEDS.find(x => x.id === id);
const coat = coatFile ? JSON.parse(fs.readFileSync(coatFile, 'utf8')) : b.coat;
const cat = buildCatModel(b.shape, coat, { res: .03, ...(process.env.COAT_STYLE ? { style: process.env.COAT_STYLE } : {}) });
const { coatAt, field, modelScale } = cat.userData;   // (the texels are in metres, the coat function in model units)
const raw = fs.readFileSync(posFile);
const pos = new Float32Array(raw.buffer, raw.byteOffset, raw.byteLength / 4);

// Blender's glTF import turns the model's Y-up into Z-up: find the mapping that puts the texels on the surface
const k = 1 / modelScale;
const maps = { identity: (x, y, z) => [x * k, y * k, z * k], fromZup: (x, y, z) => [x * k, z * k, -y * k], fromZupNeg: (x, y, z) => [x * k, -z * k, y * k] };
// (median, not mean: parts added outside the field - ears, tufts - sit far from it and are left to the bake)
let best = null;
for (const [name, f] of Object.entries(maps)) {
  const ds = [];
  for (let i = 0; i < N * N && ds.length < 4000; i += 97) {
    const x = pos[i * 3]; if (!Number.isFinite(x)) continue;
    const [a, bb, c] = f(x, pos[i * 3 + 1], pos[i * 3 + 2]);
    ds.push(Math.abs(field.eval(a, bb, c)));
  }
  ds.sort((u, v) => u - v);
  const err = ds[ds.length >> 1] ?? 1;
  if (!best || err < best.err) best = { name, f, err, n: ds.length };
}
console.log(`mapping ${best.name}: median |distance to surface| ${(best.err * modelScale * 1000).toFixed(2)} mm over ${best.n} texels`);
if (best.err * modelScale > .004) { console.error('texels do not lie on the sculpt surface - wrong breed or space?'); process.exit(2); }

const out = new Float32Array(N * N * 3).fill(NaN);
const q = new THREE.Vector3(), c = new THREE.Color();
const toSRGB = v => v <= .0031308 ? 12.92 * v : 1.055 * Math.pow(v, 1 / 2.4) - .055;
let done = 0, skipped = 0; const t0 = Date.now();
for (let i = 0; i < N * N; i++) {
  const x = pos[i * 3]; if (!Number.isFinite(x)) continue;
  const [a, bb, cc] = best.f(x, pos[i * 3 + 1], pos[i * 3 + 2]);
  // (off the field - ears, ear and paw tufts are coloured their own way when sculpted): keep the baked colour
  if (Math.abs(field.eval(a, bb, cc)) * modelScale > .004) { skipped++; continue; }
  coatAt(q.set(a, bb, cc), c);
  out[i * 3] = toSRGB(c.r); out[i * 3 + 1] = toSRGB(c.g); out[i * 3 + 2] = toSRGB(c.b);
  done++;
}
fs.writeFileSync(outFile, Buffer.from(out.buffer));
console.log(`${done} texels from the coat function, ${skipped} left to the bake (ears, tufts), ${((Date.now() - t0) / 1000).toFixed(1)} s`);
