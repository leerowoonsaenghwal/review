// Cat Island motion data: keyframed clips for the v2 model, baked at 30 fps.
//
// A pose is a flat set of channels (hips, spine, neck, head, shoulder blades, four paw targets, tail, ears,
// face). Clips are written like an animator's dope sheet: key poses with timing and easing, plus procedural
// layers that are hard to key by hand (gait foot placement, breathing, blinks). Every frame is then solved
// onto the skeleton and stored as bone rotations, so the result is ordinary animation data (glTF/FBX clips).
//
// Real-cat references used for timing (scaled to the toy cat's leg length by dynamic similarity, i.e. the
// same Froude number v^2 / (g * hip height) as a real cat):
//   walk  0.62 m/s, stride 0.45-0.49 m, cycle 0.8 s, stance 60 % fore / 55 % hind, lateral sequence
//         (Stadig & Bergh 2013, BMC Vet Res 9:129, pressure-walkway data from 18 healthy cats)
//   trot  diagonal pairs, stance ~45 %;  gallop: rotary (hind L, hind R, fore R, fore L), stance ~30 %
//   stance phase details: paw lands, wrist/hock "yield" under load, then the heel lifts and the toes
//   peel off last; in swing the front paw folds back at the wrist, then reaches forward before landing.
import * as THREE from 'three';
import { makeContact, LIMBS } from './catcontact.js';

export const FPS = 30;
const TAU = Math.PI * 2, G = 9.81;
const clamp = (x, a, b) => Math.min(b, Math.max(a, x));
const lerp = (a, b, t) => a + (b - a) * t;
const ss = t => { t = clamp(t, 0, 1); return t * t * (3 - 2 * t); };
const mj = t => { t = clamp(t, 0, 1); return t * t * t * (10 + t * (-15 + 6 * t)); };   // minimum-jerk ease
const seg = (t, a, b) => clamp((t - a) / (b - a), 0, 1);
const frac = x => x - Math.floor(x);
const bump = (t, c, w) => Math.exp(-(((t - c) / w) ** 2));
const V = (x = 0, y = 0, z = 0) => new THREE.Vector3(x, y, z);

// monotone cubic key interpolation (no overshoot; equal neighbouring keys give a clean hold)
export function K(keys, t) {
  if (t <= keys[0][0]) return keys[0][1];
  const n = keys.length;
  if (t >= keys[n - 1][0]) return keys[n - 1][1];
  let i = 0; while (keys[i + 1][0] < t) i++;
  const [t0, v0] = keys[i], [t1, v1] = keys[i + 1], h = t1 - t0, u = (t - t0) / h;
  const slope = j => j < 0 || j >= n - 1 ? 0 : (keys[j + 1][1] - keys[j][1]) / (keys[j + 1][0] - keys[j][0]);
  const tan = j => { const a = slope(j - 1), b = slope(j); if (j === 0 || j === n - 1 || a * b <= 0) return 0; return 2 / (1 / a + 1 / b); };
  const m0 = tan(i) * h, m1 = tan(i + 1) * h, u2 = u * u, u3 = u2 * u;
  return (2 * u3 - 3 * u2 + 1) * v0 + (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * v1 + (u3 - u2) * m1;
}
const blinkAt = (t, times, len = .16) => Math.max(0, ...times.map(c => bump(t, c, len / 2.5)));

// ------------------------------------------------------------------ rig
const FEET = { FL: ['Scapula_L', 'UpperArm_L', 'Forearm_L', 'Hand_L', 'Fingers_L', 1, 'front'], FR: ['Scapula_R', 'UpperArm_R', 'Forearm_R', 'Hand_R', 'Fingers_R', -1, 'front'],
  HL: [null, 'Thigh_L', 'Shin_L', 'Foot_L', 'Toes_L', 1, 'hind'], HR: [null, 'Thigh_R', 'Shin_R', 'Foot_R', 'Toes_R', -1, 'hind'] };
export const FOOT_KEYS = Object.keys(FEET);

export function makeRig(model) {
  const B = model.userData.bones, d = model.userData.dims;
  const rest = Object.fromEntries(Object.entries(B).map(([n, b]) => [n, b.position.clone()]));
  const J = d.joints;
  const len = n => rest[n].length();
  const rig = {
    model, B, d, J, rest, face: model.userData.meshes.face,
    L: { F: [len('Forearm_L'), len('Hand_L'), len('Fingers_L')], H: [len('Shin_L'), len('Foot_L'), len('Toes_L')] },
    ballH: d.ballH, hipY: J.Hips.y, chestY: J.Chest.y, torso: J.Chest.z - J.Hips.z,
    restFoot: { FL: J.Fingers_L, FR: J.Fingers_R, HL: J.Toes_L, HR: J.Toes_R },
  };
  // how far a paw can reach fore and aft from under its shoulder / hip at standing height
  const reach = (pivot, chain, ball) => { const L = chain.reduce((a, b) => a + b, 0) * .97, H = pivot.y - ball.y; return Math.sqrt(Math.max(0, L * L - H * H)); };
  rig.reachF = reach(J.UpperArm_L, [rig.L.F[0], rig.L.F[1], rig.L.F[2]], J.Fingers_L);
  rig.reachH = reach(J.Thigh_L, [rig.L.H[0], rig.L.H[1], rig.L.H[2]], J.Toes_L);
  rig.hipH = J.Thigh_L.y;
  // the sculpted paw bottoms reach a little below the paw-ball height: lift the ground targets by that much
  // so planted paws stand on the floor instead of sinking into it
  const pa = model.userData.meshes.body.geometry.attributes.position; let low = Infinity;
  for (let i = 0; i < pa.count; i++) low = Math.min(low, pa.getY(i));
  rig.floorLift = Math.max(0, -low); rig.ballH += rig.floorLift;
  return rig;
}

// the neutral standing pose
export function stand(rig) {
  const P = {
    rootX: 0, rootY: 0, rootZ: 0, rootYaw: 0,
    hipX: 0, hipY: 0, hipZ: 0, hipPitch: 0, hipYaw: 0, hipRoll: 0,
    spPitch: 0, spYaw: 0, spRoll: 0, nkPitch: 0, nkYaw: 0, nkRoll: 0, hdPitch: 0, hdYaw: 0, hdRoll: 0,
    scapL: 0, scapR: 0, scapLy: 0, scapRy: 0,
    tailBase: 0, tailYaw: 0, tailBend: .04, tailCurl: 0, tailTip: 0, tailWave: 0, tailWph: 0,
    earLp: 0, earLy: 0, earLr: 0, earRp: 0, earRy: 0, earRr: 0,
    blink: 0, mouth: 0, tongue: 0, tongueBend: 0, tongueCurl: 0, tongueSpread: 0, tongueYaw: 0, breath: 0,
  };
  for (const f of FOOT_KEYS) {
    const r = rig.restFoot[f];
    Object.assign(P, { [f + 'x']: r.x, [f + 'y']: rig.ballH, [f + 'z']: r.z, [f + 'a']: f[0] === 'F' ? .3 : .2, [f + 't']: 0, [f + 'fk']: 0, [f + 'k1']: 0, [f + 'k2']: 0, [f + 'k3']: 0, [f + 'k4']: 0, [f + 'kz']: 0 });
  }
  return P;
}
export const mix = (A, B, k) => { const o = {}; for (const key in A) o[key] = lerp(A[key], B[key] ?? A[key], k); return o; };
const over = (P, ...o) => Object.assign({ ...P }, ...o);

// ------------------------------------------------------------------ solve a pose onto the bones
const _q = new THREE.Quaternion(), _q2 = new THREE.Quaternion(), _e = new THREE.Euler();
const qe = (x, y, z, q = new THREE.Quaternion()) => q.setFromEuler(_e.set(x, y, z, 'YXZ'));
function setDir(bone, off, dir) {
  const pq = bone.parent.getWorldQuaternion(new THREE.Quaternion());
  const v0 = off.clone().normalize().applyQuaternion(pq);
  const sw = new THREE.Quaternion().setFromUnitVectors(v0, dir.clone().normalize());
  bone.quaternion.copy(pq.clone().invert().multiply(sw).multiply(pq));
  bone.updateMatrixWorld(true);
}
function midJoint(A, W, l1, l2, pole) {
  const AW = W.clone().sub(A), D = clamp(AW.length(), Math.abs(l1 - l2) + 1e-4, l1 + l2 - 1e-4), n = AW.normalize();
  const a = (l1 * l1 - l2 * l2 + D * D) / (2 * D), h = Math.sqrt(Math.max(0, l1 * l1 - a * a));
  const pp = pole.clone().addScaledVector(n, -n.dot(pole)).normalize();
  return A.clone().addScaledVector(n, a).addScaledVector(pp, h);
}

// tailPoses: optional per-joint poses (time-lagged for follow-through); earPose likewise
export function applyPose(rig, P, lag = {}) {
  const { B, rest, model } = rig;
  for (const n in B) { B[n].quaternion.identity(); B[n].position.copy(rest[n]); B[n].scale.set(1, 1, 1); }
  B.Root.position.set(P.rootX, P.rootY, P.rootZ); B.Root.quaternion.setFromAxisAngle(V(0, 1, 0), P.rootYaw);
  B.Hips.position.add(V(P.hipX, P.hipY, P.hipZ)); qe(P.hipPitch, P.hipYaw, P.hipRoll, B.Hips.quaternion);
  [['Spine1', .3], ['Spine2', .35], ['Chest', .35]].forEach(([n, w]) => qe(P.spPitch * w, P.spYaw * w, P.spRoll * w, B[n].quaternion));
  qe(P.nkPitch, P.nkYaw, P.nkRoll, B.Neck.quaternion);
  B.Scapula_L.quaternion.setFromAxisAngle(V(1, 0, 0), P.scapL); B.Scapula_L.position.y += P.scapLy;
  B.Scapula_R.quaternion.setFromAxisAngle(V(1, 0, 0), P.scapR); B.Scapula_R.position.y += P.scapRy;
  B.Belly.scale.set(1 + .05 * P.breath, 1 + .09 * P.breath, 1 + .03 * P.breath);
  // tongue (5 bones). tongue 0..1 = how far it slides out; tongueBend bends the whole length down (+) / up (-);
  // tongueCurl rolls mostly the tip: + folds it under (the J cats lap with), - scoops it up (licking, yawning);
  // tongueSpread flattens and widens the front when it presses on fur; tongueYaw swings it sideways (lip lick)
  // it can only bend where it is already out past the lips, so each joint's share of the bend is weighted by
  // how far that joint has slid out of the mouth (otherwise it would poke down through the chin)
  const ext = clamp(P.tongue, 0, 1.2), HS = rig.d.HS, slide = .38 * HS * ext;
  B.Tongue1.position.add(V(0, -.02 * HS * ext, slide));
  const WB = [.1, .3, .28, .2, .12], WC = [0, .05, .15, .32, .48], WY = [.3, .3, .2, .12, .08];
  const out = rig.d.tongueU.map(u => { const z = u * rig.d.tongueLen + slide - rig.d.tongueIn; return clamp((z + .04 * HS) / (.14 * HS), 0, 1); });
  const ob = WB.reduce((a, w, i) => a + w * out[i], 0) || 1, oy = WY.reduce((a, w, i) => a + w * out[i], 0) || 1;
  for (let i = 0; i < 5; i++) {
    const kb = WB[i] * out[i] / Math.max(ob, .35), ky = WY[i] * out[i] / Math.max(oy, .35);
    qe(1.5 * P.tongueBend * kb + 2.3 * P.tongueCurl * WC[i] * out[i], 1.1 * P.tongueYaw * ky, 0, B['Tongue' + (i + 1)].quaternion);
  }
  // tail (each joint may come from a slightly earlier pose: overlapping action / follow-through)
  for (let i = 1; i <= 10; i++) {
    const T = lag.tail ? lag.tail[i - 1] : P, k = (i - 1) / 9;
    const wave = T.tailWave * Math.sin(TAU * T.tailWph - i * .55) * (.3 + .7 * k);
    if (i === 1) qe(T.tailBase, T.tailYaw + wave * .5, 0, B.Tail1.quaternion);
    else qe(T.tailBend + (i >= 8 ? T.tailTip : 0), T.tailCurl + wave, 0, B['Tail' + i].quaternion);
  }
  const E = lag.ear || P;
  qe(E.earLp, E.earLy, E.earLr, B.Ear_L.quaternion); qe(E.earRp, -E.earRy, -E.earRr, B.Ear_R.quaternion);
  model.updateMatrixWorld(true);
  // head: authored in the root frame (cats stabilise the head while the body moves)
  const rq = B.Root.getWorldQuaternion(new THREE.Quaternion());
  const want = rq.clone().multiply(qe(P.hdPitch, P.hdYaw, P.hdRoll));
  B.Head.quaternion.copy(B.Neck.getWorldQuaternion(_q).invert().multiply(want));
  B.Head.updateMatrixWorld(true);
  // legs
  const fwd = V(0, 0, 1).applyQuaternion(rq), side = V(1, 0, 0).applyQuaternion(rq), up = V(0, 1, 0);
  for (const f of FOOT_KEYS) {
    const [, n0, n1, n2, n3, sgn, kind] = FEET[f];
    const chain = [B[n0], B[n1], B[n2], B[n3]];
    // inverse kinematics: paw target -> wrist/hock -> elbow/knee
    const a = P[f + 'a'];
    const dMid = up.clone().multiplyScalar(-Math.cos(a)).addScaledVector(fwd, Math.sin(a));
    const T = B.Root.localToWorld(V(P[f + 'x'], P[f + 'y'], P[f + 'z']));
    const lens = kind === 'front' ? rig.L.F : rig.L.H;
    const W = T.clone().addScaledVector(dMid, -lens[2]);
    const A = chain[0].getWorldPosition(new THREE.Vector3());
    const pole = kind === 'front' ? fwd.clone().negate().addScaledVector(side, sgn * .15) : fwd.clone().addScaledVector(side, sgn * .1);
    const M = midJoint(A, W, lens[0], lens[1], pole);
    setDir(chain[0], rest[n1], M.clone().sub(A));
    setDir(chain[1], rest[n2], W.clone().sub(M));
    setDir(chain[2], rest[n3], dMid);
    const toe = fwd.clone().applyAxisAngle(side, P[f + 't']);
    setDir(chain[3], V(0, 0, 1), toe);
    // forward kinematics blend (lying poses): joint angles instead of a ground target
    const fk = P[f + 'fk'];
    if (fk > 0) {
      const ks = [P[f + 'k1'], P[f + 'k2'], P[f + 'k3'], P[f + 'k4']];
      chain.forEach((b, j) => { qe(ks[j], 0, j === 0 ? P[f + 'kz'] : 0, _q2); b.quaternion.slerp(_q2, fk); b.updateMatrixWorld(true); });
    }
  }
  const m = rig.face.morphTargetInfluences; m[0] = clamp(P.blink, 0, 1); m[1] = clamp(P.mouth, 0, 1);
}

// ------------------------------------------------------------------ gaits (foot placement with planted paws)
export function gaitParams(rig, kind) {
  const h = rig.hipH;
  const spec = {
    walk: { Tref: .8, Fr: .157, lambda: 1.88, duty: { F: .6, H: .55 }, off: { HL: 0, FL: .27, HR: .5, FR: .77 }, lift: { F: .32, H: .22 } },
    trot: { Tref: .42, Fr: .5, lambda: 1.87, duty: { F: .46, H: .43 }, off: { HL: 0, FR: .03, HR: .5, FL: .53 }, lift: { F: .42, H: .3 } },
    gallop: { Tref: .3, Fr: 2, lambda: 2.83, duty: { F: .32, H: .28 }, off: { HL: 0, HR: .08, FR: .42, FL: .52 }, lift: { F: .55, H: .45 } },
  }[kind];
  // cycle time from real cats (Tref at 0.25 m hip height), scaled by sqrt(leg length) as dynamic similarity says
  const T = spec.Tref * Math.sqrt(h / .25);
  // stride from the real-cat ratio, but never longer than these short toy legs can actually reach
  const crouch = { walk: .04, trot: .06, gallop: .12 }[kind];
  const reachF = rig.reachF + crouch * h * 1.2, reachH = rig.reachH + crouch * h;
  const S = Math.min(spec.lambda * h, 2 * .9 * reachF / spec.duty.F * (kind === 'gallop' ? 1.9 : 1), 2 * .9 * reachH / spec.duty.H * (kind === 'gallop' ? 1.9 : 1));
  return { ...spec, kind, S, T, v: S / T, crouch };
}
// returns { rootZ, feet: { FL: {x,y,z,a,t,c,u,stance}, ... } } at time t for gait params g
export function gaitFeet(rig, g, t) {
  const rootZ = g.v * t, out = { rootZ, feet: {} };
  for (const f of FOOT_KEYS) {
    const fr = f[0] === 'F', duty = g.duty[fr ? 'F' : 'H'], off = g.off[f], r = rig.restFoot[f];
    const x = t / g.T - off, m = Math.floor(x), c = x - m, reach = duty * g.S / 2;
    const zt = r.z + (m + off) * g.S + reach;
    let z, y = rig.ballH, a, toe, stance = c < duty, u;
    if (stance) {
      u = c / duty; z = zt;
      // stance: lands slightly reaching, yields under load, rolls over the toes (heel-off) before lift-off
      a = fr ? K([[0, .3], [.25, .1], [.75, -.3], [1, -.6]], u) : K([[0, .25], [.3, .05], [.8, -.55], [1, -.85]], u);
      toe = fr ? K([[0, -.05], [.8, 0], [1, -.15]], u) : K([[0, 0], [.85, 0], [1, -.2]], u);
      if (!fr) y += .5 * rig.d.pawR * clamp(-a - .1, 0, 1);         // rolling up onto the toes lifts the paw ball
    } else {
      u = (c - duty) / (1 - duty);
      z = zt + g.S * mj(u);
      // (galloping, a short-legged toy cat cannot tuck its legs as high as a real one: they would fold up into
      // its belly, so the tuck is kept lower and the wrist folds less)
      const tuck = g.kind === 'gallop' ? .4 : g.kind === 'trot' ? .85 : 1;
      y += tuck * g.lift[fr ? 'F' : 'H'] * g.S * .22 * Math.pow(Math.sin(Math.PI * Math.pow(u, .8)), 1.2);
      // swing: front paw folds back at the wrist, then reaches forward (E1) and flattens to land
      a = fr ? K([[0, -.6], [.3, -1.9 * tuck], [.72, .45], [1, .3]], u) : K([[0, -.85], [.35, -1.3 * tuck], [.8, .4], [1, .25]], u);
      toe = fr ? K([[0, -.15], [.25, 1.0], [.7, .4], [1, -.05]], u) : K([[0, -.2], [.3, .4], [.75, .2], [1, 0]], u);   // (toes curl only a little: more scrapes the floor)
    }
    // cats walk on a narrow track: paws step close to the midline
    const narrow = g.kind === 'walk' ? .72 : g.kind === 'trot' ? .8 : .85;
    // galloping, the hind paws land ahead of where the front paws were: on this short body they would meet,
    // so the hind track runs wide (wider still mid-swing) and they pass outside the front legs
    const wide = g.kind !== 'gallop' || fr ? 0 : .4 + (stance ? 0 : .3 * Math.sin(Math.PI * u));
    out.feet[f] = { x: r.x * (narrow + wide), y, z: z - rootZ, a, t: toe, c, u, stance };
  }
  // a hind paw passing its front paw (same side) steps round it: sideways clearance grows as they close in
  const pr = rig.d.pawR, clear = 3.2 * pr;
  for (const [F, H] of [['FL', 'HL'], ['FR', 'HR']]) {
    const fp = out.feet[F], hp = out.feet[H], close = Math.max(0, 1 - Math.abs(hp.z - fp.z) / clear);
    if (close > 0) hp.x = Math.sign(hp.x) * Math.max(Math.abs(hp.x), Math.abs(fp.x) + 2.1 * pr * mj(close));
  }
  return out;
}

function gaitPose(rig, g, t) {
  const P = stand(rig), gf = gaitFeet(rig, g, t), h = rig.hipH, ph = t / g.T;
  for (const f of FOOT_KEYS) { const q = gf.feet[f]; P[f + 'x'] = q.x; P[f + 'y'] = q.y; P[f + 'z'] = q.z; P[f + 'a'] = q.a; P[f + 't'] = q.t; }
  P.rootZ = gf.rootZ;
  const c = f => gf.feet[f].c, stanceBump = (f, w = .1) => { const q = gf.feet[f]; return q.stance ? bump(q.u, .25, .3) : 0; };
  if (g.kind === 'gallop') {
    // the spine drives the gallop: flexed (gathered) when the hind paws swing under the chest,
    // extended when the fronts reach and the hinds push back; two flights per stride
    const flex = Math.cos(TAU * (ph - .97));
    P.spPitch = .32 * flex; P.hipPitch = -.2 * flex + .05;
    P.hipY = -g.crouch * h + .05 * h * Math.cos(TAU * 2 * (ph - .35));
    P.hipZ = .015 * flex;
    P.nkPitch = -.1 - .25 * flex; P.hdPitch = .05 + .03 * Math.sin(TAU * ph);
    P.hipRoll = .03 * Math.sin(TAU * ph);
    P.scapL = -.25 + .5 * frac(ph - .52 + .5); P.scapR = -.25 + .5 * frac(ph - .42 + .5);
    P.tailBase = -.25; P.tailBend = -.02; P.tailWave = .12; P.tailWph = ph * 2;
    P.earLp = P.earRp = -.25;
  } else {
    const trot = g.kind === 'trot';
    // body sinks a little as each hind paw takes weight (stance yield), twice per stride; same for the chest
    const dipH = (trot ? .03 : .018) * h, dipF = (trot ? .025 : .014) * h;
    P.hipY = -g.crouch * h - dipH * (stanceBump('HL') + stanceBump('HR'));
    const chestDrop = -g.crouch * h * .7 - dipF * (stanceBump('FL') + stanceBump('FR'));
    P.spPitch = -(chestDrop - P.hipY) / rig.torso;              // positive = chest lower than hips
    P.hipRoll = (trot ? .02 : .035) * Math.sin(TAU * (ph - .1));
    P.hipYaw = (trot ? .03 : .06) * Math.sin(TAU * ph);
    P.spYaw = -1.5 * P.hipYaw;                                 // shoulders swing against the hips
    P.nkPitch = trot ? .12 : .08; P.nkYaw = -.3 * P.spYaw;
    P.hdPitch = .02 + .015 * Math.sin(TAU * 2 * (ph - .3));
    // shoulder blades: rotate back with the leg during stance and ride up while the leg carries weight
    for (const [f, k] of [['FL', 'L'], ['FR', 'R']]) {
      const q = gf.feet[f];
      P['scap' + k] = q.stance ? lerp(-.18, .22, q.u) : lerp(.22, -.18, mj(q.u));
      P['scap' + k + 'y'] = q.stance ? .012 * h * Math.sin(Math.PI * q.u) : 0;
    }
    // tail carried high with a soft question-mark tip on the walk, level on the trot
    P.tailBase = trot ? .25 : 1.0; P.tailBend = trot ? .02 : .06; P.tailTip = trot ? 0 : .22;
    P.tailWave = trot ? .1 : .14; P.tailWph = ph;
    P.earLp = P.earRp = .08;
  }
  P.breath = .3 * Math.sin(TAU * ph);
  return P;
}

// ------------------------------------------------------------------ key poses
// Sitting the way cats really sit: the rump rests ON the ground, the hind legs fold forward along the body with
// the whole hind foot (heel to toes) flat on the floor, and the front legs stand straight under the shoulders.
// Solved per breed: the hips are lowered onto the rump, then the body is tilted until the shoulders sit exactly
// at front-leg height (a Munchkin sits low and flat, a Savannah tall and upright).
const _w = new THREE.Vector3();
function worldOf(rig, name) { return rig.B[name].getWorldPosition(_w).clone(); }
export function fitSit(rig, P, opts = {}) {
  const d = rig.d, hipsTarget = d.rumpR * (opts.rump ?? .92), shoulderTarget = d.joints.UpperArm_L.y * (opts.shoulder ?? .97);
  P.hipY = hipsTarget - rig.J.Hips.y;
  P.spPitch = opts.spPitch ?? -.12;
  let pitch = P.hipPitch || -.6;
  for (let it = 0; it < 10; it++) {
    P.hipPitch = pitch; applyPose(rig, P);
    const sh = worldOf(rig, 'UpperArm_L'), hp = worldOf(rig, 'Hips'), arm = sh.clone().sub(hp).length();
    pitch -= (shoulderTarget - sh.y) / Math.max(.05, arm * Math.cos(pitch)) * .8;
    pitch = clamp(pitch, -1.25, 0);
  }
  P.hipPitch = pitch; applyPose(rig, P);
  // neck carries the head back over the chest; the head itself is authored level in the root frame
  P.nkPitch = -(pitch + P.spPitch) * .85;
  const shL = worldOf(rig, 'UpperArm_L'), shR = worldOf(rig, 'UpperArm_R'), hp = worldOf(rig, 'Hips');
  for (const [f, sh] of [['FL', shL], ['FR', shR]]) { P[f + 'z'] = sh.z + .006; P[f + 'x'] = sh.x * .85; P[f + 'y'] = rig.ballH; P[f + 'a'] = .1; }
  for (const f of ['HL', 'HR']) { P[f + 'z'] = hp.z + d.len.lMt * 1.05; P[f + 'x'] = rig.restFoot[f].x * 1.3; P[f + 'y'] = rig.ballH; P[f + 'a'] = 1.52; }
  return P;
}
// ------------------------------------------------------------------ settling a held posture on the real mesh
// The skinned mesh is measured (catcontact.js), not the bones: the body is lifted until nothing but fur-deep
// contact reaches below the floor, the tail is raised off it, and planted paws that sink into the body are
// moved out along the surface normal. Used on every held posture (sit, loaf, sleep, lying) at build time.
export function contactOf(rig) {
  if (!rig.contact) {
    const C = makeContact(rig);
    C.sets = {
      trunk: C.points((p, i, m) => p === 'torso' || p.endsWith('u'), 'body', 2),
      head: C.points(p => p === 'head', 'body', 2),
      tail: C.points(p => p === 'tail', 'body', 2),
      ...Object.fromEntries(LIMBS.map(L => [L, C.points(p => p === L, 'body', 2)])),
    };
    rig.contact = C;
  }
  return rig.contact;
}
export function settle(rig, P, { fk = [], floor = .002, iters = 12 } = {}) {
  const C = contactOf(rig);
  for (let it = 0; it < iters; it++) {
    applyPose(rig, P); C.update();
    let moved = false;
    // body: lift the hips so the lowest trunk point (and any limb posed by joint angles) is at -floor
    let low = C.lowest(C.sets.trunk);
    for (const L of fk) low = Math.min(low, C.lowest(C.sets[L]));
    if (low < -floor - .001) { P.hipY += -floor - low; moved = true; }
    // head: lying on its side the big head would sink into the floor; lift it on the neck (whichever small
    // neck/head turn raises it most)
    const hl = C.lowest(C.sets.head);
    if (hl < -floor - .001) {
      let best = null, bestY = hl;
      for (const [k, dv] of [['nkRoll', .06], ['nkRoll', -.06], ['hdRoll', .06], ['hdRoll', -.06], ['nkPitch', -.06]]) {
        const Q = { ...P, [k]: P[k] + dv }; applyPose(rig, Q); C.update();
        const y = C.lowest(C.sets.head); if (y > bestY + .0005) { bestY = y; best = [k, dv]; }
      }
      if (best) { P[best[0]] += best[1]; moved = true; } else if (hl < -.01) { P.hipY += -floor - hl; moved = true; }
      applyPose(rig, P); C.update();
    }
    // tail: raise its root until it clears the floor
    // (with a few mm to spare: the idle tail sway dips it a little)
    if (C.lowest(C.sets.tail) < .003 && P.tailBase < 1.2) { P.tailBase += .04; moved = true; }
    // planted paws pressed into the body: step them out along the body's surface normal (sideways only)
    for (const L of LIMBS) {
      if (fk.includes(L) || P[L + 'fk'] > .5) continue;
      const pl = C.lowest(C.sets[L]);                              // a folded paw tucked under the floor
      if (pl < -floor - .001) { P[L + 'y'] += -floor - pl; moved = true; }
      const r = C.depth(C.sets[L], p => p !== L && p !== L + 'u');
      if (r.d < -.004) {
        const pt = C.M.body.pos, i = r.i, n = C.nearest(pt[3 * i], pt[3 * i + 1], pt[3 * i + 2], p => p === r.part, .06).n;
        const h = Math.hypot(n.x, n.z) || 1, step = Math.min(.02, -r.d + .002);
        P[L + 'x'] += n.x / h * step; P[L + 'z'] += n.z / h * step; moved = true;
      }
    }
    if (!moved) break;
  }
  applyPose(rig, P);
  return P;
}
// Bring vertex set `a` (the tongue, a paw...) into touch with the surface of parts `b` - optionally only the
// patch within zone.r of a point on a bone - by adjusting pose `keys`. Each round finds the nearest
// vertex/surface pair on the skinned meshes, then a damped least-squares step (finite differences) closes
// that pair to `gap` along the surface normal (negative gap: pressed into the fur). `at` turns the base pose
// into the pose at the moment of contact (e.g. the tongue out at mid-lick).
export function touch(rig, P, { a, b, zone = null, keys, bounds = {}, gap = .001, iters = 30, at = Q => Q, lim = .12 }) {
  const C = contactOf(rig);
  const A = a === 'tongue' ? (C.sets.tongue ||= C.points(p => p === 'tongue', 'face', 2))
    : C.sets['all' + a] ||= C.points(p => p === a, 'body', 2);
  const pa = new THREE.Vector3(), pb = new THREE.Vector3(), tmp = new THREE.Vector3();
  let res = { d: Infinity }, bestP = { ...P }, bestErr = Infinity, step = lim;
  const pose = Q => { applyPose(rig, at({ ...Q })); };
  for (let it = 0; it <= iters; it++) {
    pose(P); C.update();
    const zc = zone ? rig.B[zone.bone].localToWorld(zone.off.clone()) : null;
    // closest vertex pair first (brute force over the target patch), then the exact surface point near it
    const bp = C.M.body.pos, bPart = C.M.body.part, Bv = [];
    for (let v = 0; v < C.M.body.n; v++) if (b.includes(bPart[v]) && (!zc || Math.hypot(bp[3 * v] - zc.x, bp[3 * v + 1] - zc.y, bp[3 * v + 2] - zc.z) < zone.r)) Bv.push(v);
    const pos = C.M[A.mesh].pos; let pi = -1, pd = Infinity;
    for (const i of A.ids) for (const v of Bv) {
      const dd = (pos[3 * i] - bp[3 * v]) ** 2 + (pos[3 * i + 1] - bp[3 * v + 1]) ** 2 + (pos[3 * i + 2] - bp[3 * v + 2]) ** 2;
      if (dd < pd) { pd = dd; pi = i; }
    }
    const ok = (part, t) => b.includes(part) && (!zc || C.triCenter(t).distanceTo(zc) < zone.r * 1.2);
    const r0 = pi < 0 ? null : C.nearest(pos[3 * pi], pos[3 * pi + 1], pos[3 * pi + 2], ok, Math.sqrt(pd) + .02);
    const best = r0 && { ...r0, i: pi };
    if (!best) break;
    // backtracking: a step that made things worse is undone and the step size halved
    const err = Math.abs(best.d - gap);
    if (err > bestErr + 1e-4) { Object.assign(P, bestP); step /= 2; if (step < .005) break; continue; }
    bestErr = err; bestP = { ...P }; step = Math.min(lim, step * 1.5);
    res = { d: best.d, P };
    if (Math.abs(best.d - gap) < .0008 || it === iters) break;
    const tv = C.triVerts(best.t), n = best.n.clone();
    const f = Q => {
      pose(Q); C.refresh();
      C.skin1(A.mesh, best.i, pa); pb.set(0, 0, 0);
      tv.forEach((v, k) => pb.addScaledVector(C.skin1('body', v, tmp), best.bary.getComponent(k)));
      return pa.clone().sub(pb);
    };
    const f0 = f(P), want = n.clone().multiplyScalar(gap), e = want.sub(f0);
    const Jc = keys.map(k => f({ ...P, [k]: P[k] + .01 }).sub(f0).multiplyScalar(100));
    const m = keys.length, M2 = Array.from({ length: m }, (_, i) => Array.from({ length: m }, (_, j) => Jc[i].dot(Jc[j]) + (i === j ? 1e-5 : 0))), bv = Jc.map(j => j.dot(e));
    for (let i = 0; i < m; i++) for (let k = i + 1; k < m; k++) { const g = M2[k][i] / M2[i][i]; for (let j = i; j < m; j++) M2[k][j] -= g * M2[i][j]; bv[k] -= g * bv[i]; }
    const x = new Array(m).fill(0);
    for (let i = m - 1; i >= 0; i--) { let sum = bv[i]; for (let j = i + 1; j < m; j++) sum -= M2[i][j] * x[j]; x[i] = sum / M2[i][i]; }
    keys.forEach((k, i) => { P[k] += clamp(x[i], -step, step); if (bounds[k]) P[k] = clamp(P[k], bounds[k][0], bounds[k][1]); });
  }
  Object.assign(P, bestP); applyPose(rig, P);
  touch.last = bestErr + gap;
  return P;
}
// touch() from several starting poses; keeps the closest
export function touchBest(rig, P, opts, starts = []) {
  let best = null, bestErr = Infinity;
  for (const s0 of [{}, ...starts]) {
    const Q = touch(rig, { ...P, ...s0 }, opts), e = Math.abs(touch.last - (opts.gap ?? .001));
    if (e < bestErr) { bestErr = e; best = Q; }
  }
  Object.assign(P, best); applyPose(rig, P); touch.last = bestErr + (opts.gap ?? .001);
  return P;
}
// settle a moving clip: settle n+1 sampled frames and blend the corrections in between (cheap at play time)
export function withSettle(rig, pose, dur, opts = {}, n = 12) {
  const keys = new Set(), deltas = [];
  for (let i = 0; i <= n; i++) {
    const P = pose(dur * i / n), Q = settle(rig, { ...P }, opts), dl = {};
    for (const k in Q) if (typeof Q[k] === 'number' && Math.abs(Q[k] - P[k]) > 1e-6) { dl[k] = Q[k] - P[k]; keys.add(k); }
    deltas.push(dl);
  }
  return t => {
    const P = pose(t), f = clamp(t / dur, 0, 1) * n, i = Math.min(n - 1, Math.floor(f)), u = f - i;
    for (const k of keys) P[k] += lerp(deltas[i][k] || 0, deltas[i + 1][k] || 0, u);
    return P;
  };
}
function sitPose(rig) {
  const P = stand(rig);
  Object.assign(P, { hdPitch: .02, scapL: -.05, scapR: -.05, tailBase: -1.25, tailBend: .02, tailCurl: .3, tailYaw: .45 });
  return settle(rig, fitSit(rig, P));
}
function loafPose(rig) {
  const P = stand(rig), h = rig.hipH, chestR = rig.chestY - rig.ballH;
  Object.assign(P, {
    hipY: -.56 * rig.hipY, spPitch: .02, nkPitch: .12, hdPitch: .04, breath: 0,
    tailBase: -1.0, tailBend: .1, tailCurl: .22, tailYaw: .55,
  });
  for (const f of ['FL', 'FR']) { P[f + 'a'] = -1.62; P[f + 'y'] = rig.ballH * .9; P[f + 'z'] -= .05 * h; P[f + 't'] = .2; }
  for (const f of ['HL', 'HR']) { P[f + 'a'] = 1.5; P[f + 'y'] = rig.ballH * .9; P[f + 'z'] += .25 * h; P[f + 'x'] *= 1.15; }
  return P;
}
function sleepPose(rig) {
  const P = loafPose(rig);
  Object.assign(P, {
    // curled into a doughnut towards its left side: chin down on the paws, tail wrapped round to the nose
    hipYaw: -.38, hipRoll: .16, spYaw: .95, spRoll: -.12, nkPitch: .55, nkYaw: .4, hdPitch: .3, hdYaw: .95, hdRoll: -.3,
    tailBase: -1.15, tailYaw: .6, tailCurl: .4, tailBend: .015, blink: 1,
    earLp: -.25, earRp: -.25, earLr: .1, earRr: .1,
  });
  return P;
}

// ------------------------------------------------------------------ tongue cycles
// One grooming lick (u 0..1, ~0.45 s): the jaw opens first, the tongue reaches out and down with the tip
// curled slightly under, lands on the fur and spreads flat, then drags UP and back over the fur while the head
// lifts (the stroke) and the tip turns up; it snaps back in and the jaw closes a beat later.
export function lickCycle(u) {
  return {
    mouth: K([[0, .06], [.1, .42], [.62, .42], [.8, .1], [1, .06]], u),
    tongue: K([[0, 0], [.08, .12], [.3, .95], [.6, .82], [.76, .08], [1, 0]], u),
    tongueBend: K([[0, .2], [.3, .95], [.42, .75], [.62, -.15], [.78, .1], [1, .2]], u),
    tongueCurl: K([[0, .15], [.24, .45], [.34, .2], [.6, -.45], [.72, -.55], [.88, -.1], [1, .15]], u),
    tongueSpread: K([[0, 0], [.3, 0], [.38, 1], [.58, .75], [.7, 0], [1, 0]], u),
    hdPitch: K([[0, 0], [.3, .07], [.4, .09], [.66, -.11], [.86, -.03], [1, 0]], u),
  };
}
// One lap when drinking (u 0..1 at ~3.5 Hz; Reis et al. 2010): the tongue reaches down with its tip folded
// back under (J), touches the surface with the top of the tip only, then is pulled up much faster than it went
// down; the jaw closes on the liquid column just after the tongue is in.
export function lapCycle(u) {
  return {
    mouth: K([[0, .12], [.14, .55], [.56, .55], [.7, .04], [1, .12]], u),
    tongue: K([[0, .05], [.34, 1], [.42, 1], [.6, .05], [1, .05]], u),
    tongueBend: K([[0, .45], [.34, 1.35], [.42, 1.35], [.6, .45], [1, .45]], u),
    tongueCurl: K([[0, .15], [.3, 1.15], [.44, 1.2], [.6, .3], [1, .15]], u),
    tongueSpread: K([[0, 0], [.33, 0], [.38, .6], [.45, .3], [.52, 0], [1, 0]], u),
    hdPitch: K([[0, 0], [.36, .025], [.5, -.02], [.7, -.03], [1, 0]], u),
  };
}
// A lip lick (u 0..1, ~0.8 s): tip scooped up, the tongue sweeps across the upper lip and nose from one side
// to the other and back in - what cats do after eating or drinking.
export function lipLick(u) {
  return {
    mouth: K([[0, 0], [.12, .36], [.78, .36], [.92, .02], [1, 0]], u),
    tongue: K([[0, 0], [.15, .86], [.72, .86], [.86, 0], [1, 0]], u),
    tongueBend: -.25, tongueCurl: K([[0, -.2], [.2, -.75], [.7, -.75], [1, -.2]], u), tongueSpread: .35,
    tongueYaw: K([[0, .7], [.2, .7], [.5, -.75], [.62, -.75], [.72, 0], [1, 0]], u),
    hdPitch: K([[0, 0], [.3, -.05], [.7, -.05], [1, 0]], u),
  };
}
const addTongue = (P, c, k = 1) => { for (const key in c) P[key] = key === 'hdPitch' ? P[key] + c[key] * k : lerp(P[key], c[key], k); return P; };

// ------------------------------------------------------------------ clips
// A grooming bout for the game's behaviour layer: run these in order (head to tail, as cats do), picking how
// long to stay in each by `share` - the fraction of oral grooming real cats spend on that region (Eckstein &
// Hart 2000; the anogenital 10 % happens in the leg-up pose and is folded into GroomLeg). `from` is the
// posture the clip starts and ends in (play SitDown / LieDown first). The last two are scratching and claw
// care, not licking, and are sprinkled in rather than weighted.
export const GROOM_ROUTINE = [
  { clip: 'GroomFace', region: 'face (paw wash)', share: .22, from: 'Sit' },
  { clip: 'GroomEar', region: 'ears', share: .09, from: 'Sit' },
  { clip: 'GroomChest', region: 'neck / chest', share: .11, from: 'Sit' },
  { clip: 'GroomFlank', region: 'shoulder / side', share: .13, from: 'Sit' },
  { clip: 'GroomLeg', region: 'hind legs (+ anogenital)', share: .31, from: 'Loaf' },
  { clip: 'GroomBelly', region: 'belly', share: .09, from: 'Loaf' },
  { clip: 'GroomTail', region: 'tail', share: .05, from: 'Sit' },
  { clip: 'ScratchEar', region: 'ear (hind-foot scratch)', share: 0, from: 'Sit' },
  { clip: 'NibbleClaws', region: 'front claws', share: 0, from: 'Sit' },
];
export function makeClips(rig) {
  const h = rig.hipH, P0 = stand(rig), clips = [];
  // looping clips are stretched to a whole number of frames so the last frame meets the first exactly
  const add = (name, dur, loop, pose, extra = {}) => {
    const frames = Math.max(2, Math.round(dur * FPS)), d2 = frames / FPS, f = dur / d2;
    const clip = { name, dur: d2, loop, pose: f === 1 ? pose : t => pose(t * f), ...extra };
    if (clip.speed) { clip.speed /= f; clip.cycle = clip.cycle / f; }
    clips.push(clip);
  };

  // Idle: breathing, blinks, an ear twitch, a look around, slow tail swish, weight shift
  add('Idle', 6, true, t => over(P0, {
    breath: Math.sin(TAU * t / 2), blink: blinkAt(t, [1.1, 4.3]),
    hdYaw: K([[0, 0], [1.6, 0], [2.1, .45], [3.2, .45], [3.7, -.15], [4.6, -.15], [5.2, 0], [6, 0]], t),
    hdPitch: K([[0, 0], [2.1, -.08], [3.2, -.05], [3.7, .05], [5.2, 0], [6, 0]], t),
    nkYaw: K([[0, 0], [2.1, .12], [3.2, .12], [3.7, -.05], [5.2, 0], [6, 0]], t),
    earLp: K([[0, 0], [2.6, 0], [2.7, -.5], [2.95, .05], [3.1, 0], [6, 0]], t), earLy: K([[0, 0], [2.6, 0], [2.7, -.4], [3, 0], [6, 0]], t),
    earRp: .05 * Math.sin(TAU * t / 3),
    hipX: .006 * Math.sin(TAU * t / 6), hipRoll: .02 * Math.sin(TAU * t / 6),
    tailBase: -.3, tailBend: .1, tailTip: .15, tailWave: .22, tailWph: t / 3,
  }));
  // locomotion (root motion: the clip moves the cat forward; paws stay planted on the ground)
  for (const [name, kind, cycles] of [['Walk', 'walk', 2], ['Trot', 'trot', 2], ['Gallop', 'gallop', 3]]) {
    const g = gaitParams(rig, kind);
    add(name, g.T * cycles, true, t => {
      const P = gaitPose(rig, g, t);
      P.blink = kind === 'walk' ? blinkAt(t, [g.T * 1.4]) : 0;
      return P;
    }, { rootMotion: true, speed: g.v, stride: g.S, cycle: g.T });
  }
  // JumpUp: crouch, bum wiggle, launch, ballistic flight, front paws land first, absorb, stand
  {
    const D = .78, Hup = .2, tLift = 1.0, tPush = 1.1;
    const apex = Hup + .1, vy = Math.sqrt(2 * G * apex), Tf = vy / G + Math.sqrt(2 * (apex - Hup) / G);
    const tLand = tPush + Tf, dur = tLand + .9;
    add('JumpUp', dur, false, withSettle(rig, t => {
      const P = { ...P0 };
      const crouchA = ss(seg(t, 0, .35)) * (1 - ss(seg(t, .95, tPush)));            // before take-off
      const crouchB = .75 * ss(seg(t, tLand - .02, tLand + .14)) * (1 - ss(seg(t, tLand + .3, tLand + .75)));
      const crouch = Math.max(crouchA, crouchB);
      const fly = seg(t, tPush, tLand);
      // root follows the ballistic path of the body; paws are planted in world space before and after
      const tau = clamp(t - tPush, 0, Tf);
      P.rootZ = t < tPush ? 0 : D * Math.min(1, tau / Tf);
      P.rootY = t < tPush ? 0 : t < tLand ? vy * tau - G * tau * tau / 2 : Hup;
      // push-off: the body extends upward from the crouch just before leaving the ground
      const push = ss(seg(t, .92, tPush)) * (1 - ss(seg(t, tPush, tPush + .08)));
      P.hipY = -.2 * h * crouch + .1 * h * push;          // (a shallow crouch: deeper, the short forelegs press up into the chest)
      P.hipPitch = t < tPush ? lerp(.05 * crouch, -.45, ss(seg(t, .92, tPush))) : t < tLand ? lerp(-.45, .35, ss(fly)) : lerp(.35, 0, ss(seg(t, tLand, tLand + .5)));
      P.spPitch = t < tPush ? .08 * crouch - .25 * ss(seg(t, .95, tPush)) : t < tLand ? K([[0, -.25], [.45, .12], [1, .2]], fly) : lerp(.2, 0, ss(seg(t, tLand, tLand + .55)));
      const wig = Math.sin(TAU * 5 * t) * ss(seg(t, .4, .5)) * (1 - ss(seg(t, .82, .9)));
      P.hipYaw = .1 * wig; P.hipRoll = .05 * wig;                                       // the famous bum wiggle
      P.nkPitch = -.1 * crouch - P.hipPitch * .5; P.hdPitch = -.1 * (1 - fly) + .12 * ss(seg(t, tLand - .15, tLand + .1)) * (1 - ss(seg(t, tLand + .2, tLand + .6)));
      P.tailBase = t < tPush ? -.75 + .5 * crouch : t < tLand ? lerp(-.75, .2, Math.sin(Math.PI * fly)) : lerp(-.4, -.3, ss(seg(t, tLand, dur)));
      P.tailBend = .03; P.tailWave = t < tPush ? .35 * ss(seg(t, .3, .5)) : .05; P.tailWph = t * 3;
      P.earLp = P.earRp = t < tPush ? .15 : -.2 * Math.sin(Math.PI * fly);
      P.blink = blinkAt(t, [tLand + .05], .2) * .7;
      // paws: planted on the floor (world) until lift-off, then fly, then planted on the box top (world)
      for (const f of FOOT_KEYS) {
        const fr = f[0] === 'F', r = rig.restFoot[f], off = fr ? .04 : 0;
        const liftT = fr ? tLift : tPush, touchT = fr ? tLand : tLand + .07;
        let wz, wy, a = fr ? .3 : .2, toe = 0;
        const tread = !fr ? .015 * Math.max(0, Math.sin(TAU * 5 * t + (f === 'HL' ? 0 : Math.PI))) * (wig ? 1 : 0) : 0;
        if (t < liftT) { wz = r.z + (fr ? .12 * crouch : 0); wy = rig.ballH + tread; if (!fr) a = .2 - .5 * crouch; }
        else if (t >= touchT) { wz = r.z + D + (fr ? .05 * crouchB : 0); wy = Hup + rig.ballH; if (!fr) a = .2 - .5 * crouchB; else a = .3 - .2 * crouchB; }
        else {
          const u = (t - liftT) / (touchT - liftT), body = { z: P.rootZ, y: P.rootY };
          // fronts tuck to the chest then reach down for the landing; hinds trail stretched, then tuck under
          const relZ = fr ? K([[0, r.z + .02], [.35, r.z + .08], [.8, r.z + .14], [1, r.z]], u) : K([[0, r.z], [.25, r.z - .14], [.6, r.z - .06], [1, r.z]], u);
          const relY = fr ? K([[0, rig.ballH], [.35, .5 * rig.hipY], [.8, .2 * rig.hipY], [1, rig.ballH]], u) : K([[0, rig.ballH], [.3, .2 * rig.hipY], [.7, .25 * rig.hipY], [1, rig.ballH]], u);
          const landing = ss(seg(u, .75, 1));
          wz = lerp(body.z + relZ, r.z + D, landing); wy = lerp(body.y + relY, Hup + rig.ballH, landing);
          a = fr ? K([[0, .3], [.3, -1.6], [.8, .4], [1, .3]], u) : K([[0, -.9], [.4, -1.2], [.85, .5], [1, .2]], u);
          toe = fr ? K([[0, 0], [.3, 1.1], [.8, .2], [1, 0]], u) : K([[0, -.2], [.4, .7], [1, 0]], u);
        }
        P[f + 'x'] = r.x * (fr ? 1 : 1 + .25 * crouch); P[f + 'y'] = wy - P.rootY;   // crouching, the hind paws set wider beside the haunches P[f + 'z'] = wz - P.rootZ; P[f + 'a'] = a; P[f + 't'] = toe;
      }
      return P;
    }, dur, {}, 30), { rootMotion: true, jump: { D, H: Hup } });
  }
  // sitting down, sitting, standing up
  const SIT = sitPose(rig), LOAF = settle(rig, loafPose(rig), { floor: -.002 }), SLEEP = settle(rig, sleepPose(rig));
  // move between two postures; `lifts` raises the named paws in an arc, `splay` swings them out sideways on the
  // way (so a hind paw folding under the haunch passes beside it, not through it)
  const transfer = (A, B, k, lifts = {}, splay = {}) => {
    const P = mix(A, B, ss(k)), arc = Math.sin(Math.PI * ss(k));
    for (const [f, hgt] of Object.entries(lifts)) P[f + 'y'] += hgt * arc;
    for (const [f, w] of Object.entries(splay)) P[f + 'x'] += Math.sign(rig.restFoot[f].x) * w * rig.hipH * arc;
    return P;
  };
  add('SitDown', .9, false, t => over(transfer(P0, SIT, t / .9, { HL: .03, HR: .03 }, { HL: .3, HR: .3 }), { blink: blinkAt(t, [.75]) }));
  add('Sit', 4, true, t => over(SIT, {
    breath: Math.sin(TAU * t / 2), blink: blinkAt(t, [1.4, 3.3]), tailWave: .05, tailWph: t / 2,
    hdYaw: K([[0, 0], [1.8, 0], [2.2, .3], [3.2, .3], [3.6, 0], [4, 0]], t), earRp: K([[0, 0], [2.5, 0], [2.6, -.4], [2.85, 0], [4, 0]], t),
  }));
  add('StandUp', .7, false, t => transfer(SIT, P0, t / .7, { HL: .03, HR: .03 }, { HL: .3, HR: .3 }));
  // lying down into a loaf, loafing, curling up to sleep
  add('LieDown', 1.1, false, t => over(transfer(P0, LOAF, t / 1.1, { FL: .04, FR: .04 }, { FL: .25, FR: .25 }), { blink: blinkAt(t, [.9]) }));
  add('Loaf', 5, true, t => over(LOAF, { breath: Math.sin(TAU * t / 2.5), blink: .55 + .45 * blinkAt(t, [2]), tailWave: .04, tailWph: t / 2.5 }));
  add('Sleep', 5, true, t => over(SLEEP, { breath: 1.3 * Math.sin(TAU * t / 2.5), tailTip: .05 * Math.sin(TAU * t / 5), earLp: -.25 + .25 * bump(t, 3.2, .06) }));
  add('FallAsleep', 2, false, t => over(transfer(LOAF, SLEEP, t / 2, { FL: .02, FR: .02 }, { FL: .2, FR: .2 }), { blink: Math.max(.55, ss(seg(t, .6, 1.6))) }));
  // ---------------------------------------------------------------- grooming
  // Cats groom sitting or lying, never standing, and a grooming bout runs head to tail (cephalocaudal):
  // lick the paw and wash the face and ears with it, then chest, shoulders and flanks, hind legs (the leg-up
  // "cello" pose), belly, tail; scratching an ear with a hind foot and nibbling the claws are part of it too.
  // Share of oral grooming by region: face 31 %, hind legs 21 %, sides/back 13 %, neck/chest 11 %,
  // anogenital 10 %, belly 9 %, tail 5 % (Eckstein & Hart 2000, Appl. Anim. Behav. Sci. 68:131).
  // Each clip starts and ends in its base posture (sit or lie), so the game can chain them in that order.
  const HS = rig.d.HS, d = rig.d;
  const headLocal = v => v.clone().add(d.Hc).sub(d.joints.Head);                  // head-space point -> Head bone local
  const mouthLocal = d.mouth.clone().sub(d.joints.Head);
  const atHead = (P, off) => { applyPose(rig, P); return rig.B.Head.localToWorld(headLocal(off)); };
  const mouthAt = P => { applyPose(rig, P); return rig.B.Head.localToWorld(mouthLocal.clone()); };
  // bring a head-space spot (default: the mouth) to `target` (world) by adjusting pose channels
  // (damped least squares on finite differences); returns the remaining distance
  const spotAt = (P, off) => { applyPose(rig, P); return off ? rig.B.Head.localToWorld(headLocal(off)) : rig.B.Head.localToWorld(mouthLocal.clone()); };
  function reach(P, target, keys, iters = 10, off = null, lim = .3, bounds = {}) {
    let err = 0;
    for (let it = 0; it < iters; it++) {
      const m0 = spotAt(P, off), e = target.clone().sub(m0); err = e.length(); if (err < .0015) break;
      const Jc = keys.map(k => spotAt({ ...P, [k]: P[k] + .02 }, off).sub(m0).multiplyScalar(1 / .02));
      const n = keys.length, A = Array.from({ length: n }, (_, i) => Array.from({ length: n }, (_, j) => Jc[i].dot(Jc[j]) + (i === j ? 2e-4 : 0))), bv = Jc.map(j => j.dot(e));
      for (let i = 0; i < n; i++) for (let k = i + 1; k < n; k++) { const f = A[k][i] / A[i][i]; for (let j = i; j < n; j++) A[k][j] -= f * A[i][j]; bv[k] -= f * bv[i]; }
      const x = new Array(n).fill(0);
      for (let i = n - 1; i >= 0; i--) { let sum = bv[i]; for (let j = i + 1; j < n; j++) sum -= A[i][j] * x[j]; x[i] = sum / A[i][i]; }
      keys.forEach((k, i) => { P[k] += clamp(x[i], -lim, lim); if (bounds[k]) P[k] = clamp(P[k], bounds[k][0], bounds[k][1]); });
    }
    reach.last = target.distanceTo(spotAt(P, off));
    return P;
  }
  // multi-start: the head can come at a spot from either side, so try several starting guesses, keep the best
  function reachBest(P, target, keys, starts, off = null, bounds = {}) {
    let best = null, bestErr = Infinity;
    for (const s0 of [{}, ...starts]) {
      const Q = reach({ ...P, ...s0 }, target, keys, 30, off, .25, bounds);
      if (reach.last < bestErr - 1e-4) { bestErr = reach.last; best = Q; }
    }
    Object.assign(P, best); reach.last = bestErr;
    return P;
  }
  const report = {};
  // a point on the coat: bone-local offset, pushed out along its own direction by `stand` (tongue reach)
  const surf = (P, bone, off, standoff) => { applyPose(rig, P); const c = rig.B[bone].getWorldPosition(new THREE.Vector3()), p = rig.B[bone].localToWorld(off.clone()); return p.add(p.clone().sub(c).normalize().multiplyScalar(standoff)); };
  // with the big toy head the face cannot touch the body without passing into it: the mouth stops a head's
  // depth away and the extended tongue bridges the rest (how stylised games stage contact)
  const reachTongue = .3 * HS, farStand = .3 * HS + .35 * HS;
  // a run of licks on a solved base pose; `stroke(P, k)` moves the head along the fur for stroke amount k
  const licking = (P, t, t0, n, rate, stroke) => {
    const u = (t - t0) * rate, i = Math.floor(u);
    if (u < 0 || i >= n) return P;
    const c = lickCycle(u - i); const k = c.hdPitch / .1;        // -1 .. +1 over the stroke
    delete c.hdPitch; addTongue(P, c); stroke(P, k);
    return P;
  };
  const SITG = over(SIT, { tailWave: .05 });
  // the pose at the moment the tongue is on the fur (mid-lick), for solving contact
  const lickAt = (stroke, u = .45) => Q => { const c = lickCycle(u), k = c.hdPitch / .1; delete c.hdPitch; addTongue(Q, c); stroke(Q, k); return Q; };

  // The toy head is as big as the body, so a head turned more than ~70 degrees or rolled flat hides the face and
  // wrings the neck. Every solve keeps the head inside these limits; where a real cat would bury its face in
  // its fur, this one brings the part up to the face instead (leg raised high, tail lifted, paw to the cheek)
  // and the tongue bridges the rest.
  const HB = { hdPitch: [-.3, 1.2], hdYaw: [-1.25, 1.25], hdRoll: [-.7, .7], nkPitch: [-.5, 1.3], nkYaw: [-.7, .7], nkRoll: [-.5, .5], spPitch: [-.35, 1], spYaw: [-.5, .5] };
  const solve = (P, target, keys, starts = [], off = null) => reachBest(P, target, keys, starts, off, HB);
  const BODYK = ['spPitch', 'spYaw', 'nkPitch', 'nkYaw', 'hdPitch', 'hdYaw', 'hdRoll'];
  const bodyStarts = [{ nkPitch: .6, hdPitch: .8, hdYaw: .6, nkYaw: .4 }, { nkPitch: .6, hdPitch: .8, hdYaw: -.6, nkYaw: -.4 }, { spPitch: .5, nkPitch: 1, hdPitch: .9, hdYaw: 1, nkYaw: .6, spYaw: .3 }];
  // front paw: the furthest it can go from the shoulder (a paw target out of reach would straighten the arm
  // and leave the paw short of where the head is solved to meet it)
  const armReach = (rig.L.F[0] + rig.L.F[1] + rig.L.F[2]) * .9;
  const pawClamp = (P, p) => { applyPose(rig, P); const sh = worldOf(rig, 'UpperArm_L'), v = p.clone().sub(sh); return v.length() > armReach ? sh.add(v.setLength(armReach)) : p.clone(); };
  const setPaw = (P, p, up, a, toe) => { const g2 = V(P.FLx, P.FLy, P.FLz).lerp(p, up); P.FLx = g2.x; P.FLy = g2.y; P.FLz = g2.z; P.FLa = lerp(P.FLa, a, up); P.FLt = lerp(0, toe, up); };
  const HEADK = ['nkPitch', 'nkYaw', 'nkRoll', 'hdPitch', 'hdYaw', 'hdRoll'];
  // a paw touching a face spot: its centre sits a paw's radius out from the fur
  const pawOut = off => off.clone().multiplyScalar(1 + .11 * HS / off.length());
  // washing keeps the head up: it tips and ducks into the paw but never noses down to the ground
  const HBW = { ...HB, hdPitch: [-.3, .65], nkPitch: [-.4, .7] };
  const pawPose = (P0, off, B = HBW) => { const P = { ...P0 }, o = pawOut(off), paw = pawClamp(P, spotAt(P, o)); reach(P, paw, HEADK, 16, o, .3, B); return { P, paw, err: reach.last }; };

  // 1. GroomFace: lift a front paw, lick it, then wash: the paw strokes over the face while the head ducks and
  //    rolls INTO it (cats move the head to the paw as much as the paw to the head), ear -> eye -> cheek ->
  //    muzzle; lick again, paw down.
  const sx = 1;
  const UPP = { scapL: -.4, scapLy: .012 * rig.hipH, hipRoll: -.04, hipX: -.008 };              // paw-up support
  const lickP = over(SITG, UPP, { hdPitch: .3, hdYaw: .15 * sx, hdRoll: .1 * sx });
  const lickSpot = pawClamp(lickP, spotAt(lickP, V(.15 * HS * sx, -.75 * HS, .95 * HS)));
  // face spots in head space, in wash order: over the ear -> brow -> eye -> cheek -> muzzle
  const WASH = [V(.7 * sx, .42, .1), V(.6 * sx, .35, .5), V(.52 * sx, .12, .7), V(.55 * sx, -.2, .66), V(.35 * sx, -.45, .82)].map(v => v.multiplyScalar(HS));
  // every pose below is solved on the meshes: the tongue on the paw, the paw on the face (pressed 2 mm into the fur)
  const PAWK = ['FLx', 'FLy', 'FLz'], FACEK = [...HEADK, ...PAWK, 'FLa', 'FLt'];
  const pawUp = (P, p, a = -1.7, toe = 1.2) => { setPaw(P, p, 1, a, toe); return P; };
  const faceStroke = (Q, s2) => { Q.hdPitch += .04 * s2; };
  const lickBase = pawUp(over(lickP), lickSpot);
  touchBest(rig, lickBase, { a: 'tongue', b: ['FL'], keys: [...PAWK, 'hdPitch'], bounds: { hdPitch: [.1, .6] }, at: lickAt(faceStroke) }, [{ FLy: lickBase.FLy - .02 }, { FLz: lickBase.FLz - .02 }]);
  report.pawLick = touch.last;
  // the point on the head's surface in the direction of a head-space offset (bind pose = Head bone frame)
  const headSurf = off => {
    const g = rig.model.userData.meshes.body.geometry.attributes.position, C = contactOf(rig), dir = off.clone().normalize();
    let best = null, bd = -Infinity;
    for (let i = 0; i < g.count; i++) {
      if (C.M.body.part[i] !== 'head') continue;
      const v = V(g.getX(i), g.getY(i), g.getZ(i)), r = v.clone().sub(d.Hc), sc = r.clone().normalize().dot(dir) + .2 * r.length() / HS;
      if (sc > bd) { bd = sc; best = v; }
    }
    return best.sub(d.joints.Head);
  };
  const onFace = (off, start, bounds) => {
    // the short arm cannot reach round the big head, so the paw stays up in front of the chest (where it was
    // licked, a little to the side) and the head ducks and tips to bring each spot of the face onto it
    const S0 = over(SITG, UPP, start), hs = headSurf(off);
    const P = pawUp({ ...S0 }, V(lickBase.FLx + .25 * d.pawR * sx, lickBase.FLy + .2 * d.pawR, lickBase.FLz - .2 * d.pawR));
    touchBest(rig, P, { a: 'FL', b: ['head'], zone: { bone: 'Head', off: hs, r: .04 }, keys: [...HEADK, ...PAWK], bounds, gap: -.002 }, [{ hdPitch: (start.hdPitch || 0) + .25, nkPitch: SITG.nkPitch + .2 }, { hdRoll: (start.hdRoll || 0) + .25 }]);
    return { P, err: Math.abs(touch.last + .002) };
  };
  const washPoses = WASH.map(off => onFace(off, { hdRoll: .35 * sx, hdPitch: .25, hdYaw: .25 * sx, nkRoll: .1 * sx }, HBW));
  report.wash = Math.max(...washPoses.map(w => w.err));
  const blendFace = (P, A, B, u, k) => { for (const key of FACEK) P[key] = lerp(P[key], lerp(A[key], B[key], u), k); };
  add('GroomFace', 6.6, true, t => {
    const P = { ...SITG };
    P.breath = Math.sin(TAU * t / 2.2);
    const up = mj(seg(t, 0, .45)) * (1 - mj(seg(t, 6.1, 6.6)));
    const lickA = t >= .45 && t < 2.25, lickB = t >= 5.15 && t < 6.05;
    for (const [key, v] of Object.entries(UPP)) P[key] = (P[key] || 0) * (1 - up) + v * up;
    blendFace(P, lickBase, lickBase, 0, up);
    P.FLz += .03 * Math.sin(Math.PI * up);                                      // the paw comes up in front of the chest
    if (t >= 2.05 && t < 5.3) {
      // two wash strokes; each runs ear -> muzzle, then the head lifts back out for the next
      const k = mj(seg(t, 2.05, 2.45)) * (1 - mj(seg(t, 4.9, 5.3)));
      const w = frac(seg(t, 2.25, 5.05) * 2), q = w < .78 ? mj(w / .78) : 1 - mj((w - .78) / .22);
      const f = q * (WASH.length - 1), i = Math.min(WASH.length - 2, Math.floor(f)), u = f - i;
      blendFace(P, washPoses[i].P, washPoses[i + 1].P, u, k);
      P.earLp = -.45 * k * (1 - Math.min(1, f)); P.earLy = -.3 * k;
      P.blink = Math.max(.6, k);
    } else P.blink = up > .5 ? .75 : blinkAt(t, [.2]);
    if (lickA) licking(P, t, .45, 4, 2.2, faceStroke);
    if (lickB) licking(P, t, 5.15, 2, 2.2, faceStroke);
    return P;
  }, { contacts: [{ a: 'tongue', b: ['FL'], when: P => P.tongue > .6 }, { a: 'FL', b: ['head'], when: (P, t) => t > 2.45 && t < 4.9 }] });

  // 2. GroomEar: the paw held up beside the head, the head tipped over and rubbed against it so the paw runs
  //    over and behind the ear, eyes shut, ear flattened
  const EAR = [V(.75 * sx, .4, -.1), V(.7 * sx, .55, .12), V(.68 * sx, .3, .28)].map(v => v.multiplyScalar(HS));
  const earPoses = EAR.map(off => onFace(off, { hdRoll: .6 * sx, hdPitch: .3, hdYaw: .3 * sx, nkRoll: .25 * sx }, { ...HBW, hdPitch: [-.3, .45], nkPitch: [-.4, .4] }));
  report.ear = Math.max(...earPoses.map(w => w.err));
  add('GroomEar', 4, true, t => {
    const P = { ...SITG }, up = mj(seg(t, 0, .45)) * (1 - mj(seg(t, 3.5, 4)));
    for (const [key, v] of Object.entries(UPP)) P[key] = (P[key] || 0) * (1 - up) + v * up;
    const w = frac(seg(t, .5, 3.4) * 3), q = .5 - .5 * Math.cos(TAU * w), f = q * 2, i = Math.min(1, Math.floor(f)), u = f - i;
    blendFace(P, earPoses[i].P, earPoses[i + 1].P, u, up);
    P.FLz += .03 * Math.sin(Math.PI * up) * (1 - up);
    P.blink = up; P.breath = Math.sin(TAU * t / 2.2);
    P.earLp = -.55 * up; P.earLy = -.45 * up;
    return P;
  }, { contacts: [{ a: 'FL', b: ['head'], when: (P, t) => t > .5 && t < 3.4 }] });

  // 3. GroomChest: sitting, chin tucked down to the chest, the tongue drawn up the bib in long strokes
  {
    const stroke = (Q, s) => { Q.hdPitch += .05 * s; Q.nkPitch += .025 * s; };
    const base = over(SITG, { hdPitch: .7, nkPitch: SITG.nkPitch + .4 });
    touchBest(rig, base, { a: 'tongue', b: ['torso'], zone: { bone: 'Chest', off: V(0, -.35 * d.chestR, .75 * d.chestR), r: .06 }, keys: ['nkPitch', 'hdPitch', 'spPitch'], bounds: { ...HB, hdPitch: [0, 1.6] }, at: lickAt(stroke) }, [{ hdPitch: 1.2, nkPitch: SITG.nkPitch + .2 }, { hdPitch: 1.45, nkPitch: SITG.nkPitch + .5, spPitch: .2 }]);
    report.chest = touch.last;
    add('GroomChest', 4.2, true, t => {
      const P = { ...SITG }, k = mj(seg(t, 0, .5)) * (1 - mj(seg(t, 3.7, 4.2)));
      Object.assign(P, mix(SITG, base, k)); P.blink = .8 * k + blinkAt(t, [.1]) * (1 - k); P.breath = Math.sin(TAU * t / 2.2);
      return licking(P, t, .55, 7, 2.2, stroke);
    }, { contacts: [{ a: 'tongue', b: ['torso'], when: P => P.tongue > .6 }] });
  }

  // 4. GroomFlank: head turned round over the shoulder to lick the shoulder and side, strokes running forward
  //    along the fur; the spine twists to help and the body leans away
  {
    const base = over(SITG, { spYaw: .4, spRoll: .05, nkYaw: .5, hdYaw: 1.1, hdPitch: .55, hdRoll: .2, hipRoll: -.06 });
    const tgt = surf(base, 'Chest', V(.9 * d.chestR, .2 * d.chestR, -.12 * d.BLm), reachTongue);
    solve(base, tgt, BODYK, [{ spYaw: .5, nkYaw: .7, hdYaw: 1.25, hdPitch: .8 }]); report.flank = reach.last;
    add('GroomFlank', 4.6, true, t => {
      const P = { ...SITG }, k = mj(seg(t, 0, .6)) * (1 - mj(seg(t, 4, 4.6)));
      Object.assign(P, mix(SITG, base, k)); P.blink = .85 * k; P.breath = Math.sin(TAU * t / 2.2);
      P.earLy = .3 * k;
      return licking(P, t, .65, 7, 2, (Q, s) => { Q.nkYaw -= .08 * s; Q.hdYaw -= .1 * s; Q.hdPitch += .04 * s; });
    });
  }

  // lying on the right side, curled round (belly and leg grooming start from here)
  const LIE = over(LOAF, { hipRoll: 1.3, hipY: -.6 * rig.hipY, spRoll: .1, spPitch: .25, nkPitch: .3, hdRoll: .4, hdPitch: .3, hdYaw: -.2, tailBase: -1.2, tailBend: .03, tailCurl: .15, tailYaw: 0 });
  for (const f of FOOT_KEYS) {
    const fr = f[0] === 'F', top = f[1] === 'L';
    Object.assign(LIE, { [f + 'fk']: 1, [f + 'k1']: fr ? .5 : (top ? -1.5 : -.3), [f + 'k2']: fr ? -.6 : .6, [f + 'k3']: fr ? .6 : -.4, [f + 'k4']: .3, [f + 'kz']: top ? .25 : -.05 });
  }
  // lying on its side: an authored head curl (face turned up toward the camera side, the toy head cannot
  // reach the belly anyway) and a small bounded solve around it that brings the mouth as near as it goes
  const LIEHEAD = { spPitch: .3, spYaw: .2, nkPitch: .2, nkYaw: .6, nkRoll: 0, hdPitch: .1, hdYaw: 1, hdRoll: .45 };
  const LIEB = Object.fromEntries(Object.entries(LIEHEAD).map(([k, v]) => [k, [v - .3, v + .3]]));
  let legHead;
  // 5. GroomLeg: lying on its side, the top hind leg raised straight up in the air, the head curled round to
  //    it licking the inside of the thigh, strokes up toward the foot (the lying form of the "cello" pose -
  //    with the toy proportions a sitting cello hides the short leg behind the head)
  {
    const base = over(LIE, LIEHEAD, { HLk1: -2.5, HLk2: .2, HLk3: -.6, HLk4: .2, HLkz: .9 });
    applyPose(rig, base);
    const knee = worldOf(rig, 'Shin_L'), hock = worldOf(rig, 'Foot_L'), mid = knee.clone().lerp(hock, .3);
    const tgt = mid.clone().add(worldOf(rig, 'Head').sub(mid).setLength(reachTongue));
    reach(base, tgt, ['spPitch', 'nkPitch', 'nkYaw', 'hdPitch', 'hdYaw'], 12, null, .1, LIEB); report.leg = reach.last; legHead = Object.fromEntries([...BODYK, 'nkRoll'].map(k => [k, base[k]]));
    add('GroomLeg', 5.6, true, t => {
      const k = mj(seg(t, 0, 1)) * (1 - mj(seg(t, 4.8, 5.6)));
      const P = mix(LOAF, base, k); P.blink = .8 * k + .5 * (1 - k); P.breath = 1.1 * Math.sin(TAU * t / 2.4);
      for (const f of FOOT_KEYS) P[f + 'fk'] = k;
      P.HLk3 += .25 * Math.sin(TAU * t / 1.3) * k;                                // toes flex while licked
      return licking(P, t, 1.1, 8, 2.1, (Q, s) => { Q.nkPitch += .06 * s; Q.hdPitch += .06 * s; });
    });
  }

  // 6. GroomBelly: lying on its side, curled round to lick the belly, top hind leg lifted out of the way
  {
    // the head curled round to the belly from the same side it reaches the raised leg (any deeper and the toy
    // head turns face-down into the ground); a short bounded solve then closes in on the belly
    const base = over(LIE, legHead, { HLk1: -2.1, HLkz: .6 });
    applyPose(rig, base);
    const bp = surf(base, 'Spine2', V(0, -.9 * d.chestR, -.1 * d.BLm), 0);
    const tgt = bp.add(worldOf(rig, 'Head').sub(bp).setLength(reachTongue));
    reach(base, tgt, ['spPitch', 'nkPitch', 'nkYaw', 'hdPitch', 'hdYaw'], 12, null, .1, LIEB); report.belly = reach.last;
    add('GroomBelly', 5, true, t => {
      const k = mj(seg(t, 0, 1)) * (1 - mj(seg(t, 4.2, 5)));
      const P = mix(LOAF, base, k); P.blink = .85 * k + .5 * (1 - k); P.breath = 1.1 * Math.sin(TAU * t / 2.4);
      for (const f of FOOT_KEYS) P[f + 'fk'] = k;
      return licking(P, t, 1.1, 7, 2, (Q, s) => { Q.nkPitch += .06 * s; Q.hdPitch += .05 * s; });
    });
  }

  // 7. GroomTail: sitting with the tail wrapped round the side to the front, the end held up off the ground
  //    under a paw, the head bent down to it licking toward the tip
  {
    const base = over(SITG, { tailBase: -1.3, tailYaw: -1.7, tailCurl: -.2, tailBend: .03, tailTip: -.5, hipRoll: -.05, spPitch: SITG.spPitch + .15 });
    applyPose(rig, base);
    const t8 = worldOf(rig, 'Tail8'), t10 = worldOf(rig, 'Tail10');
    setPaw(base, t10.clone().add(V(0, .02, .01)), 1, .2, 0);                        // paw pinning the tip
    const on = t8.clone().lerp(t10, .5);
    const tgt = on.clone().add(worldOf(rig, 'Head').sub(on).setLength(reachTongue));
    reachBest(base, tgt, BODYK, bodyStarts, null, { ...HB, hdPitch: [-.3, 1] }); report.tail = reach.last;
    add('GroomTail', 4.6, true, t => {
      const k = mj(seg(t, 0, .8)) * (1 - mj(seg(t, 3.9, 4.6)));
      const P = mix(SITG, base, k); P.blink = .8 * k; P.breath = Math.sin(TAU * t / 2.5);
      P.tailWave = .05 * (1 - k);
      return licking(P, t, .9, 6, 2, (Q, s) => { Q.nkPitch += .05 * s; Q.hdPitch += .05 * s; });
    });
  }

  // 8. ScratchEar: sitting, a hind foot comes up behind the ear and scratches fast (~7 strokes/s), head tipped
  //    into the foot, eyes squeezed, that ear flattened; the body leans away to balance on three legs
  {
    const base = over(SITG, { hipRoll: -.32, hipPitch: SITG.hipPitch - .12, spRoll: .2, nkRoll: .25, hdRoll: .55, hdPitch: .35, hdYaw: .3, blink: 1, earLp: -.6, earLy: -.5 });
    Object.assign(base, { HLfk: 1, HLk1: -2.3, HLk2: 1.1, HLk3: -.9, HLk4: .4, HLkz: .55 });
    base.FRx *= 1.25;
    add('ScratchEar', 3, true, t => {
      const k = mj(seg(t, 0, .45)) * (1 - mj(seg(t, 2.5, 3)));
      const P = mix(SITG, base, k); P.HLfk = k;
      const sc = Math.sin(TAU * 7 * t) * (t > .45 && t < 2.5 ? 1 : 0);
      P.HLk2 += .35 * sc * k; P.HLk3 -= .3 * sc * k;                              // the scratching stroke
      P.hdRoll += .03 * sc * k; P.mouth = .15 * k;                                  // head jiggles with it
      return P;
    });
  }

  // 9. NibbleClaws: sitting, a front paw lifted with toes spread, head turned sideways chewing at the claws
  add('NibbleClaws', 3.4, true, t => {
    const P = { ...SITG }, sx = 1, up = mj(seg(t, 0, .45)) * (1 - mj(seg(t, 2.9, 3.4)));
    P.hdRoll = .75 * sx * up; P.hdPitch = .45 * up; P.hdYaw = .2 * sx * up; P.blink = .7 * up;
    const chew = (t > .5 && t < 2.8) ? .5 + .5 * Math.sin(TAU * 4 * t) : 0;
    P.mouth = (.15 + .45 * chew) * up; P.hdPitch += .03 * chew;
    const tgt = atHead(P, V(.32 * sx * HS, -.62 * HS, .8 * HS)), g2 = V(P.FLx, P.FLy, P.FLz).lerp(tgt, up);
    P.FLx = g2.x; P.FLy = g2.y; P.FLz = g2.z; P.FLa = lerp(P.FLa, -1.2, up); P.FLt = lerp(0, -.6, up);   // toes spread back
    P.scapL = -.35 * up;
    return P;
  });
  // stretch: play-bow with a big yawn, then each hind leg stretched out behind
  add('Stretch', 4.2, false, withSettle(rig, t => {
    const bow = ss(seg(t, .2, .9)) * (1 - ss(seg(t, 2.1, 2.6)));
    const P = { ...P0 };
    P.hipPitch = .38 * bow; P.spPitch = .22 * bow; P.hipY = -.04 * h * bow; P.hipZ = -.05 * h * bow;
    P.nkPitch = -.25 * bow; P.hdPitch = -.3 * bow;
    for (const f of ['FL', 'FR']) { P[f + 'z'] += .55 * h * bow; P[f + 'a'] = lerp(.3, 1.25, bow); P[f + 't'] = -.25 * bow; }
    for (const f of ['HL', 'HR']) { P[f + 'a'] = lerp(.2, .45, bow); }
    const yawn = bump(t, 1.35, .32);
    P.mouth = yawn; P.tongue = .38 * yawn; P.tongueBend = .15 * yawn; P.tongueCurl = -1.1 * yawn; P.tongueSpread = .5 * yawn; P.blink = .2 + .75 * yawn; P.hdPitch -= .25 * yawn;   // tongue curls up in the yawn
    P.earLp = P.earRp = -.35 * yawn;
    P.tailBase = lerp(-.3, 1.0, bow); P.tailBend = .06; P.tailTip = .15 * bow; P.tailWave = .1; P.tailWph = t / 2;
    // hind-leg stretches: body shifts forward, one leg extends back and up, toes spread
    for (const [f, c] of [['HL', 3.05], ['HR', 3.7]]) {
      const e = bump(t, c, .2);
      P[f + 'z'] -= .55 * h * e; P[f + 'y'] += .14 * h * e; P[f + 'a'] = lerp(P[f + 'a'], -1.3, e); P[f + 't'] = -.35 * e;
      P.hipZ += .04 * h * e; P.hipRoll += (f === 'HL' ? -.06 : .06) * e;
    }
    P.blink = Math.max(P.blink, blinkAt(t, [2.5]));
    return P;
  }, 4.2, {}, 28));
  // drinking: crouched over the bowl, lapping ~3.5 times a second. Like real cats (Reis et al. 2010, Science):
  // the tongue tip curls under into a J so only its top touches the surface, then whips back up, pulling a
  // column of liquid that the jaw snaps shut on. Every 8 laps a short pause to swallow.
  {
    const f = 3.5, laps = 8, lapT = 1 / f, dur = laps * lapT + .95;
    add('Drink', dur, true, t => {
      const P = { ...P0 };
      P.hipY = -.07 * h; P.hipPitch = .06; P.spPitch = .18; P.nkPitch = .4; P.hdPitch = .5;
      for (const ff of ['FL', 'FR']) { P[ff + 'z'] += .02 * h; P[ff + 'a'] = .55; }
      for (const ff of ['HL', 'HR']) P[ff + 'a'] = -.1;
      P.scapL = P.scapR = .15; P.blink = .45 + .55 * blinkAt(t, [dur - .3]);
      P.tailBase = -.4; P.tailBend = .06; P.tailWave = .08; P.tailWph = t / 2;
      P.earLp = P.earRp = .1;
      if (t < laps * lapT) {
        const u = frac(t * f);
        addTongue(P, lapCycle(u));
      } else {                                                     // swallow, lift the head a little, lick the lips
        const v = seg(t, laps * lapT, dur), lift = Math.sin(Math.PI * v);
        P.hdPitch -= .2 * lift; P.nkPitch -= .08 * lift;
        addTongue(P, lipLick(seg(t, laps * lapT + .05, dur - .02)));
      }
      return P;
    }, { drink: { laps, rate: f } });
  }
  add('LickLips', 1.4, false, t => {
    const P = over(SIT, { breath: Math.sin(TAU * t / 2), blink: .3 + .5 * Math.sin(Math.PI * seg(t, .1, 1.2)) });
    return addTongue(P, lipLick(seg(t, .15, 1.15)));
  });
  // flop: rolls onto its side and relaxes (Ragdoll); then breathing with lazy tail flicks
  {
    const FLOP = over(LOAF, { hipRoll: 1.38, hipY: -.62 * rig.hipY, spRoll: .1, hdRoll: .9, hdPitch: .05, nkPitch: .05, blink: .55, tailBase: -1.2, tailBend: .03, tailCurl: 0, tailYaw: 0, earLp: -.2, earRp: -.2 });
    for (const f of FOOT_KEYS) {
      const fr = f[0] === 'F', top = f[1] === 'L';
      Object.assign(FLOP, { [f + 'fk']: 1, [f + 'k1']: fr ? .7 : top ? -.25 : -.6, [f + 'k2']: fr ? -.45 : .7, [f + 'k3']: fr ? .5 : -.35, [f + 'k4']: .3, [f + 'kz']: fr ? (top ? .1 : -.05) : (top ? .35 : -.35) });   // forelegs reach forward clear of the head, hind legs splay off the belly
    }
    settle(rig, FLOP, { fk: FOOT_KEYS, iters: 30 });
    add('Flop', 1.6, false, withSettle(rig, t => {
      const k = seg(t, 0, 1.4), P = transfer(P0, FLOP, k);
      P.hipY -= .06 * h * Math.sin(Math.PI * ss(k));     // sinks before rolling over
      return P;
    }, 1.6, { fk: FOOT_KEYS }, 16));
    add('FlopIdle', 4, true, t => over(FLOP, { breath: 1.2 * Math.sin(TAU * t / 2.4), tailTip: .3 * Math.sin(TAU * t / 2 - 1), tailWave: .1, tailWph: t / 2, blink: .55 + .45 * blinkAt(t, [2.6]) }));
  }
  // paw batting: lift, tap down, return (Turkish Van at the water bowl)
  add('PawBat', 1.4, true, t => {
    const P = { ...P0 }, u = t / 1.4;
    const up = u < .35 ? mj(u / .35) : u < .5 ? 1 - .9 * mj((u - .35) / .15) : .1 * (1 - mj((u - .5) / .5));
    const fwd = u < .5 ? mj(Math.min(1, u / .4)) : 1 - mj((u - .5) / .5);
    P.FLy += up * .5 * h; P.FLz += fwd * .36 * h; P.FLx *= .85; P.FLa = lerp(.3, -1.5, up); P.FLt = 1.0 * up;
    P.scapL = -.35 * up; P.scapLy = .012 * up * h; P.hipZ = -.025 * h * up; P.hipPitch = .06 * up; P.hipRoll = -.05 * up;
    P.hdPitch = .35 * Math.max(up, .8 * fwd); P.hdYaw = .1 * fwd;
    P.tailBase = -.2; P.tailWave = .2; P.tailWph = u * 2; P.earLp = P.earRp = .12;
    return P;
  });
  clips.groomReport = report;
  return clips;
}

// ------------------------------------------------------------------ bake to AnimationClips
const TAIL_LAG = .035, EAR_LAG = .06;
export function poseAt(clip, t) {
  const T = clip.loop ? ((t % clip.dur) + clip.dur) % clip.dur : clamp(t, 0, clip.dur);
  const P = clip.pose(T);
  if (clip.loop && clip.rootMotion && t >= clip.dur) { /* root motion continues over the loop */ }
  return P;
}
export function solveAt(rig, clip, t) {
  const tail = Array.from({ length: 10 }, (_, i) => clip.pose(clip.loop ? ((t - i * TAIL_LAG) % clip.dur + clip.dur) % clip.dur : clamp(t - i * TAIL_LAG, 0, clip.dur)));
  const ear = clip.pose(clip.loop ? ((t - EAR_LAG) % clip.dur + clip.dur) % clip.dur : clamp(t - EAR_LAG, 0, clip.dur));
  const P = clip.pose(clamp(t, 0, clip.dur));
  applyPose(rig, P, { tail, ear });
  return P;
}
export function bakeClip(rig, clip) {
  const n = Math.round(clip.dur * FPS), times = [], names = Object.keys(rig.B);
  const q = Object.fromEntries(names.map(k => [k, []])), pos = { Root: [], Hips: [], Scapula_L: [], Scapula_R: [], Tongue1: [] }, belly = [], morph = [];
  for (let i = 0; i <= n; i++) {
    const t = i / FPS;
    times.push(t);
    solveAt(rig, clip, t);
    for (const k of names) q[k].push(...rig.B[k].quaternion.toArray());
    for (const k in pos) pos[k].push(...rig.B[k].position.toArray());
    belly.push(...rig.B.Belly.scale.toArray());
    morph.push(...rig.face.morphTargetInfluences);
  }
  const tracks = [];
  for (const k of names) {
    const v = q[k];
    // drop bones that never move (keeps the files small)
    // keep every bone that leaves its rest rotation (identity) at any time - a bone HELD in a pose (the bent spine
    // while sitting) does not move but still needs its track, or a game engine snaps it back to the rest pose
    let posed = false; for (let i = 0; i < v.length && !posed; i += 4) if (Math.abs(v[i]) + Math.abs(v[i + 1]) + Math.abs(v[i + 2]) > 1e-5) posed = true;
    if (posed || k === 'Hips') tracks.push(new THREE.QuaternionKeyframeTrack(`${k}.quaternion`, times, v));
  }
  for (const k in pos) tracks.push(new THREE.VectorKeyframeTrack(`${k}.position`, times, pos[k]));
  tracks.push(new THREE.VectorKeyframeTrack('Belly.scale', times, belly));
  tracks.push(new THREE.NumberKeyframeTrack('Face.morphTargetInfluences', times, morph));
  const ac = new THREE.AnimationClip(clip.name, clip.dur, tracks);
  ac.userData = { loop: clip.loop, rootMotion: !!clip.rootMotion, speed: clip.speed, stride: clip.stride, jump: clip.jump };
  return ac;
}
