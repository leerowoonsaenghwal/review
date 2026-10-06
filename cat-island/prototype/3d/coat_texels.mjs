// Per-texel coat colours for the game texture (called by blender_finish.py).
//   node coat_texels.mjs <breed> <pos.f32> <size> <out.f32> [coat.json]
//   COAT_MASK=<mask.f32> also writes the coat mask (size*size*4 float32, NaN off the field) and COAT_SLOTS=<slots.json>
//   the slot colours: R = base, G = dark (pattern) or point, B = white, A = second (calico/tortie). Whatever is left
//   (1 - R - G - B - A: blush, rosette fill, wrinkles) keeps the game texture's colour. A photo cat (photo2cat coat)
//   recolours the breed's texture with these weights in the game (CatCoat), so the pattern stays the breed's.
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

if (process.env.COAT_MASK) {
  // two flat-fur evaluations with pure slot colours: (base R, dark/point G, white B) and (second R)
  const P = coat.pattern || 'solid', pointish = P === 'point' || P === 'mitted';
  const strip = { ...coat, noLighten: true, silverTip: 0, glitter: 0, rosetteFill: '#000000', nose: '#000000', pad: '#000000', innerEar: '#000000' };
  const m1 = buildCatModel(b.shape, { ...strip, base: '#ff0000', dark: pointish ? '#000000' : '#00ff00', point: pointish ? '#00ff00' : '#000000', white: '#0000ff', second: '#000000' }, { res: .03, coatMask: true, ...(process.env.COAT_STYLE ? { style: process.env.COAT_STYLE } : {}) }).userData.coatAt;
  const m2 = buildCatModel(b.shape, { ...strip, base: '#000000', dark: '#000000', point: '#000000', white: '#000000', second: '#ff0000' }, { res: .03, coatMask: true, ...(process.env.COAT_STYLE ? { style: process.env.COAT_STYLE } : {}) }).userData.coatAt;
  const mask = new Float32Array(N * N * 4).fill(NaN), c2 = new THREE.Color();
  for (let i = 0; i < N * N; i++) {
    if (!Number.isFinite(out[i * 3])) continue;
    const [a, bb, cc] = best.f(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
    m1(q.set(a, bb, cc), c); m2(q, c2);
    mask[i * 4] = c.r; mask[i * 4 + 1] = c.g; mask[i * 4 + 2] = c.b; mask[i * 4 + 3] = c2.r;
  }
  fs.writeFileSync(process.env.COAT_MASK, Buffer.from(mask.buffer));
  const hex = h => h && ('#' + new THREE.Color(h).getHexString());
  const base = new THREE.Color(coat.base || '#999999');
  // (tabby coats are lightened 10 % towards cream in style 'ac' (catgen makeCoat): the game does the same to the new colours)
  fs.writeFileSync(process.env.COAT_SLOTS, JSON.stringify({ pattern: P, lighten: ['mackerel', 'classic', 'spotted', 'ticked'].includes(P) ? .1 : 0,
    base: hex(coat.base || '#999999'), dark: pointish ? hex(coat.point || '#5b4337') : hex(coat.dark) || '#' + base.clone().multiplyScalar(.6).getHexString(),
    white: hex(coat.white || '#fbf7ef'), second: hex(coat.second || '#e8964a') }));
  console.log('coat mask written');
}
console.log(`${done} texels from the coat function, ${skipped} left to the bake (ears, tufts), ${((Date.now() - t0) / 1000).toFixed(1)} s`);
