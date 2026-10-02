// node --test photo2cat.test.js  — synthetic "cats" (an ellipse on a grey background) for each rule
import test from 'node:test';
import assert from 'node:assert/strict';
import { analyzeCoat, suggestBreeds } from './photo2cat.js';

const W = 240, H = 200;
function synth(paint) {
  const data = new Uint8ClampedArray(W * H * 4), mask = new Float32Array(W * H);
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const i = y * W + x, inside = ((x - 120) / 95) ** 2 + ((y - 105) / 75) ** 2 < 1;
    const c = inside ? paint(x, y) : [150, 150, 155];
    data.set([...c, 255], i * 4); mask[i] = inside ? 1 : 0;
  }
  return { img: { data, width: W, height: H }, mask };
}
const run = (paint, opts) => { const { img, mask } = synth(paint); return analyzeCoat(img, mask, opts); };
const noise = (x, y) => ((Math.sin(x * 12.9898 + y * 78.233) * 43758.5453) % 1 + 1) % 1;
const grain = (c, x, y) => c.map(v => v + (noise(x, y) - .5) * 10);

test('solid grey -> solid', () => {
  const r = run((x, y) => grain([120, 122, 128], x, y));
  assert.equal(r.coat.pattern, 'solid');
});
test('grey with dark bands -> mackerel', () => {
  const r = run((x, y) => grain(Math.sin(x / 5) > .2 ? [70, 70, 72] : [150, 150, 150], x, y));
  assert.equal(r.coat.pattern, 'mackerel');
  assert.ok(r.coat.dark);
});
test('orange + black + white patches -> calico', () => {   // patches about a quarter of the body wide
  const r = run((x, y) => { const n = Math.sin(x / 30) + Math.cos(y / 24); return n > .6 ? [235, 140, 50] : n < -.6 ? [30, 28, 28] : [245, 245, 242]; });
  assert.equal(r.coat.pattern, 'calico');
});
test('mostly white with coloured top -> van', () => {
  const r = run((x, y) => y < 55 ? [215, 125, 55] : [245, 244, 240]);
  assert.equal(r.coat.pattern, 'van');
});
test('cream body, dark edges -> point', () => {
  const r = run((x, y) => (((x - 120) / 95) ** 2 + ((y - 105) / 75) ** 2 > .55) ? [70, 50, 40] : [238, 228, 212]);
  assert.equal(r.coat.pattern, 'point');
});
test('near-black coat never becomes a tabby', () => {
  const r = run((x, y) => grain(Math.sin(x / 5) > .5 ? [40, 38, 38] : [18, 16, 16], x, y));
  assert.equal(r.coat.pattern, 'solid');
  assert.equal(r.coat.lightMuzzle, false);
});
test('no cat in mask -> not ok', () => {
  const data = new Uint8ClampedArray(W * H * 4).fill(128);
  assert.equal(analyzeCoat({ data, width: W, height: H }, new Float32Array(W * H)).ok, false);
});
test('same photo -> same cat (deterministic)', () => {
  const p = (x, y) => grain([180, 120, 70], x, y);
  assert.deepEqual(run(p).coat, run(p).coat);
});
test('breed toggles win over coat', () => {
  assert.equal(suggestBreeds({ pattern: 'mackerel', base: '#918f89' }, { ear: 'fold' })[0].id, 'scottish_fold');
  assert.equal(suggestBreeds({ pattern: 'point', base: '#ebe3dc' })[0].id, 'siamese');
  assert.equal(suggestBreeds({ pattern: 'solid', base: '#a19f9f' })[0].id, 'russian_blue');
  assert.equal(suggestBreeds({ pattern: 'solid', base: '#e4c183' }, { fur: 'long' })[0].id, 'persian');
});
