// Cat Island items: food, toys and furniture for the cats, in the same soft toy style.
//
//   buildItem(id) -> { id, ko, pos, idx, nrm, col, anchors, tris }      (metres, Y up, +Z = the item's front)
//
// Each item is sculpted the way the cats are (sdfmesh.js): rounded primitives combined with unions, smooth
// unions and carving, then surface nets + relaxing -> ONE closed, watertight mesh per item. Closed meshes
// with outward normals are what real-time shadows need: no light leaks through seams, no missing back
// faces, no flickering self-shadow on overlapping shells.
// Colours are painted per vertex from the primitive nearest each surface point (patterns: kibble, sisal
// rope, cardboard flutes, stripes); blender_items.py bakes them (and ambient occlusion) into a texture.
// `anchors` are the places a cat uses: the food surface its tongue reaches, the deck tops it jumps onto,
// the cushion top it lies on. Sizes are set against the biggest breed (Maine Coon) so every cat fits.
import { sdEllipsoid, surfaceNets, relax } from './sdfmesh.js';

// ---- distance functions (Inigo Quilez)
const len2 = (x, y) => Math.hypot(x, y), len3 = (x, y, z) => Math.hypot(x, y, z);
const sdRoundBox = (x, y, z, bx, by, bz, r) => {
  const qx = Math.abs(x) - bx + r, qy = Math.abs(y) - by + r, qz = Math.abs(z) - bz + r;
  return len3(Math.max(qx, 0), Math.max(qy, 0), Math.max(qz, 0)) + Math.min(Math.max(qx, qy, qz), 0) - r;
};
const sdCylY = (x, y, z, r, h, rr) => {                       // vertical rounded cylinder, centred, half height h
  const dx = len2(x, z) - r + rr, dy = Math.abs(y) - h + rr;
  return Math.min(Math.max(dx, dy), 0) + len2(Math.max(dx, 0), Math.max(dy, 0)) - rr;
};
const sdConeCylY = (x, y, z, r0, r1, h, rr) => {               // rounded cylinder whose radius goes r0 (bottom) -> r1 (top)
  const t = Math.min(1, Math.max(0, (y + h) / (2 * h))), r = r0 + (r1 - r0) * t;
  const dx = (len2(x, z) - r + rr) / Math.hypot(1, (r1 - r0) / (2 * h)), dy = Math.abs(y) - h + rr;
  return Math.min(Math.max(dx, dy), 0) + len2(Math.max(dx, 0), Math.max(dy, 0)) - rr;
};
const sdTorusY = (x, y, z, R, r) => len2(len2(x, z) - R, y) - r;
const sdCapsule = (x, y, z, a, b, r) => {
  const bx = b[0] - a[0], by = b[1] - a[1], bz = b[2] - a[2], px = x - a[0], py = y - a[1], pz = z - a[2];
  const h = Math.min(1, Math.max(0, (px * bx + py * by + pz * bz) / (bx * bx + by * by + bz * bz)));
  return len3(px - bx * h, py - by * h, pz - bz * h) - r;
};
const smin = (a, b, k) => { if (k <= 0) return Math.min(a, b); const h = Math.max(k - Math.abs(a - b), 0) / k; return Math.min(a, b) - h * h * k * .25; };
const smax = (a, b, k) => -smin(-a, -b, k);

// ---- a small sculpting tree: leaves (shape + colour) combined by union / smooth union / carve
// Leaves outside their box (grown by a margin) answer with the distance to the box: a lower bound that is
// exact enough away from the surface and lets a hundred kibble pieces cost nothing elsewhere.
const MARGIN = .03;
const boxDist = (b, x, y, z) => len3(Math.max(b[0] - x, 0, x - b[3]), Math.max(b[1] - y, 0, y - b[4]), Math.max(b[2] - z, 0, z - b[5]));
function leaf(d, box, color) { return { kind: 'leaf', d, box, color }; }
const U = (...c) => ({ kind: 'u', c, k: 0 });
const SU = (k, ...c) => ({ kind: 'u', c, k });
const CUT = (a, b, k = 0, color = null) => ({ kind: 'cut', a, b, k, color });     // a with b carved out (cut surface coloured `color`)
function evalNode(n, x, y, z) {
  if (n.kind === 'leaf') { const bd = boxDist(n.box, x, y, z); return bd > MARGIN ? bd : n.d(x, y, z); }
  if (n.kind === 'u') { let d = 1e9; for (const c of n.c) { const e = evalNode(c, x, y, z); d = d === 1e9 ? e : n.k ? smin(d, e, n.k) : Math.min(d, e); } return d; }
  const a = evalNode(n.a, x, y, z), b = evalNode(n.b, x, y, z);
  return n.k ? smax(a, -b, n.k) : Math.max(a, -b);
}
// colour at a surface point: every leaf near the surface contributes, weighted by how close its own surface is
// (a soft blend over ~1.5 mm, so where two parts meet the colours fade instead of zig-zagging along the
// triangles); a carved face takes the cut's colour
function leavesNear(n, x, y, z, out, cutColor = null) {
  if (n.kind === 'leaf') { if (boxDist(n.box, x, y, z) < .01) out.push({ d: Math.abs(n.d(x, y, z)), c: cutColor || (typeof n.color === 'function' ? n.color(x, y, z) : n.color) }); return out; }
  if (n.kind === 'u') { for (const c of n.c) leavesNear(c, x, y, z, out, cutColor); return out; }
  leavesNear(n.a, x, y, z, out, cutColor);
  const db = Math.abs(evalNode(n.b, x, y, z));
  if (db < .01) { const sub = leavesNear(n.b, x, y, z, []); for (const e of sub) out.push({ d: db, c: n.color || e.c }); }
  return out;
}
function colorNode(n, x, y, z) {
  const L = leavesNear(n, x, y, z, []);
  if (!L.length) return { c: [1, 0, 1] };
  const dmin = Math.min(...L.map(e => e.d)), c = [0, 0, 0]; let ws = 0;
  for (const e of L) { const w = Math.exp(-(e.d - dmin) / .0015); ws += w; for (let k = 0; k < 3; k++) c[k] += w * e.c[k]; }
  return { c: c.map(v => v / ws) };
}
function boundsNode(n) {
  if (n.kind === 'leaf') return n.box.slice();
  if (n.kind === 'cut') return boundsNode(n.a);
  const b = [1e9, 1e9, 1e9, -1e9, -1e9, -1e9];
  for (const c of n.c) { const e = boundsNode(c); for (let i = 0; i < 3; i++) { b[i] = Math.min(b[i], e[i]); b[i + 3] = Math.max(b[i + 3], e[i + 3]); } }
  return b;
}

// ---- colours and patterns
const hex = h => { const v = parseInt(h.slice(1), 16); return [(v >> 16 & 255) / 255, (v >> 8 & 255) / 255, (v & 255) / 255]; };
const mixc = (a, b, t) => a.map((v, i) => v + (b[i] - v) * t);
const hash = (x, y, z) => { const s = Math.sin(x * 127.1 + y * 311.7 + z * 74.7) * 43758.5453; return s - Math.floor(s); };
const vnoise = (x, y, z) => {
  const ix = Math.floor(x), iy = Math.floor(y), iz = Math.floor(z), fx = x - ix, fy = y - iy, fz = z - iz;
  const s = t => t * t * (3 - 2 * t), u = s(fx), v = s(fy), w = s(fz);
  const L = (a, b, t) => a + (b - a) * t, h = (i, j, k) => hash(ix + i, iy + j, iz + k);
  return L(L(L(h(0, 0, 0), h(1, 0, 0), u), L(h(0, 1, 0), h(1, 1, 0), u), v), L(L(h(0, 0, 1), h(1, 0, 1), u), L(h(0, 1, 1), h(1, 1, 1), u), v), w);
};
const grain = (c, amt, f = 60) => (x, y, z) => mixc(c, c.map(v => v * .82), amt * vnoise(x * f, y * f, z * f));
// sisal rope wound round a vertical post: bands that spiral up
const sisal = (x, y, z) => { const a = Math.atan2(z, x), b = .5 + .5 * Math.sin(y * 260 + a * 2); return mixc(hex('#d8b98a'), hex('#a98758'), .55 * b * b + .15 * vnoise(x * 90, y * 90, z * 90)); };
// corrugated cardboard: flutes across the pad
const wood = (x, y, z) => mixc(hex('#e3c59b'), hex('#c49a6c'), .5 + .5 * Math.sin(z * 30 + 6 * vnoise(x * 8, y * 8, z * 8)));

// ---- the items (sizes in metres; a cat here stands ~.23-.30 m at the shoulder, its head ~.25 m wide)
export const BOWL_SURF = .03;             // food / water surface height: the Drink clip (also eating) reaches exactly here
export const TOY_TOP = .038;              // top of the mouse toy: PawBat's tap comes down on it
export const DECK_STEP = .2, DECK_HOP = .78;   // tower decks: one JumpUp clip up and forward (catmotion JumpUp: H, D)

const kibbleMound = (cx, cz, R, top, n, seed, colA, colB) => {
  // a heap of kibble: little rounded pieces packed under a dome of radius R topping out at `top`
  const out = [];
  for (let i = 0; i < n; i++) {
    const a = hash(i, seed, 1) * Math.PI * 2, r = R * Math.sqrt(hash(i, seed, 2)) * .9;
    const x = cx + r * Math.cos(a), z = cz + r * Math.sin(a), y = top - .012 - (top - .02) * (r / R) * (r / R) * .55 + .004 * hash(i, seed, 3);
    const s = .0075 + .003 * hash(i, seed, 4), c = hash(i, seed, 5) < .5 ? colA : colB;
    out.push(leaf((X, Y, Z) => sdEllipsoid(X - x, Y - y, Z - z, s, s * .72, s * .9), [x - s, y - s, z - s, x + s, y + s, z + s], grain(c, .5, 300)));
  }
  return out;
};

function bowl({ color, fill }) {
  // a wide, shallow dish (a saucer more than a bowl): the toy's big head comes down level with the food, so
  // the rim has to stay below its chin - a deep bowl's rim would cut into the chin while it eats or drinks
  const R = .1, H = BOWL_SURF + .003, inner = .084;
  const body = leaf((x, y, z) => sdConeCylY(x, y - H / 2, z, .085, R, H / 2, .01), [-R, 0, -R, R, H, R], color);
  const hollow = leaf((x, y, z) => sdEllipsoid(x, y - (H + .004), z, inner, .022, inner), [-inner, H - .018, -inner, inner, H + .026, inner], color);
  const lip = leaf((x, y, z) => sdTorusY(x, y - (H - .002), z, R - .007, .007), [-R, H - .009, -R, R, H + .005, R], mixc(color, [1, 1, 1], .25));
  let shape = U(CUT(body, hollow, .005, mixc(color, [1, 1, 1], .55)), lip);
  if (fill === 'kibble') {
    // a bed of kibble under the loose pieces on top, so no gap between pieces shows the empty dish; the heap
    // crowns at BOWL_SURF: the same lapping that reaches the water reaches the kibble
    const bed = leaf((x, y, z) => sdEllipsoid(x, y - (BOWL_SURF - .016), z, .07, .014, .07), [-.07, BOWL_SURF - .03, -.07, .07, BOWL_SURF - .002, .07], grain(hex('#9a5a2c'), .6, 300));
    shape = SU(.003, shape, bed, ...kibbleMound(0, 0, .062, BOWL_SURF + .002, 80, 3, hex('#b06b35'), hex('#8a4f26')));
  }
  if (fill === 'water' || fill === 'milk') {
    const c = fill === 'water' ? hex('#8fd0ee') : hex('#fbf6ea');
    shape = U(shape, leaf((x, y, z) => sdCylY(x, y - (BOWL_SURF - .006), z, inner - .006, .006, .003), [-inner, BOWL_SURF - .012, -inner, inner, BOWL_SURF, inner], c));
  }
  return { shape, anchors: { surface: { y: BOWL_SURF, r: inner - .012 }, rimTop: H + .005, footprint: R } };
}

const ITEM_DEFS = {
  food_bowl: { ko: '사료 그릇', h: .0035, build: () => bowl({ color: hex('#f2a7a0'), fill: 'kibble' }) },
  water_bowl: { ko: '물그릇', h: .0035, build: () => bowl({ color: hex('#9cc7e8'), fill: 'water' }) },
  milk_bowl: { ko: '우유 그릇', h: .0035, build: () => bowl({ color: hex('#f6d77a'), fill: 'milk' }) },
  can_food: {
    ko: '고양이 캔', h: .003, build: () => {
      const r = .042, h = .036;
      const can = leaf((x, y, z) => sdCylY(x, y - h / 2, z, r, h / 2, .004), [-r, 0, -r, r, h, r], (x, y, z) => y > .006 && y < h - .006 ? (Math.abs(Math.atan2(z, x)) < 1.2 ? hex('#f5e9c8') : hex('#e2584e')) : hex('#c9ced6'));
      const top = leaf((x, y, z) => sdCylY(x, y - (h + .004), z, r - .004, .006, .003), [-r, h - .003, -r, r, h + .011, r], hex('#c9ced6'));
      const pate = leaf((x, y, z) => sdEllipsoid(x, y - (h + .002), z, r - .009, .012, r - .009), [-r, h - .01, -r, r, h + .014, r], grain(hex('#c97b5a'), .6, 200));
      const tab = leaf((x, y, z) => sdTorusY(x - .02, (y - (h + .005)) * 3, z, .008, .006) / 3, [.008, h + .002, -.016, .034, h + .008, .016], hex('#b7bcc6'));
      return { shape: U(SU(.006, CUT(can, top, .003, hex('#b7bcc6')), pate), tab), anchors: { surface: { y: h + .012, r: r - .01 } } };   // (pâté melts into the rim: no hairline crevice)
    },
  },
  churu: {
    ko: '츄르', h: .0025, build: () => {
      // the squeeze tube treat: a flat pouch with a crimped top, lying on its side
      const L = .13, w = .016, t = .007;
      const pouch = leaf((x, y, z) => sdRoundBox(x, y - t, z, w, t, L / 2, .006), [-w, 0, -L / 2, w, 2 * t, L / 2],
        (x, y, z) => z > L / 2 - .02 ? hex('#ffffff') : Math.abs(z) < .02 ? hex('#ffd35c') : hex('#ff8a3d'));
      const crimp = leaf((x, y, z) => sdRoundBox(x, y - t * .8, z - (L / 2 + .006), w * 1.05, t * .45, .008, .002), [-w, 0, L / 2 - .004, w, 2 * t, L / 2 + .016], (x, y, z) => mixc(hex('#ffffff'), hex('#dddddd'), .5 + .5 * Math.sin(x * 900)));
      return { shape: U(pouch, crimp), anchors: { tip: [0, t, L / 2 + .014] } };
    },
  },
  ball: {
    ko: '공', h: .0025, build: () => {
      const r = .035;
      const ball = leaf((x, y, z) => len3(x, y - r, z) - r, [-r, 0, -r, r, 2 * r, r],
        (x, y, z) => { const ss = (a, b, v) => { const t = Math.min(1, Math.max(0, (v - a) / (b - a))); return t * t * (3 - 2 * t); }; const band = 1 - ss(.005, .008, Math.abs(y - r)), stripe = 1 - ss(.3, .4, Math.abs(Math.sin(Math.atan2(z, x) * 3))); return mixc(mixc(hex('#ff6b7d'), hex('#ffd34d'), band), hex('#ffffff'), stripe); });
      return { shape: ball, anchors: { center: [0, r, 0], r } };
    },
  },
  mouse_toy: {
    ko: '쥐돌이', h: .0018, build: () => {
      const body = leaf((x, y, z) => sdEllipsoid(x, y - .019, z, .022, .019, .036), [-.022, 0, -.036, .022, .038, .036], grain(hex('#b9b3ad'), .7, 220));
      const head = leaf((x, y, z) => sdEllipsoid(x, y - .018, z - .034, .014, .013, .02), [-.014, .005, .014, .014, .031, .054], grain(hex('#b9b3ad'), .7, 220));
      const ears = [-1, 1].map(s => leaf((x, y, z) => sdEllipsoid(x - s * .013, y - .034, z - .03, .01, .01, .004), [s * .013 - .01, .024, .026, s * .013 + .01, .044, .034], hex('#f3a8b6')));
      const nose = leaf((x, y, z) => len3(x, y - .017, z - .055) - .004, [-.004, .013, .051, .004, .021, .059], hex('#f07d95'));
      const tail = [];
      for (let i = 0; i < 6; i++) { const a = [Math.sin(i * .6) * .012, .006, -.034 - i * .012], b = [Math.sin((i + 1) * .6) * .012, .005, -.046 - i * .012]; tail.push(leaf((x, y, z) => sdCapsule(x, y, z, a, b, .0032), [Math.min(a[0], b[0]) - .004, 0, b[2] - .004, Math.max(a[0], b[0]) + .004, .01, a[2] + .004], hex('#f3a8b6'))); }
      return { shape: SU(.006, body, head, nose, ...ears, ...tail), anchors: { center: [0, .02, 0] } };
    },
  },
  wand_toy: {
    ko: '낚싯대 장난감', h: .002, build: () => {
      // lying on the floor: stick, string and a fish lure with a feather tail
      const stick = leaf((x, y, z) => sdCapsule(x, y, z, [0, .007, -.25], [0, .007, .12], .007), [-.008, 0, -.26, .008, .015, .13], (x, y, z) => Math.sin(z * 60) > .6 ? hex('#f28ab0') : hex('#fde1a8'));
      const pts = Array.from({ length: 9 }, (_, i) => [.06 * Math.sin(i * .5), .0032, .12 + i * .022]);
      const string = pts.slice(1).map((b, i) => { const a = pts[i]; return leaf((x, y, z) => sdCapsule(x, y, z, a, b, .003), [Math.min(a[0], b[0]) - .003, 0, a[2] - .003, Math.max(a[0], b[0]) + .003, .006, b[2] + .003], hex('#ffffff')); });
      const fz = .33, fx = .06 * Math.sin(8 * .5);
      const fish = leaf((x, y, z) => sdEllipsoid(x - fx, y - .012, z - fz, .013, .011, .03), [fx - .014, 0, fz - .031, fx + .014, .024, fz + .031], (x, y, z) => z > fz + .012 ? hex('#ffd34d') : hex('#5bb3e8'));
      const fin = leaf((x, y, z) => sdEllipsoid(x - fx, y - .012, z - (fz + .036), .004, .014, .01), [fx - .005, 0, fz + .025, fx + .005, .027, fz + .047], hex('#ffd34d'));
      const feathers = [-1, 0, 1].map(s => leaf((x, y, z) => sdEllipsoid(x - fx - s * .008, y - .007, z - (fz - .045), .006, .004, .022), [fx + s * .008 - .007, 0, fz - .068, fx + s * .008 + .007, .012, fz - .022], s ? hex('#ff7f9f') : hex('#b48cf2')));
      return { shape: SU(.002, stick, ...string, SU(.006, fish, fin, ...feathers)), anchors: { lure: [fx, .012, fz] } };
    },
  },
  scratcher: {
    ko: '스크래처', h: .004, build: () => {
      // a flat corrugated cardboard pad in a low frame: the front paws dig into it in the Stretch clip
      const W = .26, L = .5, H = .045;
      const frame = leaf((x, y, z) => sdRoundBox(x, y - H / 2, z, W / 2, H / 2, L / 2, .012), [-W / 2, 0, -L / 2, W / 2, H, L / 2], grain(hex('#e8c48c'), .4, 40));
      const pad = leaf((x, y, z) => sdRoundBox(x, y - H, z, W / 2 - .018, .012, L / 2 - .018, .004), [-W / 2, H - .012, -L / 2, W / 2, H + .012, L / 2], (x, y, z) => mixc(hex('#c9a173'), hex('#9d7448'), .5 + .5 * Math.sin(z * 380)));
      return { shape: U(CUT(frame, pad, 0, hex('#9d7448')), leaf((x, y, z) => sdRoundBox(x, y - (H - .006), z, W / 2 - .02, .004, L / 2 - .02, .002), [-W / 2, H - .012, -L / 2, W / 2, H, L / 2], (x, y, z) => mixc(hex('#c9a173'), hex('#9d7448'), .5 + .5 * Math.sin(z * 380)))), anchors: { top: H - .002, size: [W, L] } };
    },
  },
  cushion: {
    ko: '방석', h: .005, build: () => {
      // a round puffy cushion with a raised rim: big enough for the biggest breed to loaf or sleep on
      const R = .34, T = .045;
      const seat = leaf((x, y, z) => sdEllipsoid(x, y - T, z, R - .05, T, R - .05), [-R, 0, -R, R, 2 * T, R], (x, y, z) => mixc(hex('#ffe6a8'), hex('#f9cf7a'), .5 + .5 * Math.sin(len2(x, z) * 90)));
      const rim = leaf((x, y, z) => sdTorusY(x, (y - .055) * 1.25, z, R - .055, .052) / 1.25, [-R, 0, -R, R, .12, R], hex('#f39a8b'));
      return { shape: SU(.03, seat, rim), anchors: { top: 2 * T - .006, r: R - .1 } };
    },
  },
  hideout: {
    ko: '숨숨집', h: .006, build: () => {
      // a felt igloo with a round door: room for a Maine Coon loafing inside
      const R = .36, Hh = .4;
      const shell = leaf((x, y, z) => sdEllipsoid(x, y - .01, z, R, Hh, R), [-R, 0, -R, R, Hh + .01, R], grain(hex('#a9c7a2'), .5, 50));
      const inside = leaf((x, y, z) => sdEllipsoid(x, y - .03, z, R - .03, Hh - .035, R - .03), [-R, .02, -R, R, Hh, R], hex('#f6efe2'));
      const door = leaf((x, y, z) => sdCapsule(x, y, z, [0, .21, 0], [0, .21, R + .1], .16), [-.17, .04, 0, .17, .38, R + .1], hex('#f6efe2'));
      const pad = leaf((x, y, z) => sdCylY(x, y - .02, z, R - .04, .02, .015), [-R, 0, -R, R, .04, R], hex('#ffe6a8'));
      const shellCut = CUT(CUT(shell, inside, .012, hex('#f6efe2')), door, .02, hex('#cfe0c9'));
      const below = leaf((x, y, z) => y, [-R - .05, -1, -R - .05, R + .05, 0, R + .05], hex('#7f9c7a'));      // (everything under the floor is cut off)
      return { shape: U(CUT(shellCut, below, 0, hex('#7f9c7a')), pad), anchors: { floor: .04, door: [0, .05, R], r: R - .06 } };
    },
  },
  litter_box: {
    ko: '화장실', h: .005, build: () => {
      const W = .42, L = .52, H = .14;
      const box = leaf((x, y, z) => sdRoundBox(x, y - H / 2, z, W / 2, H / 2, L / 2, .03), [-W / 2, 0, -L / 2, W / 2, H, L / 2], hex('#f4a6bd'));
      const tub = leaf((x, y, z) => sdRoundBox(x, y - (H + .02), z, W / 2 - .02, H - .03, L / 2 - .02, .03), [-W / 2, .02, -L / 2, W / 2, 2 * H, L / 2], hex('#ffd6e3'));
      const sand = leaf((x, y, z) => sdRoundBox(x, y - .045, z, W / 2 - .025, .03, L / 2 - .025, .02) - .002 * vnoise(x * 200, z * 200, 1), [-W / 2, .01, -L / 2, W / 2, .08, L / 2], (x, y, z) => mixc(hex('#e9e1d2'), hex('#c8bba4'), vnoise(x * 260, y * 260, z * 260)));
      return { shape: U(CUT(box, tub, .01, hex('#ffd6e3')), sand), anchors: { floor: .075, inner: [W - .05, L - .05] } };
    },
  },
};

// cat towers: decks one JumpUp apart (DECK_STEP up, DECK_HOP forward), each held by sisal posts, on a base
function tower(levels) {
  const DW = .5, DL = .62, DT = .035, parts = [];
  const base = leaf((x, y, z) => sdRoundBox(x, y - .015, z - (levels - 1) * DECK_HOP / 2, DW / 2 + .04, .015, (levels - 1) * DECK_HOP / 2 + DL / 2 + .04, .012),
    [-DW / 2 - .04, 0, -DL / 2 - .04, DW / 2 + .04, .03, (levels - 1) * DECK_HOP + DL / 2 + .04], wood);
  parts.push(base);
  const decks = [];
  for (let k = 0; k < levels; k++) {
    const y = DECK_STEP * (k + 1), z = k * DECK_HOP;
    decks.push({ y, z, size: [DW, DL] });
    // deck: a wooden board with a soft carpet top
    parts.push(leaf((X, Y, Z) => sdRoundBox(X, Y - (y - DT / 2), Z - z, DW / 2, DT / 2, DL / 2, .012), [-DW / 2, y - DT, z - DL / 2, DW / 2, y, z + DL / 2],
      (X, Y, Z) => Y > y - .006 ? mixc(hex('#c7e3f2'), hex('#a8cfe6'), vnoise(X * 120, Y * 120, Z * 120)) : wood(X, Y, Z)));
    // posts under the deck, from the base (two at the back corners, one in front)
    for (const [px, pz] of [[-DW / 2 + .07, -DL / 2 + .07], [DW / 2 - .07, -DL / 2 + .07], [0, DL / 2 - .08]]) {
      const x0 = px, z0 = z + pz, h = (y - DT) / 2;
      parts.push(leaf((X, Y, Z) => sdCylY(X - x0, Y - h, Z - z0, .042, h, .004), [x0 - .043, 0, z0 - .043, x0 + .043, y - DT, z0 + .043], (X, Y, Z) => sisal(X - x0, Y, Z - z0)));
    }
  }
  // the top deck gets a raised rim like a bed
  const t = decks[levels - 1];
  const rimOut = leaf((X, Y, Z) => sdRoundBox(X, Y - (t.y + .012), Z - t.z, DW / 2 - .005, .022, DL / 2 - .005, .02), [-DW / 2, t.y - .01, t.z - DL / 2, DW / 2, t.y + .034, t.z + DL / 2], hex('#f9cf7a'));
  const rimIn = leaf((X, Y, Z) => sdRoundBox(X, Y - (t.y + .04), Z - t.z, DW / 2 - .05, .04, DL / 2 - .05, .02), [-DW / 2, t.y, t.z - DL / 2, DW / 2, t.y + .08, t.z + DL / 2], hex('#f9cf7a'));
  parts.push(CUT(rimOut, rimIn, .012));
  // a dangling pompom toy from the edge of the top deck
  const pz = t.z + DL / 2 - .05, px = DW / 2 - .02;
  parts.push(leaf((X, Y, Z) => sdCapsule(X, Y, Z, [px, t.y - DT, pz], [px, t.y - .14, pz], .0025), [px - .004, t.y - .15, pz - .004, px + .004, t.y, pz + .004], hex('#ffffff')));
  parts.push(leaf((X, Y, Z) => len3(X - px, Y - (t.y - .16), Z - pz) - .022, [px - .023, t.y - .183, pz - .023, px + .023, t.y - .137, pz + .023], hex('#ff7f9f')));
  // where the cat starts each hop: JumpUp moves the root DECK_HOP forward and DECK_STEP up
  const hops = decks.map((d, k) => ({ from: { y: k ? decks[k - 1].y : 0, z: d.z - DECK_HOP }, to: { y: d.y, z: d.z } }));
  return { shape: SU(.01, ...parts), anchors: { decks, hops } };
}
ITEM_DEFS.cat_tower_1 = { ko: '캣타워 1단', h: .006, build: () => tower(1) };
ITEM_DEFS.cat_tower_2 = { ko: '캣타워 2단', h: .006, build: () => tower(2) };

export const ITEM_IDS = Object.keys(ITEM_DEFS);

export function buildItem(id, { fine = 1 } = {}) {        // fine < 1: a denser mesh (the raw for baking)
  const def = ITEM_DEFS[id], { shape, anchors } = def.build();
  const field = { eval: (x, y, z) => evalNode(shape, x, y, z), bounds: () => boundsNode(shape), grad: null };
  field.grad = (x, y, z, e = 1e-4) => [(field.eval(x + e, y, z) - field.eval(x - e, y, z)) / (2 * e), (field.eval(x, y + e, z) - field.eval(x, y - e, z)) / (2 * e), (field.eval(x, y, z + e) - field.eval(x, y, z - e)) / (2 * e)];
  const mesh = relax(field, surfaceNets(field, def.h * fine, .02), 3);
  const { pos, idx } = mesh, n = pos.length / 3;
  // nothing below the floor (y = 0): the smoothing can pull a bottom edge a hair under
  for (let i = 0; i < n; i++) if (pos[3 * i + 1] < 0) pos[3 * i + 1] = 0;
  const nrm = new Float32Array(pos.length), col = new Float32Array(pos.length);
  for (let i = 0; i < n; i++) {
    const g = field.grad(pos[3 * i], pos[3 * i + 1], pos[3 * i + 2]), l = Math.hypot(...g) || 1;
    nrm[3 * i] = g[0] / l; nrm[3 * i + 1] = g[1] / l; nrm[3 * i + 2] = g[2] / l;
    const c = colorNode(shape, pos[3 * i], pos[3 * i + 1], pos[3 * i + 2]).c;
    col.set(c, 3 * i);
  }
  return { id, ko: def.ko, pos, idx, nrm, col, anchors, tris: idx.length / 3 };
}

// mesh checks: closed (every edge shared by exactly two triangles) and outward facing (signed volume > 0)
export function checkMesh(m) {
  const edges = new Map(), { idx, pos } = m;
  for (let t = 0; t < idx.length; t += 3) for (let e = 0; e < 3; e++) {
    const a = idx[t + e], b = idx[t + (e + 1) % 3], k = a < b ? a + ',' + b : b + ',' + a;
    edges.set(k, (edges.get(k) || 0) + 1);
  }
  let open = 0, nonManifold = 0;
  for (const c of edges.values()) { if (c === 1) open++; else if (c > 2) nonManifold++; }
  let vol = 0;
  for (let t = 0; t < idx.length; t += 3) {
    const a = 3 * idx[t], b = 3 * idx[t + 1], c = 3 * idx[t + 2];
    vol += (pos[a] * (pos[b + 1] * pos[c + 2] - pos[b + 2] * pos[c + 1]) - pos[a + 1] * (pos[b] * pos[c + 2] - pos[b + 2] * pos[c]) + pos[a + 2] * (pos[b] * pos[c + 1] - pos[b + 1] * pos[c])) / 6;
  }
  let minY = Infinity; for (let i = 1; i < pos.length; i += 3) minY = Math.min(minY, pos[i]);
  return { open, nonManifold, volume: vol, minY };
}

// the item's exact signed distance (metres, negative inside) and anchors: for checking a cat against it
export function itemField(id) {
  const { shape, anchors } = ITEM_DEFS[id].build();
  return { d: (x, y, z) => evalNode(shape, x, y, z), anchors, bounds: boundsNode(shape) };
}
