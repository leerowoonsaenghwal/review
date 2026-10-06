// Cat Island items, second catalogue (docs/ART_DIRECTION.md 5장, GAME_PLAN 60 items): same parts kit and painted
// textures as itemkit.js. Each item fits its grid footprint (one cell = 0.6 m; Catalog.cs w x h) and every part is
// one closed mesh. Colour variants: 2 each. Sizes are in game units (a cat is about 1 m long).
import * as THREE from 'three';
import { part, box, cyl, sph, torus, lathe, capsule, chain, M, std, felt, woodTex, gingham, carpetTex, sisalTex, tex, fishShape } from './itemkit.js';

const V2 = (x, y) => new THREE.Vector2(x, y);
const wood = (k = 'a') => M('w2' + k, () => std({ map: k === 'a' ? woodTex('#c98f58', '#a06a3a', '#e0b07a') : woodTex('#d29a62', '#a8703f', '#e8bd87'), roughness: .7 }));
const flat = (c, r = .8) => M('c2' + c + r, () => std({ color: c, roughness: r }));
const feltM = (c, c2 = 'rgba(0,0,0,.07)') => M('f2' + c, () => std({ map: felt(c, c2), roughness: 1 }));
// wicker: woven bands (baskets)
const wicker = (c1, c2) => M('wk' + c1, () => std({ map: tex('wicker' + c1, 256, 256, (g, w, h) => {
  g.fillStyle = c1; g.fillRect(0, 0, w, h);
  for (let y = 0; y < h; y += 16) for (let x = 0; x < w; x += 32) { g.fillStyle = c2; g.fillRect(x + ((y / 16) % 2) * 16, y, 16, 14); }
  g.fillStyle = 'rgba(80,50,20,.25)'; for (let y = 0; y < h; y += 16) g.fillRect(0, y + 14, w, 2);
}, 4, 2), roughness: .9 }));
// glaze with a band and dots (ceramics)
const glazeM = (body, band, dot) => M('gz' + body + band, () => std({ map: tex('gz' + body + band, 512, 128, (g, w, h) => {
  g.fillStyle = body; g.fillRect(0, 0, w, h); g.fillStyle = band; g.fillRect(0, h * .55, w, h * .16);
  g.fillStyle = dot; for (let k = 0; k < 10; k++) { g.beginPath(); g.arc((k + .5) * w / 10, h * .3, 7, 0, 7); g.fill(); }
}), roughness: .35 }));

const V = { a: 0, b: 1 };
// ---------------------------------------------------------------- food
function fountain(v) {   // a ceramic drinking fountain: a wide basin, a dome with water spilling over
  const g = new THREE.Group(), [body, band] = v ? ['#bfe9e0', '#3fb38d'] : ['#cfeaf8', '#4fb6e8'];
  lathe(g, [V2(0, 0), V2(.2, 0), V2(.22, .02), V2(.21, .1), V2(.19, .12), V2(.17, .1), V2(0, .1)], 48, glazeM(body, band, '#fffaf0'));
  cyl(g, flat('#7fd0f0', .15), .175, .175, .012, 0, .1, 0, 48);
  sph(g, glazeM(body, band, '#fffaf0'), .1, 0, .12, 0, 1, .9, 1, 32);
  sph(g, flat('#9fdcf4', .1), .035, 0, .21, 0, 1, .8, 1, 16);
  return g;
}
function treatJar(v) {   // a glass-look jar with a painted fish label and a knob lid
  const g = new THREE.Group(), lab = v ? '#ff9fbd' : '#86cfe6';
  const label = M('jar' + v, () => std({ map: tex('jar' + v, 512, 128, (gg, w, h) => { gg.fillStyle = '#f6f1e3'; gg.fillRect(0, 0, w, h); gg.fillStyle = lab; gg.fillRect(0, h * .25, w, h * .5); for (let k = 0; k < 3; k++) fishShape(gg, (k + .5) * w / 3, h * .5, 1.2, '#fbf6e6', '#6f5a40'); }), roughness: .3 }));
  lathe(g, [V2(0, 0), V2(.1, 0), V2(.115, .02), V2(.115, .2), V2(.1, .22), V2(0, .22)], 40, label);
  cyl(g, wood('b'), .1, .1, .03, 0, .235, 0, 40); sph(g, wood('b'), .03, 0, .265, 0, 1, .8, 1, 16);
  return g;
}
function fishPlate(v) {   // an oval plate with a whole painted fish on it
  const g = new THREE.Group(), [rim, fish] = v ? ['#ffcf5a', '#ff8d7a'] : ['#6fc3ec', '#ffcf5a'];
  const plate = lathe(g, [V2(0, 0), V2(.16, 0), V2(.2, .02), V2(.21, .035), V2(.19, .03), V2(.15, .012), V2(0, .012)], 48, glazeM('#fff6ea', rim, rim)); plate.scale.set(1, 1, .72);
  const f = sph(g, flat(fish, .5), .1, -.02, .03, 0, 1, .3, .42, 24); f.rotation.y = Math.PI / 2;
  const tail = cyl(g, flat(fish, .5), .05, .01, .012, .1, .028, 0, 3); tail.rotation.set(0, 0, Math.PI / 2);
  sph(g, flat('#5a4632', .4), .008, -.09, .045, .022, 1, 1, 1, 8);
  return g;
}
// ---------------------------------------------------------------- toys
function yarnBasket(v) {   // a wicker basket with three yarn balls and a loose strand
  const g = new THREE.Group(), bk = v ? wicker('#e2b483', '#c99a6b') : wicker('#d9c3a0', '#bfa37a');
  lathe(g, [V2(0, 0), V2(.2, 0), V2(.24, .16), V2(.25, .17), V2(.23, .17), V2(.19, .03), V2(0, .03)], 40, bk);
  const cols = v ? ['#ffb3c4', '#a6e3c4', '#fff1b8'] : ['#ff6b7d', '#5bb3e8', '#ffd34d'];
  [[-.07, .14, .03], [.07, .15, -.04], [0, .17, .08]].forEach(([x, y, z], i) => { sph(g, feltM(cols[i]), .075, x, y, z, 1, 1, 1, 24); });
  chain(g, flat(cols[0], 1), [[-.07, .2, .06], [-.2, .2, .15], [-.3, .02, .2], [-.45, .01, .25]], .006);
  return g;
}
function paperBox(v) {   // an open cardboard box with flaps (a cat can sit in it)
  const g = new THREE.Group(), card = M('card2', () => std({ map: tex('card2', 256, 256, (gg, w, h) => { gg.fillStyle = '#d9b07a'; gg.fillRect(0, 0, w, h); gg.fillStyle = 'rgba(120,80,40,.25)'; for (let y = 0; y < h; y += 10) gg.fillRect(0, y, w, 2); if (v) { gg.fillStyle = '#ff7f9f'; gg.beginPath(); gg.arc(w / 2, h / 2, 24, 0, 7); gg.fill(); } }), roughness: 1 }));
  const W = .5, L = .42, H = .3, t = .012;
  box(g, card, W, t, L, 0, t / 2, 0, .004);
  for (const [w, l, x, z] of [[W, t, 0, -L / 2], [W, t, 0, L / 2], [t, L, -W / 2, 0], [t, L, W / 2, 0]]) box(g, card, w, H, l, x, H / 2, z, .004);
  for (const sd of [-1, 1]) { const f = box(g, card, W - .01, t, .16, 0, H - .08 * Math.sin(.5), sd * (L / 2 + .08 * Math.cos(.5)), .004); f.rotation.x = sd * .5; }   // (flaps folded out and down from the top edges)
  return g;
}
function tunnel(v) {   // a crinkly fabric play tunnel lying on the floor (2 cells long)
  const g = new THREE.Group(), [c1, c2] = v ? ['#a6e3c4', '#5bb98c'] : ['#ffb3c4', '#ff7f9f'];
  const fab = M('tun' + v, () => std({ map: tex('tun' + v, 256, 256, (gg, w, h) => { gg.fillStyle = c1; gg.fillRect(0, 0, w, h); gg.fillStyle = c2; for (let x = 0; x < w; x += 32) gg.fillRect(x, 0, 10, h); }, 6, 1), roughness: .9 }));
  const r = .22, len = 1.0, t = .02, prof = [V2(r - t, -len / 2), V2(r, -len / 2), V2(r, len / 2), V2(r - t, len / 2), V2(r - t, -len / 2)];
  const m = lathe(g, prof, 40, fab); m.rotation.z = Math.PI / 2; m.position.y = r;
  for (const x of [-len / 2, len / 2]) { const ring = torus(g, flat(c2, .8), r - .005, .018, 8, 40); ring.rotation.y = Math.PI / 2; ring.position.set(x, r, 0); }
  return g;
}
function bellBall(v) {   // a light plastic ball with a bell inside showing through the slots
  const g = new THREE.Group(), c = v ? '#ffd34d' : '#ff7f9f';
  const shell = M('bb' + v, () => std({ map: tex('bb' + v, 256, 128, (gg, w, h) => { gg.fillStyle = c; gg.fillRect(0, 0, w, h); gg.fillStyle = '#6f5a40'; for (let k = 0; k < 6; k++) { gg.beginPath(); gg.ellipse((k + .5) * w / 6, h / 2, 10, 22, 0, 0, 7); gg.fill(); } }), roughness: .4 }));
  sph(g, shell, .07, 0, .07, 0, 1, 1, 1, 32);
  return g;
}
function catnipFish(v) {   // a plush fish stuffed with catnip: felt body, fins, tail, stitched seam
  const g = new THREE.Group(), [body, fin] = v ? ['#ff9f8a', '#ffd34d'] : ['#86cfe6', '#ff7f9f'];
  const b = sph(g, feltM(body), .14, 0, .06, 0, 1, .42, .5, 32); b.rotation.y = Math.PI / 2;
  const tail = cyl(g, feltM(fin), .08, .01, .025, 0, .06, -.17, 3); tail.rotation.set(Math.PI / 2, 0, 0);
  for (const sd of [-1, 1]) { const f = sph(g, feltM(fin), .04, sd * .055, .04, .02, .3, .2, 1, 12); f.rotation.y = sd * .5; }
  sph(g, flat('#5a4632', .4), .012, .045, .08, .1, 1, 1, 1, 10); sph(g, flat('#5a4632', .4), .012, -.045, .08, .1, 1, 1, 1, 10);
  return g;
}
function featherStand(v) {   // a wobbly toy: a weighted round base, a spring stalk, a tuft of feathers
  const g = new THREE.Group(), [base, f1, f2] = v ? ['#a6e3c4', '#ff7f9f', '#ffd34d'] : ['#ffcf5a', '#5bb3e8', '#ff7f9f'];
  lathe(g, [V2(0, 0), V2(.12, 0), V2(.14, .04), V2(.11, .1), V2(.04, .12), V2(0, .12)], 40, glazeM(base, '#fffaf0', '#fffaf0'));
  const pts = Array.from({ length: 12 }, (_, i) => [Math.sin(i * 1.3) * .02, .12 + i * .03, Math.cos(i * 1.3) * .02]);
  chain(g, flat('#c9c4ba', .4), pts, .006);
  [f1, f2, f1].forEach((c, i) => { const f = sph(g, flat(c, 1), .03, (i - 1) * .025, .5, 0, .35, 2.4, .8, 16); f.rotation.z = (i - 1) * .4; });
  return g;
}
// ---------------------------------------------------------------- furniture (cats lie on these: the top height is in anchors)
function basketBed(v) {   // a round wicker basket with a check cushion inside (2x2)
  const g = new THREE.Group(), bk = wicker(v ? '#e2b483' : '#d9c3a0', v ? '#c99a6b' : '#bfa37a');
  lathe(g, [V2(0, 0), V2(.5, 0), V2(.54, .16), V2(.56, .2), V2(.52, .2), V2(.47, .05), V2(0, .05)], 64, bk);
  torus(g, bk, .53, .035, 10, 64).rotation.x = Math.PI / 2; g.children.at(-1).position.y = .2;
  const cu = cyl(g, M('bbcu' + v, () => std({ map: gingham(...(v ? ['#e3f6ec', '#8fd6b1', '#5bb98c'] : ['#ffd9c9', '#ff9f8a', '#e9785f'])), roughness: .95 })), .46, .46, .06, 0, .08, 0, 64);
  return g;
}
function hammockStand(v) {   // a wooden A-frame with a sagging cloth hammock (2x2)
  const g = new THREE.Group(), cloth = M('hm' + v, () => std({ map: carpetTex(v ? '#ffb3c4' : '#7fcbe8', 'rgba(0,0,0,.1)', 2, 2), roughness: 1 }));
  for (const x of [-.48, .48]) for (const z of [-.3, .3]) { const leg = box(g, wood('a'), .05, .62, .05, x, .3, z, .015); leg.rotation.z = (x < 0 ? 1 : -1) * .12; }
  for (const x of [-.48, .48]) box(g, wood('a'), .06, .05, .7, x * .92, .6, 0, .015);
  const geo = new THREE.BoxGeometry(.8, .014, .56, 16, 1, 12); const pp = geo.attributes.position;
  for (let i = 0; i < pp.count; i++) { const u = pp.getX(i) / .4, w = pp.getZ(i) / .28; pp.setY(i, pp.getY(i) - .12 * (1 - u * u) * (1 - w * w * .4)); }
  geo.computeVertexNormals(); const h = part(g, geo, cloth); h.position.y = .56;
  return g;
}
function windowBox(v) {   // a low wooden window-seat box with a cushion on top (2x1)
  const g = new THREE.Group();
  box(g, wood(v ? 'b' : 'a'), 1.0, .34, .44, 0, .17, 0, .02);
  box(g, M('wbcu' + v, () => std({ map: gingham(...(v ? ['#efe6ff', '#bfa8f2', '#9a80e0'] : ['#fff4d6', '#ffd85a', '#f0b93a'])), roughness: .95 })), .94, .06, .4, 0, .37, 0, .03);
  for (const x of [-.3, 0, .3]) box(g, wood('b'), .2, .1, .012, x, .2, .222, .005);
  return g;
}
function teacupBed(v) {   // a giant teacup on a saucer, a cushion inside (2x2)
  const g = new THREE.Group(), [body, band] = v ? ['#fff6ea', '#86cfe6'] : ['#ffe0e8', '#ff7f9f'];
  lathe(g, [V2(0, 0), V2(.56, 0), V2(.58, .03), V2(.5, .045), V2(0, .045)], 64, glazeM(body, band, band));
  lathe(g, [V2(0, .04), V2(.36, .04), V2(.46, .16), V2(.5, .32), V2(.47, .33), V2(.43, .17), V2(.33, .08), V2(0, .08)], 64, glazeM(body, band, band));
  const handle = torus(g, glazeM(body, band, band), .1, .03, 10, 32); handle.position.set(.52, .2, 0);
  cyl(g, M('tccu' + v, () => std({ map: gingham(...(v ? ['#e3f6ec', '#8fd6b1', '#5bb98c'] : ['#fff4d6', '#ffd85a', '#f0b93a'])), roughness: .95 })), .4, .4, .06, 0, .11, 0, 64);
  return g;
}

const two = (ko, fn, extra = {}) => ({ ko, variants: 2, build: v => fn(v), ...extra });
export const EXTRA_ITEMS = {
  fountain: two('고양이 정수기', fountain, { gloss: .3 }), treat_jar: two('생선 쿠키 병', treatJar), fish_plate: two('생선 접시', fishPlate),
  yarn_basket: two('털실 바구니', yarnBasket), paper_box: two('종이 상자', paperBox), tunnel: two('놀이 터널', tunnel),
  bell_ball: two('방울 공', bellBall), catnip_fish: two('캣닢 생선 인형', catnipFish), feather_stand: two('깃털 오뚝이', featherStand),
  basket_bed: two('바구니 침대', basketBed, { anchors: { top: .11 } }), hammock_stand: two('해먹 의자', hammockStand, { anchors: { top: .5 } }),
  window_box: two('창가 상자', windowBox, { anchors: { top: .4 } }), teacup_bed: two('찻잔 침대', teacupBed, { anchors: { top: .14 } }),
};

// ---------------------------------------------------------------- deco (indoor)
function rug(v, wave) {   // a big round rug (3x3): woven, a border and a picture (paw or waves)
  const g = new THREE.Group(), [c1, c2] = wave ? (v ? ['#bdeaf0', '#3fb0e0'] : ['#a6e3c4', '#3fb38d']) : (v ? ['#ffe0e8', '#ff7f9f'] : ['#fff4d6', '#f0b93a']);
  const m = M('rug' + wave + v, () => std({ map: tex('rug' + wave + v, 512, 512, (gg, w, h) => {
    gg.fillStyle = c1; gg.fillRect(0, 0, w, h);
    gg.strokeStyle = c2; gg.lineWidth = 18; gg.beginPath(); gg.arc(w / 2, h / 2, w * .44, 0, 7); gg.stroke();
    if (wave) { gg.lineWidth = 10; for (let k = -2; k <= 2; k++) { gg.beginPath(); for (let x = 60; x <= w - 60; x += 8) gg.lineTo(x, h / 2 + k * 44 + Math.sin(x / 26) * 12); gg.stroke(); } }
    else { gg.fillStyle = c2; gg.beginPath(); gg.ellipse(w / 2, h / 2 + 30, 70, 58, 0, 0, 7); gg.fill(); for (const [dx, dy] of [[-80, -60], [-28, -100], [28, -100], [80, -60]]) { gg.beginPath(); gg.ellipse(w / 2 + dx, h / 2 + dy, 26, 32, 0, 0, 7); gg.fill(); } }
    for (let y = 0; y < h; y += 4) { gg.fillStyle = 'rgba(255,255,255,.06)'; gg.fillRect(0, y, w, 1); }
  }), roughness: 1 }));
  const r = cyl(g, m, .85, .85, .014, 0, .007, 0, 64);
  { const p = r.geometry.attributes.position, uv = r.geometry.attributes.uv; for (let i = 0; i < p.count; i++) uv.setXY(i, p.getX(i) / 1.7 + .5, p.getZ(i) / 1.7 + .5); }
  return g;
}
function floorLamp(v) {   // a mushroom lamp: a cap shade on a stalk, a glowing bulb under the cap
  const g = new THREE.Group(), cap = v ? '#ff7f9f' : '#ff9f8a';
  lathe(g, [V2(0, 0), V2(.14, 0), V2(.15, .02), V2(.06, .05), V2(0, .05)], 32, flat('#f6f1e3', .6));
  cyl(g, flat('#f6f1e3', .6), .03, .04, .6, 0, .33, 0, 16);
  lathe(g, [V2(0, .6), V2(.24, .6), V2(.25, .63), V2(.2, .72), V2(.1, .78), V2(0, .8)], 40, M('mush' + v, () => std({ map: tex('mush' + v, 256, 128, (gg, w, h) => { gg.fillStyle = cap; gg.fillRect(0, 0, w, h); gg.fillStyle = '#fffaf0'; for (let k = 0; k < 7; k++) { gg.beginPath(); gg.arc((k + .5) * w / 7, h * (k % 2 ? .35 : .65), 11, 0, 7); gg.fill(); } }), roughness: .7 })));
  sph(g, flat('#fff3c4', .2), .06, 0, .58, 0, 1, .6, 1, 16);
  return g;
}
function bookshelfLow(v) {   // a low two-shelf bookcase with books and a plant (2x1)
  const g = new THREE.Group(), w = wood(v ? 'b' : 'a');
  box(g, w, 1.0, .03, .3, 0, .015, 0, .01); box(g, w, 1.0, .03, .3, 0, .27, 0, .01); box(g, w, 1.0, .03, .3, 0, .53, 0, .01);
  for (const x of [-.485, .485]) box(g, w, .03, .55, .3, x, .275, 0, .01);
  const cols = ['#ff7f9f', '#86cfe6', '#ffd34d', '#a6e3c4', '#bfa8f2', '#ff9f8a'];
  let x = -.44; for (let i = 0; i < 9; i++) { const bw = .05 + (i % 3) * .015, bh = .18 + (i % 2) * .04; box(g, flat(cols[i % cols.length], .8), bw, bh, .22, x + bw / 2, .03 + bh / 2, 0, .006); x += bw + .01; }
  lathe(g, [V2(0, .545), V2(.06, .545), V2(.07, .63), V2(0, .63)], 24, flat('#e9785f', .7));
  for (const [dx, dz] of [[0, 0], [.04, .02], [-.04, -.01]]) sph(g, flat('#58a043', .9), .05, .25 + dx, .67, dz, 1, 1.2, 1, 12);
  return g;
}
function plantPot(v) {   // a monstera in a round pot (round split leaves, not a leaf mark)
  const g = new THREE.Group(), pot = v ? '#ff9f8a' : '#f6f1e3';
  lathe(g, [V2(0, 0), V2(.14, 0), V2(.18, .24), V2(.19, .26), V2(0, .26)], 32, glazeM(pot, '#e9785f', pot));
  for (let i = 0; i < 6; i++) { const a = i / 6 * Math.PI * 2, r = .12;
    const stem = cyl(g, flat('#4fa53b', .9), .008, .01, .3, Math.cos(a) * .05, .4, Math.sin(a) * .05, 8); stem.rotation.set(Math.sin(a) * .5, 0, -Math.cos(a) * .5);
    const leaf = sph(g, flat(i % 2 ? '#3f9a3e' : '#58a043', .85), .13, Math.cos(a) * r * 1.4, .55 + (i % 2) * .05, Math.sin(a) * r * 1.4, 1, .12, .8, 16); leaf.rotation.y = -a; }
  return g;
}
function catClock(v) {   // a standing cat-shaped clock: round face, ears, a tail pendulum
  const g = new THREE.Group(), c = v ? '#6f5a40' : '#f2a7a0';
  box(g, wood('b'), .3, .05, .2, 0, .025, 0, .015);
  cyl(g, flat(c, .6), .03, .03, .5, 0, .3, 0, 12);
  const face = cyl(g, M('clock' + v, () => std({ map: tex('clock' + v, 256, 256, (gg, w, h) => { gg.fillStyle = '#fbf6e6'; gg.fillRect(0, 0, w, h); gg.fillStyle = '#6f5a40'; for (let k = 0; k < 12; k++) { const a = k / 12 * Math.PI * 2; gg.beginPath(); gg.arc(w / 2 + Math.cos(a) * 92, h / 2 + Math.sin(a) * 92, 7, 0, 7); gg.fill(); } gg.lineWidth = 10; gg.strokeStyle = '#6f5a40'; gg.beginPath(); gg.moveTo(w / 2, h / 2); gg.lineTo(w / 2, h / 2 - 70); gg.moveTo(w / 2, h / 2); gg.lineTo(w / 2 + 50, h / 2); gg.stroke(); }), roughness: .6 })), .16, .16, .05, 0, .66, 0, 40);
  face.rotation.x = Math.PI / 2;
  const ring = torus(g, flat(c, .6), .16, .018, 8, 40); ring.position.set(0, .66, 0);
  for (const sd of [-1, 1]) { const ear = cyl(g, flat(c, .6), .0, .06, .09, sd * .1, .82, 0, 3); ear.rotation.z = -sd * .35; }
  return g;
}
function shellLamp(v) {   // a scallop-shell lamp on a little sand mound
  const g = new THREE.Group(), c = v ? '#ffd9c9' : '#fff1d6';
  lathe(g, [V2(0, 0), V2(.16, 0), V2(.12, .06), V2(0, .08)], 32, flat('#f5e2a8', .9));
  const shell = sph(g, M('shell' + v, () => std({ map: tex('shell' + v, 256, 64, (gg, w, h) => { gg.fillStyle = c; gg.fillRect(0, 0, w, h); gg.fillStyle = 'rgba(200,120,90,.35)'; for (let x = 0; x < w; x += 20) gg.fillRect(x, 0, 6, h); }, 1, 1), roughness: .5 })), .2, 0, .2, -.02, 1, .9, .35, 32);
  shell.rotation.x = -.25;
  sph(g, flat('#fff3c4', .2), .05, 0, .12, .05, 1, 1, 1, 16);
  return g;
}
function fishMobile(v) {   // a hanging mobile on a stand: a bar with three felt fish
  const g = new THREE.Group(), cols = v ? ['#ff7f9f', '#ffd34d', '#a6e3c4'] : ['#86cfe6', '#ff9f8a', '#ffd34d'];
  box(g, wood('b'), .28, .04, .28, 0, .02, 0, .015);
  cyl(g, wood('b'), .018, .018, .9, 0, .47, 0, 12);
  box(g, wood('b'), .5, .025, .025, .1, .92, 0, .01);
  [-.1, .1, .3].forEach((x, i) => { cyl(g, flat('#fffaf0', .8), .003, .003, .2, x, .82, 0, 6); const f = sph(g, feltM(cols[i]), .06, x, .69, 0, .45, .5, 1, 16); f.rotation.y = Math.PI / 2; });
  return g;
}
// ---------------------------------------------------------------- yard
function gardenBed(v) {   // a wooden raised bed with soil and little sprouts (2x1)
  const g = new THREE.Group(), w = wood('a');
  for (const [bw, bl, x, z] of [[1.1, .05, 0, -.25], [1.1, .05, 0, .25], [.05, .45, -.525, 0], [.05, .45, .525, 0]]) box(g, w, bw, .24, bl, x, .12, z, .015);
  box(g, flat('#8b5e3c', 1), 1.0, .18, .44, 0, .1, 0, .02);
  for (let i = 0; i < 5; i++) { const x = -.4 + i * .2; cyl(g, flat('#58a043', .9), .006, .008, .08, x, .23, 0, 6); sph(g, flat('#74c254', .9), .03, x - .02, .28, 0, 1, .5, .7, 10); sph(g, flat('#74c254', .9), .03, x + .02, .28, 0, 1, .5, .7, 10); }
  return g;
}
function bench(v) {   // a garden bench (2x1)
  const g = new THREE.Group(), w = wood(v ? 'b' : 'a');
  for (const z of [-.12, 0, .12]) box(g, w, 1.0, .03, .1, 0, .42, z, .01);
  for (const x of [-.42, .42]) for (const z of [-.14, .14]) box(g, w, .05, .42, .05, x, .21, z, .012);
  for (const y of [.55, .68]) box(g, w, 1.0, .08, .03, 0, y, -.17, .012);
  for (const x of [-.42, .42]) box(g, w, .05, .3, .03, x, .58, -.17, .012);
  return g;
}
function mailbox(v) {   // a round-top mailbox on a post, a little flag
  const g = new THREE.Group(), c = v ? '#86cfe6' : '#ff7f9f';
  box(g, wood('a'), .07, .8, .07, 0, .4, 0, .015);
  box(g, flat(c, .6), .2, .16, .32, 0, .88, 0, .03); const top = cyl(g, flat(c, .6), .1, .1, .32, 0, .96, 0, 24); top.rotation.x = Math.PI / 2;
  box(g, flat('#ffd34d', .6), .01, .12, .06, .11, .98, -.08, .005); box(g, flat('#ffd34d', .6), .01, .06, .1, .11, 1.05, -.06, .005);
  return g;
}
function gardenLantern(v) {   // a round paper-look garden lantern on a short post
  const g = new THREE.Group();
  box(g, wood('b'), .2, .04, .2, 0, .02, 0, .012); cyl(g, wood('b'), .025, .025, .4, 0, .22, 0, 12);
  sph(g, flat(v ? '#ffe0e8' : '#fff1d6', .4), .12, 0, .52, 0, 1, 1.1, 1, 24);
  for (const y of [.4, .64]) { const r = torus(g, wood('b'), .07, .012, 6, 24); r.rotation.x = Math.PI / 2; r.position.y = y; }
  return g;
}
function birdFeeder(v) {   // a little house feeder on a post with seeds on the tray
  const g = new THREE.Group(), roof = v ? '#86cfe6' : '#ff9f8a';
  box(g, wood('a'), .06, .9, .06, 0, .45, 0, .015);
  box(g, wood('b'), .34, .03, .3, 0, .91, 0, .01);
  for (const x of [-.14, .14]) box(g, wood('b'), .03, .18, .03, x, 1.0, 0, .008);
  for (const sd of [-1, 1]) { const r = box(g, flat(roof, .7), .4, .025, .2, 0, 1.13, sd * .08, .01); r.rotation.x = sd * .6; }
  for (let i = 0; i < 12; i++) sph(g, flat('#e2b483', .9), .012, -.1 + (i % 4) * .065, .935, -.08 + Math.floor(i / 4) * .08, 1, .6, 1, 6, 'detail');
  return g;
}
function pond(v) {   // a small round pond rimmed with pebbles (2x2), a lily pad
  const g = new THREE.Group();
  cyl(g, flat(v ? '#5ccbea' : '#3fc4dc', .15), .52, .52, .02, 0, .01, 0, 48);
  for (let i = 0; i < 16; i++) { const a = i / 16 * Math.PI * 2; sph(g, flat(i % 2 ? '#c9c4ba' : '#d9d3c7', .9), .07 + (i % 3) * .01, Math.cos(a) * .56, .03, Math.sin(a) * .56, 1.2, .6, 1, 12); }
  cyl(g, flat('#58a043', .9), .12, .12, .012, .15, .026, -.1, 24);
  sph(g, flat('#ff9fbd', .7), .04, .15, .05, -.1, 1, .8, 1, 12);
  return g;
}
function steppingStones(v) {   // three flat round stones in a row (2x1)
  const g = new THREE.Group(); [-.32, 0, .32].forEach((x, i) => sph(g, flat(i % 2 ? '#c9c4ba' : '#d9d3c7', .9), .16 - (i % 2) * .02, x, .015, (i % 2) * .05, 1, .18, .85, 20)); return g;
}
function picketFence(v) {   // a short white picket fence (2x1)
  const g = new THREE.Group(), c = v ? '#fff1d6' : '#fbf6e6';
  for (let i = 0; i < 7; i++) { const x = -.48 + i * .16; box(g, flat(c, .7), .07, .4, .025, x, .2, 0, .01); cyl(g, flat(c, .7), .0, .05, .06, x, .43, 0, 4); }
  for (const y of [.12, .3]) box(g, flat(c, .7), 1.06, .05, .02, 0, y, -.02, .008);
  return g;
}
function parasol(v) {   // a striped beach parasol over a little round table (2x2)
  const g = new THREE.Group(), [c1, c2] = v ? ['#ffd34d', '#fff1d6'] : ['#ff7f9f', '#fff1d6'];
  cyl(g, flat('#f6f1e3', .7), .24, .24, .03, 0, .015, 0, 24); cyl(g, wood('b'), .02, .02, 1.3, 0, .66, 0, 10);
  cyl(g, M('para' + v, () => std({ map: tex('para' + v, 256, 64, (gg, w, h) => { for (let k = 0; k < 8; k++) { gg.fillStyle = k % 2 ? c1 : c2; gg.fillRect(k * w / 8, 0, w / 8, h); } }), roughness: .8 })), .02, .62, .2, 0, 1.22, 0, 16);   // (a striped cone canopy)
  return g;
}
function picnicMat(v) {   // a check picnic blanket with a basket (2x2)
  const g = new THREE.Group();
  const m = box(g, M('pic' + v, () => std({ map: gingham(...(v ? ['#e3f6ec', '#8fd6b1', '#5bb98c'] : ['#ffd9c9', '#ff9f8a', '#e9785f'])), roughness: 1 })), 1.1, .012, 1.0, 0, .006, 0, .005);
  lathe(g, [V2(0, .012), V2(.12, .012), V2(.14, .14), V2(0, .14)], 24, wicker('#e2b483', '#c99a6b')).position.set(.35, 0, -.3);
  const h = torus(g, wicker('#e2b483', '#c99a6b'), .1, .012, 6, 24); h.position.set(.35, .14, -.3);
  return g;
}
function flowerPotRow(v) {   // three small pots with flowers in a row (2x1)
  const g = new THREE.Group(), fc = v ? ['#ffd23f', '#ffffff', '#ffd23f'] : ['#ff5d6c', '#ffd23f', '#ff5d6c'];
  [-.32, 0, .32].forEach((x, i) => {
    lathe(g, [V2(0, 0), V2(.08, 0), V2(.1, .14), V2(0, .14)], 24, glazeM('#e9785f', '#fbf6e6', '#e9785f')).position.x = x;
    cyl(g, flat('#4fa53b', .9), .006, .006, .14, x, .2, 0, 6); sph(g, flat(fc[i], .7), .045, x, .3, 0, 1, 1.2, 1, 14);
  });
  return g;
}
function sandbox(v) {   // a wooden sand pit (2x2), a little bucket and spade
  const g = new THREE.Group(), w = wood('a');
  for (const [bw, bl, x, z] of [[1.1, .06, 0, -.52], [1.1, .06, 0, .52], [.06, 1.0, -.52, 0], [.06, 1.0, .52, 0]]) box(g, w, bw, .14, bl, x, .07, z, .015);
  box(g, flat('#f5e2a8', 1), 1.0, .08, 1.0, 0, .04, 0, .02);
  lathe(g, [V2(0, .08), V2(.06, .08), V2(.08, .2), V2(0, .2)], 20, flat(v ? '#86cfe6' : '#ff7f9f', .5)).position.set(.3, 0, .25);
  return g;
}
function swingBasket(v) {   // a hanging basket swing on a wooden frame (2x2)
  const g = new THREE.Group(), w = wood('a');
  for (const x of [-.5, .5]) for (const z of [-.25, .25]) { const l = box(g, w, .05, 1.05, .05, x, .5, z, .015); l.rotation.x = (z < 0 ? 1 : -1) * .2; }
  box(g, w, 1.1, .06, .06, 0, 1.0, 0, .015);
  for (const x of [-.2, .2]) cyl(g, flat('#fffaf0', .8), .006, .006, .55, x, .72, 0, 6);
  lathe(g, [V2(0, .35), V2(.24, .35), V2(.28, .48), V2(.25, .48), V2(.21, .38), V2(0, .38)], 32, wicker('#d9c3a0', '#bfa37a'));
  cyl(g, M('swcu' + v, () => std({ map: gingham(...(v ? ['#efe6ff', '#bfa8f2', '#9a80e0'] : ['#fff4d6', '#ffd85a', '#f0b93a'])), roughness: .95 })), .21, .21, .04, 0, .4, 0, 32);
  return g;
}
function wateringCan(v) {   // a round watering can with a spout and a handle
  const g = new THREE.Group(), c = v ? '#86cfe6' : '#a6e3c4';
  lathe(g, [V2(0, 0), V2(.11, 0), V2(.12, .16), V2(.08, .2), V2(0, .2)], 32, flat(c, .4));
  const sp = cyl(g, flat(c, .4), .018, .025, .22, .17, .14, 0, 12); sp.rotation.z = -1.0;
  cyl(g, flat(c, .4), .035, .02, .03, .26, .2, 0, 12).rotation.z = -1.0;
  const h = torus(g, flat(c, .4), .07, .015, 8, 24); h.position.set(-.06, .22, 0);
  return g;
}

Object.assign(EXTRA_ITEMS, {
  rug_round: two('둥근 러그', v => rug(v, false)), rug_wave: two('물결 러그', v => rug(v, true)), floor_lamp: two('버섯 스탠드', floorLamp),
  bookshelf_low: two('낮은 책장', bookshelfLow), plant_pot: two('몬스테라 화분', plantPot), cat_clock: two('고양이 시계', catClock),
  shell_lamp: two('조개 등', shellLamp), fish_mobile: two('물고기 모빌', fishMobile),
  garden_bed: two('텃밭 상자', gardenBed), bench: two('나무 벤치', bench, { anchors: { top: .44 } }), mailbox: two('우체통', mailbox),
  garden_lantern: two('정원 등불', gardenLantern), bird_feeder: two('새 모이통', birdFeeder), pond: two('작은 연못', pond),
  stepping_stones: two('징검돌', steppingStones), picket_fence: two('나무 울타리', picketFence), parasol: two('파라솔', parasol),
  picnic_mat: two('소풍 돗자리', picnicMat, { anchors: { top: .012 } }), flower_pot_row: two('꽃 화분 줄', flowerPotRow), sandbox: two('모래 놀이터', sandbox),
  swing_basket: two('그네 바구니', swingBasket, { anchors: { top: .42 } }), watering_can: two('물뿌리개', wateringCan),
});
