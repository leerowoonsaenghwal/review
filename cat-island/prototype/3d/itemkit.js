// Cat Island items with toy detail (docs/ART_DIRECTION.md 5장): every item is a group of parts, each part ONE
// closed mesh (shadows), with painted textures drawn on a canvas (fabric check, felt, wood grain, glaze, sisal
// rope, carpet with paw prints, bark, cardboard flutes). The mockup page (items_detail.html) and the game export
// (export_itemkit.html -> blender_items.py --parts) build from this same code.
//
//   buildKitItem(id, { variant, press }) -> THREE.Group, userData: { id, ko, anchors, variants, bake }
//
// Each part carries userData.role:
//   'both'   (default) a real part of the game mesh, its own look baked from itself
//   'detail' too small or too many for the game mesh (kibble, stitches, screws, sand grains): only its colour
//            and relief are baked onto the part underneath
//   'proxy'  a simple game-mesh surface that receives a 'detail' bake (the kibble heap's dome)
// Sizes and the heights a cat uses are the game's (items.js: BOWL_SURF, TOY_TOP, DECK_STEP, cushion top, hideout
// floor): the clips were fitted to them. Variant only changes colours (the mesh is the same for every variant).
// Needs a browser (canvas).
import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { mergeVertices } from 'three/addons/utils/BufferGeometryUtils.js';
import { surfaceNets, relax, sdEllipsoid } from './sdfmesh.js';

const V = (x, y, z) => new THREE.Vector3(x, y, z);
export const rng = s => { let seed = s; return () => (seed = (seed * 16807) % 2147483647) / 2147483647; };
const hashStr = s => { let h = 7; for (const c of s) h = (h * 31 + c.charCodeAt(0)) % 2147483646; return h + 1; };

// ---------------------------------------------------------------- painted textures (canvas, cached)
const texCache = new Map();
export function tex(key, w, h, draw, rx = 1, ry = 1) {
  const k = key + '|' + rx + '|' + ry;
  if (texCache.has(k)) return texCache.get(k);
  if (typeof document === 'undefined') return null;   // (node: shapes only, for kitField)
  const cv = document.createElement('canvas'); cv.width = w; cv.height = h;
  draw(cv.getContext('2d'), w, h, rng(hashStr(key)));
  const t = new THREE.CanvasTexture(cv); t.colorSpace = THREE.SRGBColorSpace; t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(rx, ry); t.anisotropy = 8; t.name = key;
  texCache.set(k, t); return t;
}
const weave = (g, w, h, a = .07) => {   // fine fabric weave over whatever is painted
  for (let y = 0; y < h; y += 4) { g.fillStyle = `rgba(255,255,255,${a})`; g.fillRect(0, y, w, 1); }
  for (let x = 0; x < w; x += 4) { g.fillStyle = `rgba(0,0,0,${a * .7})`; g.fillRect(x, 0, 1, h); }
};
export const gingham = (c1, c2, c3) => tex('gingham' + c1 + c2 + c3, 512, 512, (g, w, h) => {
  g.fillStyle = c1; g.fillRect(0, 0, w, h);
  const s = 64; g.globalAlpha = .55; g.fillStyle = c2;
  for (let i = 0; i < w; i += s * 2) { g.fillRect(i, 0, s, h); g.fillRect(0, i, w, s); }
  g.globalAlpha = .45; g.fillStyle = c3; for (let i = 0; i < w; i += s * 2) for (let j = 0; j < h; j += s * 2) g.fillRect(i, j, s, s);
  g.globalAlpha = 1; weave(g, w, h);
}, 1.6, 1.6);
export const felt = (c, c2, rx = 4, ry = 4) => tex('felt' + c + c2, 256, 256, (g, w, h, R) => { g.fillStyle = c; g.fillRect(0, 0, w, h); for (let i = 0; i < 2500; i++) { g.fillStyle = R() < .5 ? c2 : 'rgba(255,255,255,.18)'; g.fillRect(R() * w, R() * h, 2, 1); } weave(g, w, h, .05); }, rx, ry);
export const woodTex = (base, dark, light, rx = 1, ry = 1) => tex('wood' + base + dark + light, 512, 512, (g, w, h, R) => {
  g.fillStyle = base; g.fillRect(0, 0, w, h);
  for (let i = 0; i < 60; i++) { const y = R() * h, amp = 4 + R() * 10, ph = R() * 6; g.strokeStyle = R() < .6 ? dark : light; g.lineWidth = 1 + R() * 2.5; g.globalAlpha = .35 + R() * .35;
    g.beginPath(); for (let x = 0; x <= w; x += 8) g.lineTo(x, y + Math.sin(x / 70 + ph) * amp); g.stroke(); }
  g.globalAlpha = .6; for (let k = 0; k < 3; k++) { const x = R() * w, y = R() * h; g.fillStyle = dark; g.beginPath(); g.ellipse(x, y, 10, 6, 0, 0, 7); g.fill(); g.strokeStyle = dark; for (let r = 14; r < 40; r += 7) { g.beginPath(); g.ellipse(x, y, r * 1.6, r * .7, 0, 0, 7); g.stroke(); } }
  g.globalAlpha = 1;
}, rx, ry);
export const sisalTex = () => tex('sisal', 256, 256, (g, w, h) => {   // twisted rope wound round the post
  g.fillStyle = '#d8b57d'; g.fillRect(0, 0, w, h);
  for (let y = -w; y < h + w; y += 16) { g.strokeStyle = '#b8935b'; g.lineWidth = 5; g.beginPath(); g.moveTo(0, y); g.lineTo(w, y + 22); g.stroke();
    g.strokeStyle = '#ecd2a0'; g.lineWidth = 2; g.beginPath(); g.moveTo(0, y + 6); g.lineTo(w, y + 28); g.stroke();
    for (let x = 0; x < w; x += 12) { g.strokeStyle = 'rgba(120,90,50,.35)'; g.lineWidth = 1; g.beginPath(); g.moveTo(x, y + x * 22 / w + 2); g.lineTo(x + 6, y + x * 22 / w + 12); g.stroke(); } }
}, 3, 6);
export const carpetTex = (c, c2, rx = 2, ry = 2) => tex('carpet' + c + c2, 256, 256, (gg, w, h, R) => { gg.fillStyle = c; gg.fillRect(0, 0, w, h); for (let i = 0; i < 1800; i++) { gg.fillStyle = R() < .5 ? c2 : 'rgba(255,255,255,.16)'; gg.fillRect(R() * w, R() * h, 2, 1); }
  const paw = (x, y) => { gg.fillStyle = 'rgba(255,255,255,.5)'; gg.beginPath(); gg.ellipse(x, y, 13, 11, 0, 0, 7); gg.fill(); for (const [dx, dy] of [[-15, -14], [-5, -21], [6, -21], [16, -14]]) { gg.beginPath(); gg.ellipse(x + dx, y + dy, 5, 6, 0, 0, 7); gg.fill(); } };
  paw(64, 80); paw(190, 200); }, rx, ry);
export const barkTex = () => tex('bark', 256, 512, (g, w, h, R) => { g.fillStyle = '#9a6a40'; g.fillRect(0, 0, w, h); for (let i = 0; i < 40; i++) { const x = R() * w; g.strokeStyle = R() < .5 ? '#7d5330' : '#b5824f'; g.lineWidth = 3 + R() * 6; g.beginPath(); g.moveTo(x, 0); for (let y = 0; y <= h; y += 32) g.lineTo(x + Math.sin(y / 60 + i) * 6, y); g.stroke(); } }, 2, 1);
const flat = c => tex('flat' + c, 4, 4, (g, w, h) => { g.fillStyle = c; g.fillRect(0, 0, w, h); });
const fishShape = (g, x, y, s, body, eye) => {   // a little fish drawn on glaze, a label or a print
  g.fillStyle = body; g.beginPath(); g.ellipse(x, y, 26 * s, 10 * s, 0, 0, 7); g.fill();
  g.beginPath(); g.moveTo(x + 22 * s, y); g.lineTo(x + 40 * s, y - 11 * s); g.lineTo(x + 40 * s, y + 11 * s); g.closePath(); g.fill();
  if (eye) { g.fillStyle = eye; g.beginPath(); g.arc(x - 14 * s, y - 2 * s, 2.4 * s, 0, 7); g.fill(); }
};

export const std = o => new THREE.MeshStandardMaterial({ roughness: .85, ...o });
const matCache = new Map();
const M = (key, make) => { if (!matCache.has(key)) matCache.set(key, make()); return matCache.get(key); };

// a part: one mesh, one material, a role (see top). userData.sdf(x, y, z, s): its signed distance in its own
// frame (s = its world scale), for kitField (the QA checks cats against the parts' exact shapes)
const part = (g, geo, mat, role = 'both', name = '', sdf = null) => { const m = new THREE.Mesh(geo, mat); m.userData.role = role; if (sdf) m.userData.sdf = sdf; m.name = name || role; m.castShadow = m.receiveShadow = true; g.add(m); return m; };
const mn3 = s => Math.min(s[0], s[1], s[2]);
const box = (g, mat, w, h, l, x, y, z, r = .012, role) => { const rr = Math.min(r, h / 2 - .001, w / 2 - .001, l / 2 - .001);
  const m = part(g, new RoundedBoxGeometry(w, h, l, 4, rr), mat, role, '', (X, Y, Z, s) => sdRoundBox(X, Y, Z, w / 2 * s[0], h / 2 * s[1], l / 2 * s[2], rr * mn3(s))); m.position.set(x, y, z); return m; };
const sdCone = (X, Y, Z, rTop, rBot, h) => { const t = Math.min(1, Math.max(0, (Y + h / 2) / h)), r = rBot + (rTop - rBot) * t;
  const dx = (Math.hypot(X, Z) - r) / Math.hypot(1, (rTop - rBot) / h), dy = Math.abs(Y) - h / 2; return Math.min(Math.max(dx, dy), 0) + Math.hypot(Math.max(dx, 0), Math.max(dy, 0)); };
const cyl = (g, mat, r0, r1, h, x, y, z, seg = 32, role) => { const m = part(g, new THREE.CylinderGeometry(r0, r1, h, seg), mat, role, '', (X, Y, Z, s) => sdCone(X / s[0], Y / s[1], Z / s[2], r0, r1, h) * mn3(s)); m.position.set(x, y, z); return m; };
const sph = (g, mat, r, x, y, z, sx = 1, sy = 1, sz = 1, seg = 24, role) => { const m = part(g, new THREE.SphereGeometry(r, seg, Math.max(8, seg * .6 | 0)), mat, role, '', (X, Y, Z, s) => sdEllipsoid(X, Y, Z, r * s[0], r * s[1], r * s[2])); m.position.set(x, y, z); m.scale.set(sx, sy, sz); return m; };
const torus = (g, mat, R, t, rs = 8, ts = 64, role) => part(g, new THREE.TorusGeometry(R, t, rs, ts), mat, role, '', (X, Y, Z, s) => (Math.hypot(Math.hypot(X / s[0], Y / s[1]) - R, Z / s[2]) - t) * mn3(s));
// a lathe: exact distance to its (r, y) outline (closed along the axis)
const sdPoly = (px, py, P) => { let d = (px - P[0][0]) ** 2 + (py - P[0][1]) ** 2, sg = 1;
  for (let i = 0, j = P.length - 1; i < P.length; j = i++) { const ex = P[j][0] - P[i][0], ey = P[j][1] - P[i][1], wx = px - P[i][0], wy = py - P[i][1], L = ex * ex + ey * ey || 1e-12, t = Math.min(1, Math.max(0, (wx * ex + wy * ey) / L));
    d = Math.min(d, (wx - ex * t) ** 2 + (wy - ey * t) ** 2); const c1 = py >= P[i][1], c2 = py < P[j][1], c3 = ex * wy > ey * wx; if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) sg = -sg; }
  return sg * Math.sqrt(d); };
const lathe = (g, prof, seg, mat, role, name) => { const P = prof.map(v => [v.x, v.y]); if (P[0][0] > 1e-9) P.unshift([0, P[0][1]]); if (P.at(-1)[0] > 1e-9) P.push([0, P.at(-1)[1]]);
  return part(g, new THREE.LatheGeometry(prof, seg), mat, role, name, (X, Y, Z, s) => sdPoly(Math.hypot(X / s[0], Z / s[2]), Y / s[1], P) * mn3(s)); };
// a chain of capsules through points (string, tail, loose yarn): each link a closed capsule
const capsule = (g, mat, r, L, role) => part(g, new THREE.CapsuleGeometry(r, L, 4, 8), mat, role, '', (X, Y, Z, s) => sdCapsule(X / s[0], Y / s[1], Z / s[2], [0, -L / 2, 0], [0, L / 2, 0], r) * mn3(s));
const chain = (g, mat, pts, r, role) => { for (let i = 0; i < pts.length - 1; i++) { const a = V(...pts[i]), b = V(...pts[i + 1]), d = b.clone().sub(a), L = d.length();
  const m = capsule(g, mat, r, L, role); m.position.copy(a).add(b).multiplyScalar(.5); m.quaternion.setFromUnitVectors(V(0, 1, 0), d.normalize()); } };
// an SDF solid as one closed mesh (surface nets), with UVs from `uv(x, y, z, n)`
function sdfPart(g, field, bounds, h, mat, uv, role) {
  if (typeof document === 'undefined') { const m = part(g, new THREE.BufferGeometry(), mat, role, '', (X, Y, Z, s) => field(X, Y, Z)); return m; }   // (node: the field is all kitField needs)
  const F = { eval: field, bounds: () => bounds, grad: null };
  F.grad = (x, y, z, e = 1e-4) => [(field(x + e, y, z) - field(x - e, y, z)) / (2 * e), (field(x, y + e, z) - field(x, y - e, z)) / (2 * e), (field(x, y, z + e) - field(x, y, z - e)) / (2 * e)];
  const mesh = relax(F, surfaceNets(F, h, .02), 3), n = mesh.pos.length / 3;
  const uvs = new Float32Array(2 * n), nrm = new Float32Array(3 * n);
  for (let i = 0; i < n; i++) { const x = mesh.pos[3 * i], y = mesh.pos[3 * i + 1], z = mesh.pos[3 * i + 2], gr = F.grad(x, y, z), l = Math.hypot(...gr) || 1;
    nrm.set([gr[0] / l, gr[1] / l, gr[2] / l], 3 * i); uvs.set(uv(x, y, z, [gr[0] / l, gr[1] / l, gr[2] / l]), 2 * i); }
  const geo = new THREE.BufferGeometry(); geo.setAttribute('position', new THREE.BufferAttribute(mesh.pos, 3)); geo.setAttribute('normal', new THREE.BufferAttribute(nrm, 3)); geo.setAttribute('uv', new THREE.BufferAttribute(uvs, 2)); geo.setIndex(new THREE.BufferAttribute(mesh.idx, 1));
  return part(g, geo, mat, role, '', (X, Y, Z) => field(X, Y, Z));
}
const len2 = Math.hypot;
const sdRoundBox = (x, y, z, bx, by, bz, r) => { const qx = Math.abs(x) - bx + r, qy = Math.abs(y) - by + r, qz = Math.abs(z) - bz + r; return Math.hypot(Math.max(qx, 0), Math.max(qy, 0), Math.max(qz, 0)) + Math.min(Math.max(qx, qy, qz), 0) - r; };
const sdCapsule = (x, y, z, a, b, r) => { const bx = b[0] - a[0], by = b[1] - a[1], bz = b[2] - a[2], px = x - a[0], py = y - a[1], pz = z - a[2]; const h = Math.min(1, Math.max(0, (px * bx + py * by + pz * bz) / (bx * bx + by * by + bz * bz))); return Math.hypot(px - bx * h, py - by * h, pz - bz * h) - r; };
// box-projected UVs (metres * k): for noise-like textures on SDF solids
const boxUV = k => (x, y, z, n) => Math.abs(n[1]) > .6 ? [x * k, z * k] : Math.abs(n[0]) > Math.abs(n[2]) ? [z * k, y * k] : [x * k, y * k];
// a pompom on a string, hanging from (x, y, z)
const pompom = (g, x, y, z, len = .14, col = '#ff7f9f') => {
  cyl(g, M('string', () => std({ color: '#fffaf0' })), .002, .002, len, x, y - len / 2, z, 6);
  const pg = new THREE.IcosahedronGeometry(.028, 4), pp = pg.attributes.position;
  for (let i = 0; i < pp.count; i++) { const v = V(pp.getX(i), pp.getY(i), pp.getZ(i)); v.multiplyScalar(1 + .12 * Math.sin(v.x * 400) * Math.sin(v.y * 380) * Math.sin(v.z * 420)); pp.setXYZ(i, v.x, v.y, v.z); }
  pg.computeVertexNormals(); const m = part(g, pg, M('pom' + col, () => std({ color: col, roughness: 1 }))); m.position.set(x, y - len - .02, z);
};
const screwM = () => M('screw', () => std({ color: '#8a7a68', roughness: .5 }));

// ---------------------------------------------------------------- cushion (mockup as is)
// game size: radius .544, the seat top (where a cat lies) at .134; userData.press: the dent a loafing cat makes
// (a morph target on the seat: 'Press')
export const CUSHION_VARIANTS = [
  { name: '복숭아', check: ['#ffd9c9', '#ff9f8a', '#e9785f'], roll: '#ff9f8a', pipe: '#fff3e2', button: '#e9785f' },
  { name: '민트', check: ['#e3f6ec', '#8fd6b1', '#5bb98c'], roll: '#8fd6b1', pipe: '#fffaf0', button: '#4aa47a' },
  { name: '라일락', check: ['#efe6ff', '#bfa8f2', '#9a80e0'], roll: '#bfa8f2', pipe: '#fffaf0', button: '#8a6fd4' },
];
export const CUSHION_PRESS = { x: 0, z: .02, rx: .2, rz: .3, d: .035 };
export function cushion(v, press = null) {
  const g = new THREE.Group(), R = .544, top = .134;
  const prof = []; const Rs = R - .07;
  for (let i = 0; i <= 40; i++) { const t = i / 40, r = Rs * t, y = top - .006 * t * t - .05 * Math.pow(t, 5); prof.push(new THREE.Vector2(r, y)); }
  prof.push(new THREE.Vector2(Rs + .02, .06), new THREE.Vector2(Rs, .0), new THREE.Vector2(0, 0));
  const seatProf = prof.slice().reverse(); let seatG = new THREE.LatheGeometry(seatProf, 96);
  seatG = mergeVertices(seatG);
  const p = seatG.attributes.position, dent = (x, z, P) => { const dx = (x - P.x) / P.rx, dz = (z - P.z) / P.rz; return P.d * Math.exp(-(dx * dx + dz * dz) * 1.4); };
  const pressed = new Float32Array(p.count * 3);
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i), y = p.getY(i), z = p.getZ(i), r = Math.hypot(x, z);
    let dy = -.018 * Math.exp(-(r * r) / (2 * .035 * .035));
    for (let k = 0; k < 4; k++) { const a = k * Math.PI / 2 + Math.PI / 4, ex = x - Math.cos(a) * .2, ez = z - Math.sin(a) * .2; dy -= .014 * Math.exp(-(ex * ex + ez * ez) / (2 * .03 * .03)); }
    const w = y > .03 ? Math.min(1, (y - .03) / .08) : 0;
    const dp = press ? dent(x, z, press) : 0;
    p.setY(i, y + (dy - dp) * w);
    pressed[3 * i + 1] = -dent(x, z, CUSHION_PRESS) * w;   // (morph: the dent of a loafing cat, as an offset)
  }
  { const uv = seatG.attributes.uv; for (let i = 0; i < p.count; i++) uv.setXY(i, p.getX(i) / (2 * R) + .5, p.getZ(i) / (2 * R) + .5); }   // (the check lies flat across the top)
  seatG.computeVertexNormals();
  seatG.morphAttributes.position = [new THREE.BufferAttribute(pressed, 3)]; seatG.morphTargetsRelative = true;
  const SP = [[0, prof[0].y], ...prof.map(v2 => [v2.x, v2.y])];
  const seat = part(g, seatG, M('seat' + v.name, () => std({ map: gingham(...v.check), roughness: .95 })), 'both', 'Seat', (X, Y, Z, s) => sdPoly(Math.hypot(X, Z), Y, SP));
  seat.morphTargetDictionary = { Press: 0 }; seat.morphTargetInfluences = [0];
  const roll = torus(g, M('roll' + v.name, () => std({ map: felt(v.roll, 'rgba(0,0,0,.06)'), roughness: .95 })), R - .08, .08, 32, 128);
  roll.rotation.x = Math.PI / 2; roll.position.y = .08; roll.scale.z = 1.05;
  for (const [rr, yy] of [[R - .08, .162], [R, .08]]) {
    const pipe = torus(g, M('pipe' + v.pipe, () => std({ color: v.pipe, roughness: .8 })), rr, .009, 10, 128); pipe.rotation.x = Math.PI / 2; pipe.position.y = yy;
  }
  for (let k = 0; k < 64; k++) { const a = k / 64 * Math.PI * 2, s = part(g, new THREE.CapsuleGeometry(.0035, .016, 4, 8), M('pipe' + v.pipe, () => std({ color: v.pipe })), 'detail');
    s.position.set(Math.cos(a) * (Rs - .03), top - .028, Math.sin(a) * (Rs - .03)); s.rotation.set(Math.PI / 2, 0, -a); }
  const btnM = M('btn' + v.button, () => std({ color: v.button, roughness: .45 }));
  for (let k = 0; k < 4; k++) { const a = k * Math.PI / 2 + Math.PI / 4; cyl(g, btnM, .03, .033, .014, Math.cos(a) * .2, top - .011, Math.sin(a) * .2, 24); }
  cyl(g, btnM, .04, .043, .016, 0, top - .014, 0, 32);
  for (const s of [-1, 1]) cyl(g, M('thread', () => std({ color: '#5a4632' })), .007, .007, .017, s * .013, top - .012, 0, 12, 'detail');
  return g;
}

// ---------------------------------------------------------------- dishes (mockup food bowl; water and milk the same dish, other pictures)
// game size: radius .16, food / liquid surface at BOWL_SURF .03
export const BOWL_VARIANTS = {
  food_bowl: [{ name: '산호', glaze: '#ff8d7a', foot: '#e8644f' }, { name: '하늘', glaze: '#6fc3ec', foot: '#3fa0d6' }, { name: '버터', glaze: '#ffcf5a', foot: '#f0a92c' }],
  water_bowl: [{ name: '바다', glaze: '#4fb6e8', foot: '#2f93cc' }, { name: '민트', glaze: '#6fd2b0', foot: '#3fb38d' }, { name: '라일락', glaze: '#b8a2f0', foot: '#957ad8' }],
  milk_bowl: [{ name: '젖소', glaze: '#fff6ea', foot: '#5a4632' }, { name: '딸기', glaze: '#ffc2d1', foot: '#ff7f9f' }],
};
function dish(id, v) {
  const g = new THREE.Group(), R = .16, H = .033;
  const prof = [[0, .0], [R - .03, 0], [R - .028, .004], [R - .022, .004], [R - .012, .012], [R - .002, H - .006], [R + .002, H], [R - .004, H + .006], [R - .012, H + .003], [R - .02, H - .004], [R - .03, H - .01], [0, H - .012]];

  const glaze = tex('glaze' + id + v.name, 1024, 128, (gg, w, h) => {
    gg.fillStyle = v.glaze; gg.fillRect(0, 0, w, h);
    gg.fillStyle = '#fff6ea'; gg.fillRect(0, h * .62, w, h * .38);      // inside (cream)
    if (id === 'milk_bowl' && v.name === '젖소') { gg.fillStyle = '#fbe9d6'; gg.fillRect(0, h * .62, w, h * .38); }
    gg.fillStyle = id === 'milk_bowl' && v.name === '젖소' ? '#5a4632' : '#fff6ea'; gg.fillRect(0, h * .5, w, h * .06);   // rim line
    gg.fillStyle = v.foot; gg.fillRect(0, h * .08, w, h * .05);          // foot line
    for (let k = 0; k < 4; k++) {
      const x = (k + .5) * w / 4, y = h * .3;
      if (id === 'food_bowl') fishShape(gg, x, y, 1, '#fff6ea', '#5a4632');
      else if (id === 'water_bowl') { gg.fillStyle = '#fff6ea'; for (const [dx, s] of [[-30, 1], [6, .7], [34, .85]]) { const cx = x + dx, cy = y + (s - .8) * 12; gg.beginPath(); gg.moveTo(cx, cy - 14 * s); gg.bezierCurveTo(cx + 10 * s, cy - 2 * s, cx + 9 * s, cy + 9 * s, cx, cy + 9 * s); gg.bezierCurveTo(cx - 9 * s, cy + 9 * s, cx - 10 * s, cy - 2 * s, cx, cy - 14 * s); gg.fill(); } }
      else { gg.fillStyle = v.name === '젖소' ? '#5a4632' : '#fff6ea'; for (const [dx, dy, rx, ry, rot] of [[-26, -6, 16, 9, .3], [10, 8, 12, 7, -.4], [34, -10, 8, 6, .8]]) { gg.beginPath(); gg.ellipse(x + dx, y + dy, rx, ry, rot, 0, 7); gg.fill(); } }
    }
  });
  lathe(g, prof.map(([r, y]) => new THREE.Vector2(r, y)), 96, M('dish' + id + v.name, () => std({ map: glaze, roughness: .35 })), 'both', 'Dish');
  if (id === 'food_bowl') {
    // kibble: a heap of round and flat pieces crowned at the food surface (baked onto a dome in the game mesh)
    const R2 = rng(5), kc = ['#b06b35', '#8a4f26', '#c98447'];
    sph(g, M('kbed', () => std({ color: '#8a4f26', roughness: 1 })), 1, 0, .012, 0, .116, .01, .116, 48, 'detail');
    sph(g, M('kbed', () => std({ color: '#8a4f26', roughness: 1 })), 1, 0, .012, 0, .118, .017, .118, 48, 'proxy').name = 'KibbleDome';
    const kG = [new THREE.CylinderGeometry(.0068, .0068, .0045, 12), new THREE.SphereGeometry(.0062, 10, 8)];
    for (let i = 0; i < 900; i++) {
      const a = R2() * Math.PI * 2, r = Math.sqrt(R2()) * .112, crown = .03 - .004 - (r / .112) ** 2 * .006;
      const m = part(g, kG[i % 2], M('kib' + i % 3, () => std({ color: kc[i % 3], roughness: .9 })), 'detail'); m.position.set(Math.cos(a) * r, crown - R2() * .003, Math.sin(a) * r); m.rotation.set(R2() * 3, R2() * 3, R2() * 3);
    }
  } else {
    // the water / milk: a disc with its surface at BOWL_SURF, a little shine
    const liquid = id === 'water_bowl' ? '#7fd0f0' : '#fffdf6';
    cyl(g, M('liq' + id, () => std({ color: liquid, roughness: .15 })), .128, .126, .012, 0, .03 - .006, 0, 64);
  }
  return g;
}

// ---------------------------------------------------------------- cat towers: kit of parts (mockup KIT), six designs
// Deck tops on a 0.4 m grid (STEP); every deck a cat lands on is at least 0.6 x 0.7 m. The game keeps the low
// 1-deck tower (cat_tower_1, deck at DECK_STEP .2) until the 0.4 m jump and the jump down exist (step B).
export const STEP = .4;
export const TOWER_VARIANTS = [
  { name: '하늘', carpets: ['#7fcbe8', '#ffb3c4', '#fff1d6', '#a6e3c4'] },
  { name: '딸기', carpets: ['#ffb3c4', '#7fcbe8', '#a6e3c4', '#fff1d6'] },
];
const kitM = v => {
  const c = TOWER_VARIANTS[v] ? TOWER_VARIANTS[v].carpets : TOWER_VARIANTS[0].carpets, shade = ['rgba(40,90,120,.16)', 'rgba(150,60,80,.14)', 'rgba(120,90,50,.12)', 'rgba(40,110,80,.14)'];
  const carpet = i => M('carpet' + c[i], () => std({ map: carpetTex(c[i], shade[TOWER_VARIANTS[0].carpets.indexOf(c[i])]), roughness: 1 }));
  return {
    woodA: M('woodA', () => std({ map: woodTex('#c98f58', '#a06a3a', '#e0b07a'), roughness: .7 })), woodB: M('woodB', () => std({ map: woodTex('#d29a62', '#a8703f', '#e8bd87'), roughness: .7 })),
    woodW: M('woodW', () => std({ map: woodTex('#f3e6cf', '#d9c3a0', '#fffaf0'), roughness: .7 })), sisal: M('sisal', () => std({ map: sisalTex(), roughness: 1 })), bark: M('bark', () => std({ map: barkTex(), roughness: .95 })),
    blue: carpet(0), pink: carpet(1), cream: carpet(2), mint: carpet(3),
    leaf: M('leaf', () => std({ map: felt('#66b84a', 'rgba(30,80,20,.16)'), roughness: .95 })), leaf2: M('leaf2', () => std({ map: felt('#4fa53b', 'rgba(20,60,15,.16)'), roughness: .95 })),
    butter: M('butter', () => std({ map: woodTex('#f6c768', '#e0a83f', '#ffe09a'), roughness: .6 })), dark: M('dark', () => std({ color: '#3b2a22', roughness: 1 })),
  };
};
function makeKit(v) {
  const K = kitM(v);
  const post = (g, x, z, y0, y1, r = .05, mat = K.sisal) => { cyl(g, mat, r, r, y1 - y0 - .04, x, (y0 + y1) / 2, z); cyl(g, K.woodA, r + .012, r + .012, .025, x, y0 + .0125, z); cyl(g, K.woodA, r + .012, r + .012, .025, x, y1 - .0125, z); };
  const deck = (g, x, y, z, w, l, carpetM = K.blue, bed = false, round = false) => {
    if (round) { cyl(g, K.woodB, w / 2, w / 2, .04, x, y - .026, z, 48); cyl(g, carpetM, w / 2 - .03, w / 2 - .03, .008, x, y - .004, z, 48);
      if (bed) { const t = torus(g, carpetM, w / 2 - .035, .035, 16, 64); t.rotation.x = Math.PI / 2; t.position.set(x, y + .03, z); } return; }
    box(g, K.woodB, w, .04, l, x, y - .026, z); box(g, carpetM, w - .06, .008, l - .06, x, y - .004, z, .004);   // (carpet top = the deck height y)
    if (bed) for (const [ww, ll, dx, dz] of [[w, .06, 0, -l / 2 + .03], [w, .06, 0, l / 2 - .03], [.06, l - .12, -w / 2 + .03, 0], [.06, l - .12, w / 2 - .03, 0]]) box(g, carpetM, ww, .06, ll, x + dx, y + .03, z + dz, .028);
  };
  const base = (g, w, l, x = 0, z = 0) => box(g, K.woodA, w, .03, l, x, .015, z);
  const screws = (g, x, y, z, w, l) => { for (const sx of [-1, 1]) for (const sz of [-1, 1]) cyl(g, screwM(), .008, .008, .004, x + sx * (w / 2 - .022), y + .006, z + sz * (l / 2 - .022), 16, 'detail'); };
  const house = (g, x, z, w, h, l, wallM, roofM) => {
    box(g, wallM, w, h, l, x, h / 2, z, .05);
    const door = cyl(g, K.dark, .16, .16, .006, x, .2, z + l / 2 + .001, 40); door.rotation.x = Math.PI / 2;   // (a thin disc: closed)
    const ring = torus(g, roofM, .165, .022, 12, 48); ring.position.set(x, .2, z + l / 2 + .004);
    deck(g, x, h + .02, z, w, l, roofM);
  };
  // a hammock: a sagging cloth slab (closed: a thin box bent down) on four strings
  const hammock = (g, x, y, z, w, l, mat) => {
    const geo = mergeVertices(new THREE.BoxGeometry(w, .012, l, 16, 1, 16).deleteAttribute('normal').deleteAttribute('uv')), pp = geo.attributes.position;
    for (let i = 0; i < pp.count; i++) { const u = pp.getX(i) / (w / 2), vv = pp.getZ(i) / (l / 2); pp.setY(i, pp.getY(i) - .09 * (1 - u * u) * (1 - vv * vv)); }
    const uv = new Float32Array(pp.count * 2); for (let i = 0; i < pp.count; i++) { uv[2 * i] = pp.getX(i) * 3; uv[2 * i + 1] = pp.getZ(i) * 3; } geo.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    geo.computeVertexNormals(); const m = part(g, geo, mat); m.position.set(x, y, z);
    for (const sx of [-1, 1]) for (const sz of [-1, 1]) cyl(g, M('string', () => std({ color: '#fffaf0' })), .004, .004, .16, x + sx * w / 2, y + .08, z + sz * l / 2, 6);
  };
  const leafPad = (g, x, y, z, r, rot, mat) => {
    const lg = new THREE.Group(); lg.position.set(x, y, z); lg.rotation.y = rot; g.add(lg);
    // the lobes as ONE solid (overlapping coplanar discs would z-fight and bake dark): a union of rounded discs
    const lobes = [[0, 0, r], [r * .55, -r * .2, r * .62], [-r * .55, -r * .2, r * .62], [0, r * .6, r * .55]];
    const f = (X, Y, Z) => { let d = 1e9; for (const [dx, dz, rr] of lobes) { const ex = Math.hypot(X - dx, Z - dz) - rr + .01, ey = Math.abs(Y + .025) - .025 + .01; d = Math.min(d, Math.min(Math.max(ex, ey), 0) + Math.hypot(Math.max(ex, 0), Math.max(ey, 0)) - .01); } return d; };
    // outline of the union (star-shaped round its centre): the farthest inside point along each direction; extruded with a soft bevel
    const sh = new THREE.Shape();
    for (let k = 0; k < 120; k++) { const a = k / 120 * Math.PI * 2, cx = Math.cos(a), cz = Math.sin(a); let lo = 0, hi = 2 * r;
      for (let i = 0; i < 30; i++) { const m = (lo + hi) / 2; if (f(cx * m, -.025, cz * m) < -.008) lo = m; else hi = m; }   // (inset by the bevel)
      k ? sh.lineTo(cx * lo, -cz * lo) : sh.moveTo(cx * lo, -cz * lo); }
    const geo = new THREE.ExtrudeGeometry(sh, { depth: .034, bevelEnabled: true, bevelThickness: .008, bevelSize: .008, bevelSegments: 3, curveSegments: 1 });
    geo.rotateX(-Math.PI / 2); geo.translate(0, -.042, 0);
    { const uv = geo.attributes.uv, pp = geo.attributes.position; for (let i = 0; i < pp.count; i++) uv.setXY(i, pp.getX(i) * 2, pp.getZ(i) * 2 + pp.getY(i) * 2); }
    part(lg, geo, mat, 'both', 'LeafPad', (X, Y, Z) => f(X, Y, Z));
    box(lg, K.leaf2, .02, .006, r * 1.6, 0, .002, r * .1, .003);
  };
  // a tunnel: a thick-walled tube lying along x (closed: a lathe of its wall section)
  const tunnel = (g, x, y, z, r, len, mat) => {
    const t = .018, prof = [V(r - t, -len / 2), V(r, -len / 2), V(r, len / 2), V(r - t, len / 2), V(r - t, -len / 2)].map(p => new THREE.Vector2(p.x, p.y));
    const m = lathe(g, prof, 48, mat); m.rotation.z = Math.PI / 2; m.position.set(x, y, z);
  };
  return { M: K, post, deck, base, screws, house, hammock, leafPad, tunnel, pompom };
}
// the game's low tower (mockup tower(): deck top at DECK_STEP .2, deck .72 x 1.0, base .80 x 1.08)
function tower1(v) {
  const g = new THREE.Group(), DW = .72, DL = 1.0, DT = .04, Y = .2, K = kitM(v);
  box(g, K.woodA, DW + .08, .03, DL + .08, 0, .015, 0);
  // (the carpet's top is the deck a cat lands on: exactly DECK_STEP; the board sits under it)
  box(g, K.woodB, DW, DT, DL, 0, Y - DT / 2 - .006, 0);
  box(g, K.blue, DW - .08, .008, DL - .08, 0, Y - .004, 0, .004);
  // the rim: set 6 mm in from the board's edge, 2.8 cm high, well rounded: a cat's hind paws pass over it jumping down (qa_items)
  const ri = .006, rw = .045, rh = .028;
  for (const [w, l, x, z] of [[DW - 2 * ri, rw, 0, -DL / 2 + ri + rw / 2], [DW - 2 * ri, rw, 0, DL / 2 - ri - rw / 2], [rw, DL - 2 * ri - 2 * rw, -DW / 2 + ri + rw / 2, 0], [rw, DL - 2 * ri - 2 * rw, DW / 2 - ri - rw / 2, 0]]) box(g, K.butter, w, rh, l, x, Y + rh / 2, z, .016);
  for (const [px, pz] of [[-DW / 2 + .05, -DL / 2 + .05], [DW / 2 - .05, -DL / 2 + .05], [-DW / 2 + .05, DL / 2 - .05], [DW / 2 - .05, DL / 2 - .05]]) {
    const h = Y - DT - .03;
    cyl(g, K.sisal, .042, .042, h - .03, px, .03 + h / 2, pz);
    for (const yy of [.03 + .012, Y - DT - .012]) cyl(g, K.woodA, .052, .052, .024, px, yy, pz);
  }
  for (const sx of [-1, 1]) for (const sz of [-1, 1]) cyl(g, screwM(), .008, .008, .004, sx * (DW / 2 - .022), Y + .033, sz * (DL / 2 - .022), 16, 'detail');
  pompom(g, DW / 2 - .02, Y - DT, DL / 2 - .05, .12);
  return g;
}
export const TOWERS = {
  stool: { ko: '1단 스툴형', top: [0, STEP + .01, 0], decks: [{ y: STEP, at: [0, 0], size: [1.0, 1.0], round: true }], build: (g, K) => { K.post(g, 0, 0, .03, STEP - .04, .1); cyl(g, K.M.woodA, .4, .42, .03, 0, .015, 0, 48); K.deck(g, 0, STEP, 0, 1.0, 1.0, K.M.pink, true, true); K.pompom(g, .42, STEP - .04, .2); } },
  stairs: { ko: '2단 계단형', top: [.32, 2 * STEP, 0], decks: [{ y: STEP, at: [-.4, 0], size: [.7, .85] }, { y: 2 * STEP, at: [.32, 0], size: [.75, .85] }], build: (g, K) => { K.base(g, 1.5, .95);
    K.post(g, -.6, -.3, .03, STEP - .04); K.post(g, -.6, .3, .03, STEP - .04); K.post(g, -.05, -.3, .03, 2 * STEP - .04); K.post(g, -.05, .3, .03, 2 * STEP - .04); K.post(g, .6, 0, .03, 2 * STEP - .04);
    K.deck(g, -.4, STEP, 0, .7, .85, K.M.blue); K.screws(g, -.4, STEP, 0, .7, .85); K.deck(g, .32, 2 * STEP, 0, .75, .85, K.M.cream, true); K.pompom(g, .66, 2 * STEP - .04, .38, .2); } },
  house: { ko: '3단 하우스형', top: [.02, 3 * STEP, 0], decks: [{ y: STEP, at: [-.32, 0], size: [.66, .8] }, { y: 2 * STEP, at: [.38, 0], size: [.64, .9] }, { y: 3 * STEP, at: [.02, 0], size: [1.3, .95] }], build: (g, K) => { K.base(g, 1.35, 1.0);
    K.house(g, -.32, 0, .66, STEP - .02, .8, K.M.woodW, K.M.mint);
    K.post(g, .16, -.36, .03, 2 * STEP - .04); K.post(g, .16, .36, .03, 2 * STEP - .04); K.post(g, .6, -.36, .03, 3 * STEP - .04); K.post(g, .6, .36, .03, 3 * STEP - .04);
    K.post(g, -.55, -.3, STEP + .02, 3 * STEP - .04); K.post(g, -.55, .3, STEP + .02, 3 * STEP - .04);
    K.deck(g, .38, 2 * STEP, 0, .64, .9, K.M.mint); K.hammock(g, .38, STEP + .05, 0, .36, .6, K.M.cream);
    K.deck(g, .02, 3 * STEP, 0, 1.3, .95, K.M.pink, true); K.pompom(g, .68, 2 * STEP - .04, .3); } },
  tree: { ko: '3단 나무형', top: [.02, 3 * STEP + .02, -.05], decks: [{ y: STEP, at: [.38, .1], size: [.76, .76], leaf: true }, { y: 2 * STEP, at: [-.36, -.05], size: [.76, .76], leaf: true }, { y: 3 * STEP + .02, at: [.02, -.05], size: [.88, .88], leaf: true }], build: (g, K) => { cyl(g, K.M.woodA, .5, .52, .03, 0, .015, 0, 48);
    cyl(g, K.M.bark, .12, .15, 3 * STEP - .08, 0, (3 * STEP - .08) / 2 + .03, 0, 32);
    K.leafPad(g, .38, STEP, .1, .38, .4, K.M.leaf); K.leafPad(g, -.36, 2 * STEP, -.05, .38, -2.6, K.M.leaf); K.leafPad(g, .02, 3 * STEP + .02, -.05, .44, 1.3, K.M.leaf);
    for (const [x, y, z] of [[.38, STEP, .1], [-.36, 2 * STEP, -.05], [.05, 3 * STEP + .02, -.2]]) K.post(g, x * .55, z * .55, y - .14, y - .04, .035, K.M.bark);
    K.pompom(g, .6, STEP - .04, .2, .16, '#ffd23f'); } },
  tall: { ko: '5단 높은 타워', top: [0, 5 * STEP, 0], decks: [1, 2, 3, 4].map(i => ({ y: i * STEP, at: [i % 2 ? -.42 : .42, 0], size: [.7, .8] })).concat([{ y: 5 * STEP, at: [0, 0], size: [1.0, .85] }]), build: (g, K) => { K.base(g, 1.55, 1.0);
    const H5 = 5 * STEP - .04;
    K.post(g, -.35, 0, .03, H5, .06); K.post(g, .35, 0, .03, H5, .06);
    const L = [[-.42, K.M.blue], [.42, K.M.cream], [-.42, K.M.pink], [.42, K.M.blue]];
    L.forEach(([x, m], i) => {
      const y = (i + 1) * STEP, side = Math.sign(x), y0 = i < 2 ? .03 : (i - 1) * STEP;
      K.deck(g, x, y, 0, .7, .8, m); K.screws(g, x, y, 0, .7, .8);
      for (const z of [-.3, .3]) K.post(g, x + side * .27, z, y0, y - .04, .035);
    });
    K.deck(g, 0, 5 * STEP, 0, 1.0, .85, K.M.mint, true);
    K.tunnel(g, .47, 2 * STEP + .17, 0, .17, .5, K.M.cream);
    K.pompom(g, -.74, 3 * STEP - .04, .35, .2); K.pompom(g, .74, 2 * STEP - .04, -.35, .14, '#ffd23f'); } },
};
export function buildTower(id, v = 0) { const g = new THREE.Group(); TOWERS[id].build(g, makeKit(v)); return g; }

// ---------------------------------------------------------------- the other items (5-2 table)
// can: a round can with a paper label (fish picture, our colours) and a pull ring; game size r .067, pâté top .077
const CAN_VARIANTS = [{ name: '딸기', band: '#ff7f9f', fish: '#6f5a40' }, { name: '바다', band: '#86cfe6', fish: '#6f5a40' }];
function canFood(v) {
  const g = new THREE.Group(), r = .0672, h = .0576;
  const label = tex('label' + v.name, 1024, 256, (gg, w, hh) => {
    gg.fillStyle = '#fbf6e6'; gg.fillRect(0, 0, w, hh);
    gg.fillStyle = v.band; gg.fillRect(0, 0, w, hh * .22); gg.fillRect(0, hh * .78, w, hh * .22);
    gg.fillStyle = '#8fca5e'; gg.fillRect(0, hh * .22, w, hh * .04); gg.fillRect(0, hh * .74, w, hh * .04);
    for (let k = 0; k < 3; k++) fishShape(gg, (k + .5) * w / 3, hh * .5, 2.4, v.fish, '#fbf6e6');
    gg.fillStyle = v.fish; for (let k = 0; k < 3; k++) { const x = (k + .5) * w / 3 + 95; for (let d = 0; d < 3; d++) { gg.beginPath(); gg.arc(x + d * 14, hh * .5 - 20 + d * 6, 4, 0, 7); gg.fill(); } }   // (bubbles)
  });
  const tin = M('tin', () => std({ color: '#d5d9e0', roughness: .35 }));
  cyl(g, M('label' + v.name, () => std({ map: label, roughness: .7 })), r, r, h - .012, 0, h / 2, 0, 48);
  for (const yy of [.005, h - .005]) { const t = torus(g, tin, r - .001, .005, 8, 64); t.rotation.x = Math.PI / 2; t.position.y = yy; }
  cyl(g, tin, r - .004, r - .004, .006, 0, h - .006, 0, 48);
  sph(g, M('pate', () => std({ map: felt('#c97b5a', 'rgba(120,50,30,.25)', 2, 2), roughness: .9 })), 1, 0, h - .004, 0, r - .012, .023, r - .012, 32);
  const ring = torus(g, tin, .013, .0035, 8, 24); ring.rotation.x = Math.PI / 2; ring.position.set(.03, h + .004, 0); ring.scale.set(1, 1.4, 1);
  return g;
}
// churu: a striped pouch lying flat, a crimped (zig-zag) end and squeeze marks; game size .208 long
const CHURU_VARIANTS = [{ name: '참치', a: '#ff8a3d', b: '#ffd35c' }, { name: '닭가슴살', a: '#8fca5e', b: '#fff1b8' }, { name: '연어', a: '#ff7f9f', b: '#ffe0e8' }];
function churu(v) {
  const g = new THREE.Group(), L = .208, w = .0256, t = .0112;
  const wrap = tex('churu' + v.name, 512, 128, (gg, W, H) => {
    gg.fillStyle = v.b; gg.fillRect(0, 0, W, H);
    gg.fillStyle = v.a; for (let x = -H; x < W; x += 48) { gg.beginPath(); gg.moveTo(x, 0); gg.lineTo(x + 24, 0); gg.lineTo(x + 24 + H * .5, H); gg.lineTo(x + H * .5, H); gg.closePath(); gg.fill(); }
    gg.fillStyle = '#fbf6e6'; gg.fillRect(W * .38, H * .2, W * .24, H * .6); fishShape(gg, W * .5, H * .5, 1.1, v.a, '#6f5a40');
    gg.strokeStyle = 'rgba(0,0,0,.18)'; gg.lineWidth = 2; for (const x of [W * .78, W * .83, W * .88]) { gg.beginPath(); gg.moveTo(x, H * .1); gg.lineTo(x - 6, H * .9); gg.stroke(); }   // (squeeze marks)
  });
  const pouch = box(g, M('churu' + v.name, () => std({ map: wrap, roughness: .5 })), 2 * w, 2 * t, L - .02, 0, t, -.01, .008);
  pouch.geometry.attributes.uv && (() => { const p = pouch.geometry.attributes.position, uv = pouch.geometry.attributes.uv; for (let i = 0; i < p.count; i++) uv.setXY(i, p.getZ(i) / (L - .02) + .5, p.getX(i) / (2 * w) + .5); })();
  // the crimped end: a flat plate with a zig-zag edge (extruded outline: closed)
  const sh = new THREE.Shape(); const n = 8, cw = w * 1.05; sh.moveTo(-cw, 0); for (let i = 0; i <= n; i++) sh.lineTo(-cw + 2 * cw * i / n, i % 2 ? .02 : .026); sh.lineTo(cw, 0); sh.closePath();
  const crimpG = new THREE.ExtrudeGeometry(sh, { depth: .006, bevelEnabled: false }); crimpG.rotateX(Math.PI / 2); crimpG.translate(0, t + .003, L / 2 - .02 - .002);
  part(g, crimpG, M('crimp', () => std({ color: '#fffaf0', roughness: .5 })));
  return g;
}
// ball: a yarn ball (wound-thread pattern, a few raised bands, one loose strand); game size r .056
const BALL_VARIANTS = [{ name: '딸기', c: '#ff6b7d', d: '#e04b5f' }, { name: '레몬', c: '#ffd34d', d: '#e9b322' }, { name: '하늘', c: '#5bb3e8', d: '#3a91c8' }];
function ball(v) {
  const g = new THREE.Group(), r = .056;
  const yarn = tex('yarn' + v.name, 512, 256, (gg, W, H, R) => {
    gg.fillStyle = v.c; gg.fillRect(0, 0, W, H);
    for (let i = 0; i < 70; i++) { const y = R() * H, a = (R() - .5) * .9; gg.strokeStyle = R() < .5 ? v.d : 'rgba(255,255,255,.35)'; gg.lineWidth = 2 + R() * 2; gg.beginPath(); gg.moveTo(0, y); gg.lineTo(W, y + Math.tan(a) * W * .2); gg.stroke(); }
  }, 2, 1);
  const yarnM = M('yarn' + v.name, () => std({ map: yarn, roughness: 1 })), bandM = M('yband' + v.name, () => std({ color: v.d, roughness: 1 }));
  sph(g, yarnM, r, 0, r, 0, 1, 1, 1, 40);
  for (const [rx, rz] of [[.4, 0], [-.3, .9], [1.2, .5]]) { const b = torus(g, bandM, r * .985, .0035, 6, 64); b.position.y = r; b.rotation.set(rx, 0, rz); }
  chain(g, bandM, [[r * .7, r * 1.55, r * .4], [r * 1.05, r * .9, r * .55], [r * 1.5, .004, r * .9], [r * 2.3, .004, r * .7], [r * 2.9, .004, r * 1.1]], .0035);
  return g;
}
// mouse: felt body with a stitched seam, round ears, a string tail and a button nose; body top at TOY_TOP .0608
const MOUSE_VARIANTS = [{ name: '회색', c: '#b9b3ad', ear: '#f3a8b6' }, { name: '분홍', c: '#ffb3c4', ear: '#ff7f9f' }, { name: '민트', c: '#a6e3c4', ear: '#ffd23f' }];
function mouseToy(v) {
  const g = new THREE.Group(), s = 1.6;
  const feltM = M('mfelt' + v.name, () => std({ map: felt(v.c, 'rgba(0,0,0,.08)', 3, 3), roughness: 1 })), earM = M('mear' + v.name, () => std({ color: v.ear, roughness: .9 }));
  sph(g, feltM, 1, 0, .019 * s, 0, .022 * s, .019 * s, .036 * s, 32);
  sph(g, feltM, 1, 0, .018 * s, .034 * s, .014 * s, .013 * s, .02 * s, 24);
  for (const sd of [-1, 1]) { const e = cyl(g, earM, .0075 * s, .0075 * s, .0035 * s, sd * .011 * s, .031 * s, .031 * s, 24); e.rotation.set(Math.PI / 2 - .25, 0, sd * .35); }
  sph(g, M('mnose', () => std({ color: '#6f5a40', roughness: .4 })), .004 * s, 0, .017 * s, .055 * s, 1, 1, 1, 12);
  // the stitched seam along the back: little dashes (baked into the felt)
  for (let k = 0; k < 14; k++) { const z = (-.03 + k * .0045) * s, y = .019 * s + .019 * s * Math.sqrt(Math.max(0, 1 - (z / (.036 * s)) ** 2)); const d = capsule(g, M('stitch', () => std({ color: '#fffaf0' })), .0012 * s, .0022 * s, 'detail'); d.position.set(0, y, z); d.rotation.x = Math.PI / 2; }
  chain(g, M('mtail', () => std({ color: '#fffaf0', roughness: .9 })), Array.from({ length: 7 }, (_, i) => [Math.sin(i * .6) * .012 * s, .006 * s, (-.034 - i * .012) * s]), .003 * s);
  return g;
}
// wand: a wooden stick (grain), a string, feathers and a bell, lying on the floor
const WAND_VARIANTS = [{ name: '분홍', f: ['#ff7f9f', '#b48cf2', '#ffd23f'] }, { name: '파랑', f: ['#5bb3e8', '#8fca5e', '#fff1d6'] }];
function wandToy(v) {
  const g = new THREE.Group(), s = 1.6;
  const stick = capsule(g, M('stick', () => std({ map: woodTex('#d29a62', '#a8703f', '#e8bd87', .2, 2), roughness: .7 })), .007 * s, .37 * s);
  stick.rotation.x = Math.PI / 2; stick.position.set(0, .007 * s, -.065 * s);
  cyl(g, M('grip', () => std({ map: felt('#ff7f9f', 'rgba(0,0,0,.08)'), roughness: 1 })), .009 * s, .009 * s, .07 * s, 0, .007 * s, -.21 * s, 16).rotation.x = Math.PI / 2;
  const pts = Array.from({ length: 9 }, (_, i) => [.06 * Math.sin(i * .5) * s, .0032 * s, (.12 + i * .022) * s]);
  chain(g, M('string', () => std({ color: '#fffaf0' })), pts, .0022 * s);
  const end = pts.at(-1);
  // a bell (a sphere with a slit band) and three feathers fanning out past it
  const bell = sph(g, M('bell', () => std({ color: '#ffd23f', roughness: .3, metalness: .2 })), .011 * s, end[0], .011 * s, end[2] + .01 * s, 1, 1, 1, 20);
  cyl(g, M('slit', () => std({ color: '#6f5a40' })), .0115 * s, .0115 * s, .0016 * s, end[0], .011 * s, end[2] + .01 * s, 20, 'detail');
  v.f.forEach((c, i) => { const a = (i - 1) * .45; const fm = M('feather' + c, () => std({ color: c, roughness: 1 }));
    const f = sph(g, fm, 1, end[0] + Math.sin(a) * .04 * s, .005 * s, end[2] + .02 * s + Math.cos(a) * .04 * s, .009 * s, .004 * s, .034 * s, 20); f.rotation.y = a;
    const q = capsule(g, M('quill', () => std({ color: '#fffaf0' })), .0012 * s, .06 * s, 'detail'); q.position.copy(f.position).setY(.009 * s); q.rotation.set(Math.PI / 2, 0, 0); q.rotation.y = a; });
  return g;
}
// scratcher: a cardboard pad in a wooden frame; flute stripes on the sides, a fish printed on top; top at .0688
const SCRATCH_VARIANTS = [{ name: '코코아', print: '#6f5a40' }, { name: '딸기', print: '#ff7f9f' }];
function scratcher(v) {
  const g = new THREE.Group(), W = .416, L = .8, H = .05, top = .0688, fw = .03;   // (the frame sits lower than the pad: the fluted cardboard edge shows)
  const frameM = M('sframe', () => std({ map: woodTex('#d29a62', '#a8703f', '#e8bd87', 1, 1), roughness: .7 }));
  for (const [w, l, x, z] of [[W, fw, 0, -L / 2 + fw / 2], [W, fw, 0, L / 2 - fw / 2], [fw, L - 2 * fw, -W / 2 + fw / 2, 0], [fw, L - 2 * fw, W / 2 - fw / 2, 0]]) box(g, frameM, w, H, l, x, H / 2, z, .01);
  const card = tex('card' + v.name, 512, 1024, (gg, Wd, Ht) => {
    gg.fillStyle = '#c9a173'; gg.fillRect(0, 0, Wd, Ht);
    for (let y = 0; y < Ht; y += 8) { gg.fillStyle = 'rgba(110,75,40,.35)'; gg.fillRect(0, y, Wd, 3); }   // flutes across the pad
    gg.save(); gg.translate(Wd / 2, Ht / 2); gg.rotate(Math.PI / 2); gg.globalAlpha = .85; fishShape(gg, -30, 0, 6, v.print, '#c9a173'); gg.restore(); gg.globalAlpha = 1;
  });
  const pad = box(g, M('card' + v.name, () => std({ map: card, roughness: 1 })), W - 2 * fw + .004, top - .002, L - 2 * fw + .004, 0, (top - .002) / 2 + .002, 0, .004);   // (top at .0688, where the Stretch paws dig in)
  { const p = pad.geometry.attributes.position, uv = pad.geometry.attributes.uv; for (let i = 0; i < p.count; i++) uv.setXY(i, p.getX(i) / (W - 2 * fw) + .5, p.getZ(i) / (L - 2 * fw) + .5); }
  for (const sx of [-1, 1]) for (const sz of [-1, 1]) cyl(g, screwM(), .007, .007, .003, sx * (W / 2 - fw / 2), H + .0005, sz * (L / 2 - fw / 2), 16, 'detail');
  return g;
}
// hideout: a felt igloo with stitched seams, two little cat ears on the roof and a piped door rim
// game size R .72, height .8, floor (where a cat loafs) .08, door a capsule of radius .34 at y .36
const HIDE_VARIANTS = [{ name: '민트', c: '#8fd6b1', d: '#5bb98c', pad: ['#fff4d6', '#ffd85a', '#f0b93a'] }, { name: '복숭아', c: '#ffb59f', d: '#e9785f', pad: ['#e3f6ec', '#8fd6b1', '#5bb98c'] }, { name: '라일락', c: '#c9b6f4', d: '#9a80e0', pad: ['#fff4d6', '#ffb3c4', '#ff7f9f'] }];
function hideout(v) {
  const g = new THREE.Group(), R = .72, Hh = .8, s = 2;
  const shellF = (x, y, z) => {
    const outer = sdEllipsoid(x, y - .02, z, R, Hh, R), inner = sdEllipsoid(x, y - .06, z, R - .06, Hh - .07, R - .06);
    const door = sdCapsule(x, y, z, [0, .36, 0], [0, .36, R + .2], .34), below = -y;
    return Math.max(outer, -inner, -door, below);
  };
  const tex2 = tex('igloo' + v.name, 512, 512, (gg, W, H, Rr) => {   // top half: the felt outside; bottom half: the cream lining
    gg.fillStyle = v.c; gg.fillRect(0, 0, W, H / 2); gg.fillStyle = '#fff4e6'; gg.fillRect(0, H / 2, W, H / 2);
    for (let i = 0; i < 3000; i++) { gg.fillStyle = Rr() < .5 ? 'rgba(0,0,0,.07)' : 'rgba(255,255,255,.2)'; gg.fillRect(Rr() * W, Rr() * H, 2, 1); }
    gg.fillStyle = 'rgba(255,255,255,.75)'; for (let k = 0; k < 6; k++) for (let y = 8; y < H / 2 - 30; y += 14) gg.fillRect(k * W / 6 - 1, y, 3, 8);   // stitched seams (meridians)
  });
  sdfPart(g, shellF, [-R, 0, -R, R, Hh + .03, R], .008, M('igloo' + v.name, () => std({ map: tex2, roughness: 1 })), (x, y, z, n) => {
    const outside = n[0] * x + n[2] * z + n[1] * (y - .02) * .6 > 0;
    return [Math.atan2(z, x) / (2 * Math.PI) + .5, (outside ? .5 : 0) + .5 * Math.min(1, y / Hh) * .98];
  });
  // the door rim: a round piping where the door cut meets the shell
  const rimF = (x, y, z) => {
    const outer = sdEllipsoid(x, y - .02, z, R, Hh, R), door = sdCapsule(x, y, z, [0, .36, 0], [0, .36, R + .2], .34);
    return Math.max(Math.hypot(outer, door) - .024, -y, -z);
  };
  sdfPart(g, rimF, [-.4, 0, 0, .4, .74, R + .04], .006, M('rim' + v.name, () => std({ color: v.d, roughness: .8 })), boxUV(4));
  // two little ears on the roof (rounded cones), tipped a little outward
  for (const sd of [-1, 1]) {
    const prof = [[0, 0], [.07, 0], [.068, .02], [.04, .08], [.012, .118], [0, .124]].map(([r, y]) => new THREE.Vector2(r, y));
    const e = lathe(g, prof, 24, M('igloo' + v.name, () => std({ map: tex2, roughness: 1 })));
    e.position.set(sd * .26, Hh - .09, -.05); e.rotation.z = -sd * .45; e.scale.set(1, 1, .55);
  }
  // the floor pad (check fabric): its top is the hideout floor, .08
  const pad = cyl(g, M('hpad' + v.name, () => std({ map: gingham(...v.pad), roughness: .95 })), R - .1, R - .1, .06, 0, .05, 0, 64);
  { const p = pad.geometry.attributes.position, uv = pad.geometry.attributes.uv; for (let i = 0; i < p.count; i++) uv.setXY(i, p.getX(i) * 1.2 + .5, p.getZ(i) * 1.2 + .5); }
  return g;
}
// litter box: a rounded tub with a lid seam, sand grains and a scoop hung on the rim; game size .84 x 1.04 x .28, sand at .15
const LITTER_VARIANTS = [{ name: '딸기', c: '#ff9fbd', d: '#ff7f9f' }, { name: '민트', c: '#8fd6b1', d: '#5bb98c' }];
function litterBox(v) {
  const g = new THREE.Group(), W = .84, L = 1.04, H = .28, t = .03;
  const tubF = (x, y, z) => Math.max(sdRoundBox(x, y - H / 2, z, W / 2, H / 2, L / 2, .06), -sdRoundBox(x, y - H / 2 - t, z, W / 2 - t, H / 2, L / 2 - t, .05));
  sdfPart(g, tubF, [-W / 2, 0, -L / 2, W / 2, H, L / 2], .008, M('tub' + v.name, () => std({ map: felt(v.c, 'rgba(255,255,255,.12)', 1, 1), roughness: .5 })), boxUV(1));
  // the lid seam: a band round the tub a little below the rim
  const seamF = (x, y, z) => Math.max(Math.abs(sdRoundBox(x, 0, z, W / 2 + .004, 1, L / 2 + .004, .064)) - .008, Math.abs(y - (H - .05)) - .012);
  sdfPart(g, seamF, [-W / 2 - .02, H - .07, -L / 2 - .02, W / 2 + .02, H - .03, L / 2 + .02], .005, M('seam' + v.name, () => std({ color: v.d, roughness: .5 })), boxUV(4));
  const sandT = tex('litter', 256, 256, (gg, Wd, Ht, Rr) => { gg.fillStyle = '#efe6d2'; gg.fillRect(0, 0, Wd, Ht); for (let i = 0; i < 900; i++) { gg.fillStyle = Rr() < .5 ? '#d8cbb0' : '#fffaf0'; gg.beginPath(); gg.arc(Rr() * Wd, Rr() * Ht, 1.5 + Rr() * 2, 0, 7); gg.fill(); } }, 3, 3);
  box(g, M('sand', () => std({ map: sandT, roughness: 1 })), W - 2 * t - .004, .15 - .02, L - 2 * t - .004, 0, (.15 - .02) / 2 + .02, 0, .04);
  const R3 = rng(9); for (let i = 0; i < 60; i++) sph(g, M('grain', () => std({ color: '#d8cbb0' })), .006 + R3() * .004, (R3() - .5) * (W - .2), .15, (R3() - .5) * (L - .2), 1, .7, 1, 8, 'detail');
  // the scoop, hung over the right rim: a handle and a slotted blade
  const scoopM = M('scoop' + v.name, () => std({ color: v.d, roughness: .5 }));
  box(g, scoopM, .03, .2, .05, W / 2 + .03, H - .04, .2, .012);
  box(g, scoopM, .012, .05, .05, W / 2 - .01, H + .01, .2, .005);
  box(g, scoopM, .14, .012, .16, W / 2 - .09, H - .03, .2, .006).rotation.z = .5;
  return g;
}

// ---------------------------------------------------------------- the catalogue
// anchors: the places a cat uses, kept exactly as items.js (the clips were fitted to them)
// helpers for the second catalogue file (itemkit2.js)
export { part, box, cyl, sph, torus, lathe, capsule, chain, M, sdfPart, boxUV, pompom, screwM, fishShape, sdRoundBox, sdCapsule };
import { EXTRA_ITEMS } from './itemkit2.js';

export const KIT_ITEMS = {
  food_bowl: { ko: '사료 그릇', variants: BOWL_VARIANTS.food_bowl.length, build: v => dish('food_bowl', BOWL_VARIANTS.food_bowl[v]) },
  water_bowl: { ko: '물그릇', variants: BOWL_VARIANTS.water_bowl.length, gloss: .35, build: v => dish('water_bowl', BOWL_VARIANTS.water_bowl[v]) },
  milk_bowl: { ko: '우유 그릇', variants: BOWL_VARIANTS.milk_bowl.length, gloss: .35, build: v => dish('milk_bowl', BOWL_VARIANTS.milk_bowl[v]) },
  can_food: { ko: '고양이 캔', variants: CAN_VARIANTS.length, build: v => canFood(CAN_VARIANTS[v]) },
  churu: { ko: '츄르', variants: CHURU_VARIANTS.length, build: v => churu(CHURU_VARIANTS[v]) },
  ball: { ko: '공', variants: BALL_VARIANTS.length, build: v => ball(BALL_VARIANTS[v]) },
  mouse_toy: { ko: '쥐돌이', variants: MOUSE_VARIANTS.length, build: v => mouseToy(MOUSE_VARIANTS[v]) },
  wand_toy: { ko: '낚싯대 장난감', variants: WAND_VARIANTS.length, build: v => wandToy(WAND_VARIANTS[v]) },
  scratcher: { ko: '스크래처', variants: SCRATCH_VARIANTS.length, build: v => scratcher(SCRATCH_VARIANTS[v]) },
  cushion: { ko: '방석', variants: CUSHION_VARIANTS.length, build: (v, o) => cushion(CUSHION_VARIANTS[v], o.press) },
  hideout: { ko: '숨숨집', variants: HIDE_VARIANTS.length, build: v => hideout(HIDE_VARIANTS[v]) },
  litter_box: { ko: '화장실', variants: LITTER_VARIANTS.length, build: v => litterBox(LITTER_VARIANTS[v]) },
  cat_tower_1: { ko: '캣타워 1단', variants: 2, build: v => tower1(v) },
  ...EXTRA_ITEMS,
  ...Object.fromEntries(Object.entries(TOWERS).map(([k, t]) => ['tower_' + k, { ko: '캣타워 ' + t.ko, variants: TOWER_VARIANTS.length, tower: true, build: v => buildTower(k, v),
    anchors: { step: STEP, top: t.top, decks: t.decks.map(d => ({ y: d.y, x: d.at[0], z: d.at[1], size: d.size, ...(d.round ? { round: true } : {}), ...(d.leaf ? { leaf: true } : {}) })) } }])),
};
// ---------------------------------------------------------------- season items (limited): the same builders in season colours + a small mark
function recolourLeaves(g, colour, dots = null) {
  const m = std({ map: felt(colour, 'rgba(0,0,0,.08)'), roughness: .95 });
  g.traverse(o => { if (o.isMesh && o.material && o.material.map && o.material.map.name && o.material.map.name.startsWith('felt#66b84a')) o.material = m; });
  if (dots) g.traverse(o => { if (o.name === 'LeafPad') for (let k = 0; k < 5; k++) { const d = sph(o.parent, M('dot' + dots, () => std({ color: dots, roughness: .9 })), .04, Math.cos(k * 1.3) * .2, .005, Math.sin(k * 1.3) * .2, 1, .4, 1, 10); } });
  return g;
}
const SEASON_ITEMS = {
  petal_cushion: { ko: '꽃잎 방석', variants: 1, build: () => { const g = cushion({ name: '벚꽃', check: ['#ffeef3', '#ffc2d1', '#ff9fbd'], roll: '#ffc2d1', pipe: '#fffaf0', button: '#ff7f9f' });
    for (let k = 0; k < 6; k++) { const a = k / 6 * Math.PI * 2; const p = sph(g, M('petal', () => std({ color: '#ffb3c4', roughness: .8 })), .05, Math.cos(a) * .3, .12, Math.sin(a) * .3, 1, .15, .6, 10); p.rotation.y = -a; } return g; } },
  melon_cushion: { ko: '수박 방석', variants: 1, build: () => { const g = cushion({ name: '수박', check: ['#ff8d8d', '#ff6b6b', '#e9585a'], roll: '#58a043', pipe: '#fffaf0', button: '#3b2a22' }); return g; } },
  pumpkin_house: { ko: '호박 숨숨집', variants: 1, build: () => { const g = hideout({ name: '호박', c: '#ff9f4a', d: '#e9785f', pad: ['#fff4d6', '#ffd85a', '#f0b93a'] });
    cyl(g, M('stem', () => std({ color: '#58a043', roughness: .9 })), .04, .06, .12, 0, .85, 0, 10); return g; } },
  hanok_hideout: { ko: '한옥 숨숨집', variants: 1, build: () => { const g = hideout({ name: '한옥', c: '#e2b483', d: '#8b5e3c', pad: ['#f6f1e3', '#a6e3c4', '#5bb98c'] });
    const roof = lathe(g, [new THREE.Vector2(0, .95), new THREE.Vector2(.82, .62), new THREE.Vector2(.86, .66), new THREE.Vector2(0, 1.0)], 8, M('giwa', () => std({ map: tex('giwa', 256, 64, (gg, w, h) => { gg.fillStyle = '#8a96a0'; gg.fillRect(0, 0, w, h); gg.fillStyle = '#a3aeb6'; for (let x = 0; x < w; x += 16) gg.fillRect(x, 0, 8, h); }), roughness: .8 }))); roof.rotation.y = Math.PI / 8; return g; } },
  sakura_tower: { ko: '벚꽃 캣타워', variants: 1, tower: true, anchors: KIT_ITEMS.tower_tree.anchors, build: () => recolourLeaves(buildTower('tree', 0), '#ffc2d1', '#ffffff') },
  snow_tree: { ko: '눈 트리', variants: 1, build: () => recolourLeaves(buildTower('tree', 0), '#3f9a3e', '#fffaf0') },
};
Object.assign(KIT_ITEMS, SEASON_ITEMS);
export const KIT_IDS = Object.keys(KIT_ITEMS);
export function buildKitItem(id, { variant = 0, press = null } = {}) {
  const d = KIT_ITEMS[id], g = d.build(Math.min(variant, d.variants - 1), { press });
  g.name = id; g.userData = { id, ko: d.ko, variants: d.variants, gloss: d.gloss || 0, ...(d.anchors ? { anchors: d.anchors } : {}) };
  g.updateMatrixWorld(true);
  return g;
}

// ---------------------------------------------------------------- the exact shape, for QA (node)
// kitField(id) -> { d(x, y, z) metres (negative inside), anchors, bounds }: the union of the game parts' own
// distance functions (role 'both' / 'proxy'; baked-on detail is not geometry). Parts without one (the
// hammock, the churu crimp, pompoms) are taken as their bounding box (a little more than the part: safe).
export function kitField(id) {
  const g = buildKitItem(id); const parts = [], tmpP = new THREE.Vector3(), q = new THREE.Quaternion(), sc = new THREE.Vector3();
  g.traverse(o => {
    if (!o.isMesh || o.userData.role === 'detail') return;
    o.matrixWorld.decompose(tmpP, q, sc);
    const pos = tmpP.clone(), inv = q.clone().invert(), s = [sc.x, sc.y, sc.z];
    let f = o.userData.sdf;
    if (!f) { o.geometry.computeBoundingBox(); const b = o.geometry.boundingBox, c = b.getCenter(new THREE.Vector3()), h = b.getSize(new THREE.Vector3()).multiplyScalar(.5);
      f = (X, Y, Z, s2) => sdRoundBox(X - c.x * s2[0], Y - c.y * s2[1], Z - c.z * s2[2], h.x * s2[0], h.y * s2[1], h.z * s2[2], 0); }
    const wb = new THREE.Box3().setFromObject(o, true);
    parts.push({ pos, inv, s, f, wb: wb.isEmpty() ? null : [wb.min.x, wb.min.y, wb.min.z, wb.max.x, wb.max.y, wb.max.z] });
  });
  const v = new THREE.Vector3();
  const d = (x, y, z) => { let best = 1e9;
    for (const P of parts) {
      if (P.wb) { const bd = Math.hypot(Math.max(P.wb[0] - x, 0, x - P.wb[3]), Math.max(P.wb[1] - y, 0, y - P.wb[4]), Math.max(P.wb[2] - z, 0, z - P.wb[5])); if (bd > .05 && bd > best) continue; if (bd > .05) { best = Math.min(best, bd); continue; } }
      v.set(x - P.pos.x, y - P.pos.y, z - P.pos.z).applyQuaternion(P.inv); best = Math.min(best, P.f(v.x, v.y, v.z, P.s)); }
    return best; };
  const bb = new THREE.Box3().setFromObject(g, true);
  return { d, anchors: g.userData.anchors || null, bounds: [bb.min.x, bb.min.y, bb.min.z, bb.max.x, bb.max.y, bb.max.z], parts: parts.length };
}
