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
const over = (P, o) => Object.assign({ ...P }, o);

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
    } else {
      u = (c - duty) / (1 - duty);
      z = zt + g.S * mj(u);
      y += g.lift[fr ? 'F' : 'H'] * g.S * .22 * Math.pow(Math.sin(Math.PI * Math.pow(u, .8)), 1.2);
      // swing: front paw folds back at the wrist, then reaches forward (E1) and flattens to land
      a = fr ? K([[0, -.6], [.3, -1.9], [.72, .45], [1, .3]], u) : K([[0, -.85], [.35, -1.3], [.8, .4], [1, .25]], u);
      toe = fr ? K([[0, -.15], [.25, 1.0], [.7, .4], [1, -.05]], u) : K([[0, -.2], [.3, .8], [.75, .2], [1, 0]], u);
    }
    // cats walk on a narrow track: paws step close to the midline
    const narrow = g.kind === 'walk' ? .72 : g.kind === 'trot' ? .8 : .85;
    out.feet[f] = { x: r.x * narrow, y, z: z - rootZ, a, t: toe, c, u, stance };
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
function sitPose(rig) {
  const P = stand(rig), h = rig.hipH;
  Object.assign(P, {
    hipY: -.47 * rig.hipY, hipZ: -.02 * h, hipPitch: -.78, spPitch: -.32, nkPitch: .62, hdPitch: .02,
    scapL: -.08, scapR: -.08,
    tailBase: -1.05, tailBend: .1, tailCurl: .26, tailYaw: .35,
  });
  for (const f of ['FL', 'FR']) { P[f + 'a'] = .12; P[f + 'z'] -= .01; P[f + 'x'] *= .8; }
  for (const f of ['HL', 'HR']) { P[f + 'a'] = 1.42; P[f + 'z'] += .17 * h; P[f + 'x'] *= 1.25; }
  return P;
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
    mouth: K([[0, 0], [.12, .3], [.78, .3], [.92, .02], [1, 0]], u),
    tongue: K([[0, 0], [.15, .68], [.72, .68], [.86, 0], [1, 0]], u),
    tongueBend: -.25, tongueCurl: K([[0, -.2], [.2, -.75], [.7, -.75], [1, -.2]], u), tongueSpread: .35,
    tongueYaw: K([[0, .7], [.2, .7], [.5, -.75], [.62, -.75], [.72, 0], [1, 0]], u),
    hdPitch: K([[0, 0], [.3, -.05], [.7, -.05], [1, 0]], u),
  };
}
const addTongue = (P, c, k = 1) => { for (const key in c) P[key] = key === 'hdPitch' ? P[key] + c[key] * k : lerp(P[key], c[key], k); return P; };

// ------------------------------------------------------------------ clips
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
    add('JumpUp', dur, false, t => {
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
      P.hipY = -.3 * h * crouch + .1 * h * push;
      P.hipPitch = t < tPush ? lerp(.05 * crouch, -.45, ss(seg(t, .92, tPush))) : t < tLand ? lerp(-.45, .35, ss(fly)) : lerp(.35, 0, ss(seg(t, tLand, tLand + .5)));
      P.spPitch = t < tPush ? .08 * crouch - .25 * ss(seg(t, .95, tPush)) : t < tLand ? K([[0, -.25], [.45, .12], [1, .2]], fly) : lerp(.2, 0, ss(seg(t, tLand, tLand + .55)));
      const wig = Math.sin(TAU * 5 * t) * ss(seg(t, .4, .5)) * (1 - ss(seg(t, .82, .9)));
      P.hipYaw = .1 * wig; P.hipRoll = .05 * wig;                                       // the famous bum wiggle
      P.nkPitch = -.1 * crouch - P.hipPitch * .5; P.hdPitch = -.1 * (1 - fly) + .12 * ss(seg(t, tLand - .15, tLand + .1)) * (1 - ss(seg(t, tLand + .2, tLand + .6)));
      P.tailBase = t < tPush ? -1.25 + .2 * crouch : t < tLand ? lerp(-.9, .2, Math.sin(Math.PI * fly)) : lerp(-.4, -.3, ss(seg(t, tLand, dur)));
      P.tailBend = .03; P.tailWave = t < tPush ? .35 * ss(seg(t, .3, .5)) : .05; P.tailWph = t * 3;
      P.earLp = P.earRp = t < tPush ? .15 : -.2 * Math.sin(Math.PI * fly);
      P.blink = blinkAt(t, [tLand + .05], .2) * .7;
      // paws: planted on the floor (world) until lift-off, then fly, then planted on the box top (world)
      for (const f of FOOT_KEYS) {
        const fr = f[0] === 'F', r = rig.restFoot[f], off = fr ? .04 : 0;
        const liftT = fr ? tLift : tPush, touchT = fr ? tLand : tLand + .07;
        let wz, wy, a = fr ? .3 : .2, toe = 0;
        const tread = !fr ? .015 * Math.max(0, Math.sin(TAU * 5 * t + (f === 'HL' ? 0 : Math.PI))) * (wig ? 1 : 0) : 0;
        if (t < liftT) { wz = r.z + (fr ? .03 * crouch : 0); wy = rig.ballH + tread; if (!fr) a = .2 - .5 * crouch; }
        else if (t >= touchT) { wz = r.z + D; wy = Hup + rig.ballH; if (!fr) a = .2 - .5 * crouchB; else a = .3 - .2 * crouchB; }
        else {
          const u = (t - liftT) / (touchT - liftT), body = { z: P.rootZ, y: P.rootY };
          // fronts tuck to the chest then reach down for the landing; hinds trail stretched, then tuck under
          const relZ = fr ? K([[0, r.z + .02], [.35, r.z + .08], [.8, r.z + .14], [1, r.z]], u) : K([[0, r.z], [.25, r.z - .14], [.6, r.z - .06], [1, r.z]], u);
          const relY = fr ? K([[0, rig.ballH], [.35, .5 * rig.hipY], [.8, .2 * rig.hipY], [1, rig.ballH]], u) : K([[0, rig.ballH], [.3, .3 * rig.hipY], [.7, .4 * rig.hipY], [1, rig.ballH]], u);
          const landing = ss(seg(u, .75, 1));
          wz = lerp(body.z + relZ, r.z + D, landing); wy = lerp(body.y + relY, Hup + rig.ballH, landing);
          a = fr ? K([[0, .3], [.3, -1.6], [.8, .4], [1, .3]], u) : K([[0, -.9], [.4, -1.2], [.85, .5], [1, .2]], u);
          toe = fr ? K([[0, 0], [.3, 1.1], [.8, .2], [1, 0]], u) : K([[0, -.2], [.4, .7], [1, 0]], u);
        }
        P[f + 'x'] = r.x; P[f + 'y'] = wy - P.rootY; P[f + 'z'] = wz - P.rootZ; P[f + 'a'] = a; P[f + 't'] = toe;
      }
      return P;
    }, { rootMotion: true, jump: { D, H: Hup } });
  }
  // sitting down, sitting, standing up
  const SIT = sitPose(rig), LOAF = loafPose(rig), SLEEP = sleepPose(rig);
  const transfer = (A, B, k, lifts = {}) => {
    const P = mix(A, B, ss(k));
    for (const [f, hgt] of Object.entries(lifts)) P[f + 'y'] += hgt * Math.sin(Math.PI * ss(k));
    return P;
  };
  add('SitDown', .9, false, t => over(transfer(P0, SIT, t / .9, { HL: .02, HR: .02 }), { blink: blinkAt(t, [.75]) }));
  add('Sit', 4, true, t => over(SIT, {
    breath: Math.sin(TAU * t / 2), blink: blinkAt(t, [1.4, 3.3]), tailWave: .05, tailWph: t / 2,
    hdYaw: K([[0, 0], [1.8, 0], [2.2, .3], [3.2, .3], [3.6, 0], [4, 0]], t), earRp: K([[0, 0], [2.5, 0], [2.6, -.4], [2.85, 0], [4, 0]], t),
  }));
  add('StandUp', .7, false, t => transfer(SIT, P0, t / .7, { HL: .015, HR: .015 }));
  // lying down into a loaf, loafing, curling up to sleep
  add('LieDown', 1.1, false, t => over(transfer(P0, LOAF, t / 1.1, { FL: .03, FR: .03 }), { blink: blinkAt(t, [.9]) }));
  add('Loaf', 5, true, t => over(LOAF, { breath: Math.sin(TAU * t / 2.5), blink: .55 + .45 * blinkAt(t, [2]), tailWave: .04, tailWph: t / 2.5 }));
  add('Sleep', 5, true, t => over(SLEEP, { breath: 1.3 * Math.sin(TAU * t / 2.5), tailTip: .05 * Math.sin(TAU * t / 5), earLp: -.25 + .25 * bump(t, 3.2, .06) }));
  add('FallAsleep', 2, false, t => over(transfer(LOAF, SLEEP, t / 2), { blink: Math.max(.55, ss(seg(t, .6, 1.6))) }));
  // grooming: lick the paw three times, then wipe it over the face and ear twice
  add('Groom', 4.8, true, t => {
    const P = { ...SIT }, h2 = rig.hipH;
    // 4 licks up the paw (0.4-2.2 s), then the paw wipes the face and ear
    const lick = t < 2.2, lu = seg(t, .4, 2.2) * 4, li = Math.floor(lu), lph = lu - li, k = lick && t > .4 ? 1 : 0;
    const up = ss(seg(t, 0, .35)) * (1 - ss(seg(t, 4.45, 4.8)));
    P.breath = Math.sin(TAU * t / 2);
    P.hdPitch = lick ? .45 + .1 * k : .15; P.hdYaw = lick ? -.25 : -.45; P.hdRoll = lick ? -.15 : -.35;
    P.blink = .85;
    if (k) addTongue(P, lickCycle(lph));
    // paw target: in front of the mouth for licking, then circling over the cheek and ear
    const Hc = rig.d.Hc, HS = rig.d.HS;
    const ang = TAU * (t - 2.2) / 1.3;
    // paw held just in front of and below the mouth: the tongue lands on its top and drags up over it
    const lickPos = V(.12 * HS, Hc.y - .95 * HS + rig.hipY * -.25, Hc.z + 1.0 * HS);
    const wipePos = V(.55 * HS + .15 * HS * Math.cos(ang), Hc.y - .25 * HS + .35 * HS * Math.sin(ang) + rig.hipY * -.25, Hc.z + .35 * HS - .1 * HS * Math.cos(ang));
    // the paw also rises a little into each stroke, meeting the tongue (a lick is the two moving together)
    const tp = lick ? lickPos.clone().add(V(0, .04 * HS * Math.sin(Math.PI * clamp((lph - .3) / .4, 0, 1)) * k, 0)) : wipePos;
    const ground = V(P.FLx, P.FLy, P.FLz);
    const tgt = ground.lerp(tp, up);
    P.FLx = tgt.x; P.FLy = tgt.y; P.FLz = tgt.z; P.FLa = lerp(P.FLa, -1.5, up); P.FLt = lerp(0, 1.2, up);
    P.scapL = -.35 * up; P.scapLy = .01 * up * h2;
    if (!lick) { P.hdPitch = .2 + .12 * Math.sin(ang); P.earLp = -.3 * (.5 + .5 * Math.sin(ang)); }
    P.tailWave = .05; P.tailWph = t / 2;
    return P;
  });
  // stretch: play-bow with a big yawn, then each hind leg stretched out behind
  add('Stretch', 4.2, false, t => {
    const bow = ss(seg(t, .2, .9)) * (1 - ss(seg(t, 2.1, 2.6)));
    const P = { ...P0 };
    P.hipPitch = .38 * bow; P.spPitch = .22 * bow; P.hipY = -.04 * h * bow; P.hipZ = -.05 * h * bow;
    P.nkPitch = -.45 * bow; P.hdPitch = -.3 * bow;
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
  });
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
      Object.assign(FLOP, { [f + 'fk']: 1, [f + 'k1']: fr ? .35 : -.25, [f + 'k2']: fr ? -.45 : .7, [f + 'k3']: fr ? .5 : -.35, [f + 'k4']: .3, [f + 'kz']: top ? .1 : -.05 });
    }
    add('Flop', 1.6, false, t => {
      const k = seg(t, 0, 1.4), P = transfer(P0, FLOP, k);
      P.hipY -= .06 * h * Math.sin(Math.PI * ss(k));     // sinks before rolling over
      return P;
    });
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
