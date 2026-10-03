// Write each item as a raw glTF (dense, vertex-coloured) for the Blender finishing pass (blender_items.py),
// plus its anchors (where cats eat, lie, land) as JSON.
//   node export_items.mjs OUT_DIR [id,id,...]
import fs from 'fs';
import path from 'path';
import { ITEM_IDS, buildItem, checkMesh } from './items.js';

const out = process.argv[2] || 'out/items_raw', ids = process.argv[3] ? process.argv[3].split(',') : ITEM_IDS;
fs.mkdirSync(out, { recursive: true });

function glb(m) {
  const n = m.pos.length / 3, c4 = new Float32Array(4 * n);
  for (let i = 0; i < n; i++) { c4[4 * i] = m.col[3 * i]; c4[4 * i + 1] = m.col[3 * i + 1]; c4[4 * i + 2] = m.col[3 * i + 2]; c4[4 * i + 3] = 1; }
  const bufs = [m.pos, m.nrm, c4, new Uint32Array(m.idx)];
  const views = [], parts = []; let off = 0;
  for (const b of bufs) { const u = new Uint8Array(b.buffer, b.byteOffset, b.byteLength); views.push({ buffer: 0, byteOffset: off, byteLength: u.length }); parts.push(u); off += u.length; while (off % 4) { parts.push(new Uint8Array(1)); off++; } }
  const mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
  for (let i = 0; i < n; i++) for (let k = 0; k < 3; k++) { mn[k] = Math.min(mn[k], m.pos[3 * i + k]); mx[k] = Math.max(mx[k], m.pos[3 * i + k]); }
  const json = {
    asset: { version: '2.0', generator: 'cat-island items.js' }, scene: 0, scenes: [{ nodes: [0] }], nodes: [{ name: m.id, mesh: 0 }],
    meshes: [{ name: m.id, primitives: [{ attributes: { POSITION: 0, NORMAL: 1, COLOR_0: 2 }, indices: 3, material: 0 }] }],
    materials: [{ name: m.id + '_raw', pbrMetallicRoughness: { metallicFactor: 0, roughnessFactor: .8 } }],
    buffers: [{ byteLength: off }], bufferViews: views,
    accessors: [
      { bufferView: 0, componentType: 5126, count: n, type: 'VEC3', min: mn, max: mx },
      { bufferView: 1, componentType: 5126, count: n, type: 'VEC3' },
      { bufferView: 2, componentType: 5126, count: n, type: 'VEC4' },
      { bufferView: 3, componentType: 5125, count: m.idx.length, type: 'SCALAR' },
    ],
  };
  let js = Buffer.from(JSON.stringify(json)); const pad = (4 - js.length % 4) % 4; js = Buffer.concat([js, Buffer.alloc(pad, 0x20)]);
  const bin = Buffer.concat(parts.map(p => Buffer.from(p.buffer, p.byteOffset, p.byteLength)));
  const head = Buffer.alloc(12); head.writeUInt32LE(0x46546C67, 0); head.writeUInt32LE(2, 4); head.writeUInt32LE(12 + 8 + js.length + 8 + bin.length, 8);
  const c0 = Buffer.alloc(8); c0.writeUInt32LE(js.length, 0); c0.writeUInt32LE(0x4E4F534A, 4);
  const c1 = Buffer.alloc(8); c1.writeUInt32LE(bin.length, 0); c1.writeUInt32LE(0x004E4942, 4);
  return Buffer.concat([head, c0, js, c1, bin]);
}

for (const id of ids) {
  const m = buildItem(id, { fine: .7 }), c = checkMesh(m);
  fs.writeFileSync(path.join(out, id + '_raw.glb'), glb(m));
  fs.writeFileSync(path.join(out, id + '.json'), JSON.stringify({ id, ko: m.ko, units: 'metres, Y up, +Z front', anchors: m.anchors, rawTris: m.tris, rawCheck: c }, null, 1));
  console.log(id, m.tris, 'tris', c.open ? 'OPEN ' + c.open : 'closed');
}
