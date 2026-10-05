// Motion feel (step B, docs/ART_DIRECTION.md 7): the layer that makes the clips read like a toy come alive,
// laid OVER the computed clips at playback time (the game does the same in Unity). Five pieces:
//
//   SpringBones      tail and ears follow the body a beat late and overshoot (overlap / follow-through), in world
//                    space, so a turn, a hop or a landing whips the tail without any extra keys
//   Squash           the body stretches with speed and squashes on a landing, about the feet (volume kept)
//   holdRemap        timing: a held beat at the bottom of an anticipation (the crouch before a jump), a quicker
//                    action after it
//   walkBounce       a light bob at the step rhythm, a dip on each footfall, a steadier head
//   PetReaction      leaning into the hand: head tilts and pushes up, eyes close, ears ease back, tail lifts and
//                    sways slowly, a purr shiver, hearts
//
//   HeadSteady       a walking cat keeps its head level and still while the body sways under it: the head's world
//                    rotation is held on a slow average of where the body points
//   lookUp           before a high jump the cat looks up at the target, longer the higher it is
//
// Everything is time-stepped: call update(dt) once per rendered frame, after the clip pose is applied.
import * as THREE from 'three';
const clamp = (v, a, b) => Math.min(b, Math.max(a, v)), ss = (a, b, x) => { const t = clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); };
const qa = new THREE.Quaternion(), qb = new THREE.Quaternion(), qc = new THREE.Quaternion(), va = new THREE.Vector3();

export class SpringBones {
  // chains: arrays of bones from root to tip (each bone's parent is the previous one, or animated)
  constructor(chains, { stiffness = 90, damping = 11, tipLoose = .5 } = {}) {
    this.chains = chains.map(ch => ch.map((b, i) => ({ b, k: stiffness * (1 - tipLoose * i / Math.max(1, ch.length - 1)), c: damping, w: new THREE.Vector3(), q: null })));
  }
  reset() { for (const ch of this.chains) for (const s of ch) { s.q = null; s.w.set(0, 0, 0); } }
  update(dt) {
    dt = Math.min(dt, 1 / 20);
    for (const ch of this.chains) for (const s of ch) {
      const b = s.b; b.parent.updateWorldMatrix(true, false);
      b.parent.getWorldQuaternion(qa);                                   // parent as it is now (already sprung)
      const target = qb.copy(qa).multiply(b.quaternion);                 // where the clip wants this bone, in world
      if (!s.q) s.q = target.clone();
      // angular spring in world space: error = rotation from current to target
      qc.copy(s.q).invert().premultiply(target);                         // target * current^-1
      if (qc.w < 0) { qc.x = -qc.x; qc.y = -qc.y; qc.z = -qc.z; qc.w = -qc.w; }
      const ang = 2 * Math.acos(clamp(qc.w, -1, 1)), sn = Math.sqrt(Math.max(1e-12, 1 - qc.w * qc.w));
      va.set(qc.x / sn, qc.y / sn, qc.z / sn).multiplyScalar(ang);
      s.w.addScaledVector(va, s.k * dt).multiplyScalar(Math.max(0, 1 - s.c * dt));
      const wl = s.w.length();
      if (wl > 1e-6) { qc.setFromAxisAngle(va.copy(s.w).divideScalar(wl), wl * dt); s.q.premultiply(qc).normalize(); }
      b.quaternion.copy(qa).invert().multiply(s.q);                      // back to the bone's local rotation
      b.updateWorldMatrix(false, false);
    }
  }
}

// squash & stretch about the feet: sy > 1 stretches (thinner), < 1 squashes (wider); volume kept (sx = sz = 1/sqrt(sy))
export class Squash {
  constructor({ stretchK = .05, maxStretch = .1, impact = 1.3, k = 260, c = 16 } = {}) { Object.assign(this, { stretchK, maxStretch, impact, k, c }); this.reset(); }
  reset() { this.s = 0; this.v = 0; this.prevY = null; this.prevVy = 0; }
  // y: the root's height this frame (world); returns the scale to put on the cat's group
  update(dt, y) {
    dt = Math.min(dt, 1 / 20);
    const vy = this.prevY == null ? 0 : (y - this.prevY) / dt;
    if (this.prevVy < -.8 && vy > -.2) this.v -= this.impact * Math.min(2, -this.prevVy / 2.5);   // a landing: an impulse into squash
    if (this.prevVy > -.05 && vy > 1.0) this.v += .6;                                             // a take-off: a snap of stretch
    const target = clamp(this.stretchK * Math.abs(vy), 0, this.maxStretch);
    this.v += (this.k * (target - this.s) - this.c * this.v) * dt; this.s += this.v * dt;
    this.prevY = y; this.prevVy = vy;
    const sy = 1 + clamp(this.s, -.16, .14);
    return { sy, sxz: 1 / Math.sqrt(sy) };
  }
}

// timing: playback time -> clip time with a held beat at `at` (length `hold`) and the rest of the clip played
// `speedAfter` times faster (anticipation: hold at the bottom of the crouch, then go)
export function holdRemap(tv, at, hold, speedAfter = 1) {
  if (tv < at) return tv;
  if (tv < at + hold) return at;
  return at + (tv - at - hold) * speedAfter;
}

// walking: a bob at twice the step rate (each footfall), a dip right after the plant, a head held steadier
export function walkBounce(t, cycle, { bob = .014, dip = .02 } = {}) {
  const ph = (t / cycle) * 2 % 1;                  // two footfalls (front pair) per cycle
  const y = bob * (.5 - .5 * Math.cos(2 * Math.PI * ph));
  const sy = 1 - dip * Math.exp(-((ph - .05) ** 2) / .004);
  return { y, sy, headSteady: .6 };
}

// petting: weight w (0..1) of the reaction, and the extra rotations to put on the bones after the clip pose
export class PetReaction {
  constructor(rig) { this.rig = rig; this.t = 0; }
  apply(t, w, side = 1) {
    const B = this.rig.B, e = new THREE.Euler(), q = new THREE.Quaternion();
    const rot = (bone, x, y, z) => { e.set(x, y, z); q.setFromEuler(e); bone.quaternion.multiply(q); };
    const pur = Math.sin(t * 2 * Math.PI * 24) * .004 * w;                       // purr shiver
    rot(B.Neck, -.12 * w, .1 * side * w, 0);
    rot(B.Head, -.18 * w + pur, .12 * side * w, .28 * side * w * (1 + .15 * Math.sin(t * 3)));   // tilt and push up into the hand
    rot(B.Ear_L, .45 * w, 0, -.15 * w); rot(B.Ear_R, .45 * w, 0, .15 * w);       // ears ease back, relaxed
    rot(B.Chest, pur, 0, 0);
    rot(B.Tail1, -.55 * w, .25 * w * Math.sin(t * 1.6), 0);                     // tail lifts, slow happy sway
    rot(B.Tail3, 0, .25 * w * Math.sin(t * 1.6 - .8), 0);
    const face = this.rig.face; if (face && face.morphTargetInfluences) face.morphTargetInfluences[0] = Math.max(face.morphTargetInfluences[0], .92 * w);   // eyes close
  }
}

// little pink hearts rising from the head (sprites), and a cartoon hand for the petting scenes
export function heartSprite() {
  const cv = document.createElement('canvas'); cv.width = cv.height = 64; const g = cv.getContext('2d');
  g.fillStyle = '#ff7f9f'; g.beginPath(); g.moveTo(32, 54); g.bezierCurveTo(4, 34, 8, 8, 32, 20); g.bezierCurveTo(56, 8, 60, 34, 32, 54); g.fill();
  g.fillStyle = 'rgba(255,255,255,.7)'; g.beginPath(); g.ellipse(22, 22, 6, 4, -.6, 0, 7); g.fill();
  const t = new THREE.CanvasTexture(cv); t.colorSpace = THREE.SRGBColorSpace;
  return new THREE.SpriteMaterial({ map: t, transparent: true, depthWrite: false });
}
export function cartoonHand(color = '#f6d2bd') {
  const g = new THREE.Group(), m = new THREE.MeshStandardMaterial({ color, roughness: .7 });
  const palm = new THREE.Mesh(new THREE.SphereGeometry(.07, 24, 16), m); palm.scale.set(1, .45, 1.1); g.add(palm);
  for (let i = 0; i < 4; i++) { const f = new THREE.Mesh(new THREE.CapsuleGeometry(.018, .06, 6, 12), m); f.rotation.x = Math.PI / 2; f.position.set(-.045 + i * .03, -.005, .1); g.add(f); }
  const th = new THREE.Mesh(new THREE.CapsuleGeometry(.018, .045, 6, 12), m); th.rotation.set(Math.PI / 2, 0, -.9); th.position.set(.075, 0, .02); g.add(th);
  const cuff = new THREE.Mesh(new THREE.CylinderGeometry(.065, .07, .06, 24), new THREE.MeshStandardMaterial({ color: '#ff9ab3', roughness: .9 })); cuff.rotation.x = Math.PI / 2; cuff.position.z = -.1; g.add(cuff);
  g.traverse(o => { if (o.isMesh) o.castShadow = true; });
  return g;
}

export class HeadSteady {
  constructor(head, { follow = .12, hold = .75 } = {}) { this.head = head; this.follow = follow; this.hold = hold; this.avg = null; }
  reset() { this.avg = null; }
  update() {
    const h = this.head; h.updateWorldMatrix(true, false);
    const qw = h.getWorldQuaternion(new THREE.Quaternion());
    if (!this.avg) this.avg = qw.clone(); else this.avg.slerp(qw, this.follow);
    const want = qw.clone().slerp(this.avg, this.hold);                     // mostly the steady average
    h.parent.getWorldQuaternion(qa);
    h.quaternion.copy(qa.invert().multiply(want)); h.updateWorldMatrix(false, true);
  }
}
// jump height -> anticipation: how long the crouch is held (looking up at the target) before the take-off
export function jumpAnticipation(height) { return { hold: clamp((height - .2) / .6, 0, 1) * .45, look: clamp(height / .8, 0, 1) * .5 }; }
