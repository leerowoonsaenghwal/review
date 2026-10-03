// Contact and interpenetration measurements on the real skinned meshes (no physics engine, no renderer).
//   const C = makeContact(rig);  applyPose(rig, P);  C.update();
//   C.depth(points, exclude)  -> how deep a set of vertices sits inside the rest of the cat
//   C.gap(points, labels)     -> signed distance from a set of vertices to a body part's surface
// Every vertex gets a part label from the bone that moves it most:
//   torso, head, tail, tongue, FL/FR/HL/HR (forearm/shin down), FLu/FRu/HLu/HRu (upper arm / thigh);
//   face-mesh vertices on paw bones are the toe beans ('bean' flag).
import * as THREE from 'three';

const partOf = name => {
  if (/^Tongue/.test(name)) return 'tongue';
  if (/^(Head|Ear_)/.test(name)) return 'head';
  if (/^Tail/.test(name)) return 'tail';
  const m = name.match(/^(UpperArm|Forearm|Hand|Fingers|Thigh|Shin|Foot|Toes)_([LR])$/);
  if (m) return (['UpperArm', 'Forearm', 'Hand', 'Fingers'].includes(m[1]) ? 'F' : 'H') + m[2] + (m[1] === 'UpperArm' || m[1] === 'Thigh' ? 'u' : '');
  return 'torso';
};
export const LIMBS = ['FL', 'FR', 'HL', 'HR'];

// closest point on triangle abc to p (Ericson, Real-Time Collision Detection 5.1.5); returns barycentrics
function closestBary(px, py, pz, A, B, Cc, out) {
  const abx = B[0] - A[0], aby = B[1] - A[1], abz = B[2] - A[2], acx = Cc[0] - A[0], acy = Cc[1] - A[1], acz = Cc[2] - A[2];
  const apx = px - A[0], apy = py - A[1], apz = pz - A[2];
  const d1 = abx * apx + aby * apy + abz * apz, d2 = acx * apx + acy * apy + acz * apz;
  if (d1 <= 0 && d2 <= 0) return out.set(1, 0, 0);
  const bpx = px - B[0], bpy = py - B[1], bpz = pz - B[2];
  const d3 = abx * bpx + aby * bpy + abz * bpz, d4 = acx * bpx + acy * bpy + acz * bpz;
  if (d3 >= 0 && d4 <= d3) return out.set(0, 1, 0);
  const vc = d1 * d4 - d3 * d2;
  if (vc <= 0 && d1 >= 0 && d3 <= 0) { const v = d1 / (d1 - d3); return out.set(1 - v, v, 0); }
  const cpx = px - Cc[0], cpy = py - Cc[1], cpz = pz - Cc[2];
  const d5 = abx * cpx + aby * cpy + abz * cpz, d6 = acx * cpx + acy * cpy + acz * cpz;
  if (d6 >= 0 && d5 <= d6) return out.set(0, 0, 1);
  const vb = d5 * d2 - d1 * d6;
  if (vb <= 0 && d2 >= 0 && d6 <= 0) { const w = d2 / (d2 - d6); return out.set(1 - w, 0, w); }
  const va = d3 * d6 - d5 * d4;
  if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) { const w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return out.set(0, 1 - w, w); }
  const den = 1 / (va + vb + vc), v = vb * den, w = vc * den;
  return out.set(1 - v - w, v, w);
}

export function makeContact(rig, { cell = .015 } = {}) {
  const { body, face } = rig.model.userData.meshes, skel = body.skeleton;
  const mk = mesh => {
    const g = mesh.geometry, n = g.attributes.position.count;
    const si = g.attributes.skinIndex.array, sw = g.attributes.skinWeight.array, rest = g.attributes.position.array;
    const part = new Array(n), wmax = new Float32Array(n);
    for (let i = 0; i < n; i++) {
      let best = 0; for (let k = 1; k < 4; k++) if (sw[i * 4 + k] > sw[i * 4 + best]) best = k;
      part[i] = partOf(skel.bones[si[i * 4 + best]].name); wmax[i] = sw[i * 4 + best];
    }
    return { mesh, n, si, sw, rest, part, wmax, pos: new Float32Array(n * 3), idx: g.index.array };
  };
  const M = { body: mk(body), face: mk(face) };
  // triangles of the body mesh, labelled when all three corners share a part (seams belong to no part)
  const B = M.body, nt = B.idx.length / 3, triPart = new Array(nt);
  for (let t = 0; t < nt; t++) {
    const a = B.part[B.idx[3 * t]], b = B.part[B.idx[3 * t + 1]], c = B.part[B.idx[3 * t + 2]];
    triPart[t] = a === b && b === c ? a : null;
  }
  // vertices on the rim of a part (where it meets a seam or another part): inside/outside is undefined
  // there (the part's surface is open), so a nearest point on a rim is not used to judge penetration
  const rim = new Uint8Array(B.n), firstPart = new Array(B.n);
  for (let t = 0; t < nt; t++) for (let k = 0; k < 3; k++) {
    const v = B.idx[3 * t + k];
    if (firstPart[v] === undefined) firstPart[v] = triPart[t]; else if (firstPart[v] !== triPart[t]) rim[v] = 1;
    if (!triPart[t]) rim[v] = 1;
  }
  const mats = skel.bones.map(() => new THREE.Matrix4());
  const skinAll = m => {
    const e = mats.map(x => x.elements), { n, si, sw, rest, pos } = m;
    for (let i = 0; i < n; i++) {
      const x = rest[3 * i], y = rest[3 * i + 1], z = rest[3 * i + 2]; let ox = 0, oy = 0, oz = 0;
      for (let k = 0; k < 4; k++) {
        const w = sw[4 * i + k]; if (!w) continue;
        const a = e[si[4 * i + k]];
        ox += w * (a[0] * x + a[4] * y + a[8] * z + a[12]); oy += w * (a[1] * x + a[5] * y + a[9] * z + a[13]); oz += w * (a[2] * x + a[6] * y + a[10] * z + a[14]);
      }
      pos[3 * i] = ox; pos[3 * i + 1] = oy; pos[3 * i + 2] = oz;
    }
  };
  // spatial hash of labelled triangles, rebuilt on update()
  let grid = new Map();
  const vn = new Float32Array(B.n * 3);
  const normals = () => {
    vn.fill(0); const p = B.pos;
    for (let t = 0; t < nt; t++) {
      const i = B.idx[3 * t], j = B.idx[3 * t + 1], k = B.idx[3 * t + 2];
      const ux = p[3 * j] - p[3 * i], uy = p[3 * j + 1] - p[3 * i + 1], uz = p[3 * j + 2] - p[3 * i + 2];
      const wx = p[3 * k] - p[3 * i], wy = p[3 * k + 1] - p[3 * i + 1], wz = p[3 * k + 2] - p[3 * i + 2];
      const nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
      for (const v of [i, j, k]) { vn[3 * v] += nx; vn[3 * v + 1] += ny; vn[3 * v + 2] += nz; }
    }
  };
  const key = (i, j, k) => (i * 73856093) ^ (j * 19349663) ^ (k * 83492791);
  const P3 = (t, k) => { const v = B.idx[3 * t + k]; return [B.pos[3 * v], B.pos[3 * v + 1], B.pos[3 * v + 2]]; };
  const C = {
    M, triPart,
    update({ face = true } = {}) {
      for (let b = 0; b < skel.bones.length; b++) mats[b].multiplyMatrices(skel.bones[b].matrixWorld, skel.boneInverses[b]);
      skinAll(M.body); if (face) skinAll(M.face); normals();
      grid = new Map();
      for (let t = 0; t < nt; t++) {
        if (!triPart[t]) continue;
        const a = P3(t, 0), b = P3(t, 1), c = P3(t, 2);
        const lo = [0, 1, 2].map(k => Math.floor(Math.min(a[k], b[k], c[k]) / cell)), hi = [0, 1, 2].map(k => Math.floor(Math.max(a[k], b[k], c[k]) / cell));
        for (let i = lo[0]; i <= hi[0]; i++) for (let j = lo[1]; j <= hi[1]; j++) for (let k = lo[2]; k <= hi[2]; k++) {
          const h = key(i, j, k); let L = grid.get(h); if (!L) grid.set(h, L = []); L.push(t);
        }
      }
      return C;
    },
    // bone matrices only (then skin1 skins single vertices: cheap enough for finite differences)
    refresh() { for (let b = 0; b < skel.bones.length; b++) mats[b].multiplyMatrices(skel.bones[b].matrixWorld, skel.boneInverses[b]); return C; },
    skin1(mesh, i, out = new THREE.Vector3()) {
      const m = M[mesh], x = m.rest[3 * i], y = m.rest[3 * i + 1], z = m.rest[3 * i + 2]; out.set(0, 0, 0);
      for (let k = 0; k < 4; k++) {
        const w = m.sw[4 * i + k]; if (!w) continue;
        const a = mats[m.si[4 * i + k]].elements;
        out.x += w * (a[0] * x + a[4] * y + a[8] * z + a[12]); out.y += w * (a[1] * x + a[5] * y + a[9] * z + a[13]); out.z += w * (a[2] * x + a[6] * y + a[10] * z + a[14]);
      }
      return out;
    },
    triVerts(t) { return [B.idx[3 * t], B.idx[3 * t + 1], B.idx[3 * t + 2]]; },
    triCenter(t) { const v = C.triVerts(t), p = B.pos; return new THREE.Vector3((p[3 * v[0]] + p[3 * v[1]] + p[3 * v[2]]) / 3, (p[3 * v[0] + 1] + p[3 * v[1] + 1] + p[3 * v[2] + 1]) / 3, (p[3 * v[0] + 2] + p[3 * v[1] + 2] + p[3 * v[2] + 2]) / 3); },
    // vertex sets: { mesh: 'body'|'face', ids: [...] }
    points(test, mesh = 'body', step = 1) {
      const m = M[mesh], ids = [];
      for (let i = 0; i < m.n; i += step) if (test(m.part[i], i, m)) ids.push(i);
      return { mesh, ids };
    },
    at(set, k) { const p = M[set.mesh].pos, i = set.ids[k]; return new THREE.Vector3(p[3 * i], p[3 * i + 1], p[3 * i + 2]); },
    // nearest labelled triangle within maxR whose part passes `ok`; signed distance (negative = inside)
    nearest(x, y, z, ok, maxR = .1) {
      const ci = Math.floor(x / cell), cj = Math.floor(y / cell), ck = Math.floor(z / cell), bc = new THREE.Vector3();
      let best = null, bd = Infinity; const seen = new Set();
      for (let r = 0; r * cell <= maxR + cell; r++) {
        for (let i = ci - r; i <= ci + r; i++) for (let j = cj - r; j <= cj + r; j++) for (let k = ck - r; k <= ck + r; k++) {
          if (Math.max(Math.abs(i - ci), Math.abs(j - cj), Math.abs(k - ck)) !== r) continue;
          const L = grid.get(key(i, j, k)); if (!L) continue;
          for (const t of L) {
            if (seen.has(t) || !ok(triPart[t], t)) continue; seen.add(t);
            const a = P3(t, 0), b = P3(t, 1), c = P3(t, 2);
            closestBary(x, y, z, a, b, c, bc);
            const qx = bc.x * a[0] + bc.y * b[0] + bc.z * c[0], qy = bc.x * a[1] + bc.y * b[1] + bc.z * c[1], qz = bc.x * a[2] + bc.y * b[2] + bc.z * c[2];
            const d2 = (x - qx) ** 2 + (y - qy) ** 2 + (z - qz) ** 2;
            if (d2 < bd) { bd = d2; best = { t, q: [qx, qy, qz], bary: bc.clone() }; }
          }
        }
        if (best && Math.sqrt(bd) < r * cell) break;          // nothing in further rings can be nearer
      }
      if (!best || bd > maxR * maxR) return null;           // (hash collisions can offer far-away triangles)
      let onRim = 0; for (let k = 0; k < 3; k++) if (rim[B.idx[3 * best.t + k]]) onRim += best.bary.getComponent(k);
      best.rim = onRim > .5;
      // inside/outside from the smooth vertex normals at the closest point (a face normal misjudges points
      // nearest an edge or corner)
      const n = new THREE.Vector3();
      for (let k = 0; k < 3; k++) { const v = B.idx[3 * best.t + k], w = best.bary.getComponent(k); n.x += w * vn[3 * v]; n.y += w * vn[3 * v + 1]; n.z += w * vn[3 * v + 2]; }
      n.normalize();
      const s = (x - best.q[0]) * n.x + (y - best.q[1]) * n.y + (z - best.q[2]) * n.z;
      best.d = Math.sqrt(bd) * (s < 0 ? -1 : 1); best.part = triPart[best.t]; best.n = n;
      return best;
    },
    // deepest vertex of `set` inside a part accepted by `ok`
    // deepest vertex of `set` inside a part accepted by `ok`; `base` (from depthEach in the rest pose) lets a
    // vertex already that deep in the bind pose (a long-haired breed's legs inside its fur skirt) count from there
    depth(set, ok, base = null, maxR = .06) {
      const p = M[set.mesh].pos; let worst = { d: 0 };
      // only points inside the bounding box of the parts tested can be inside them
      const lo = [Infinity, Infinity, Infinity], hi = [-Infinity, -Infinity, -Infinity];
      for (let v = 0; v < B.n; v++) if (ok(B.part[v])) for (let k = 0; k < 3; k++) { lo[k] = Math.min(lo[k], B.pos[3 * v + k]); hi[k] = Math.max(hi[k], B.pos[3 * v + k]); }
      set.ids.forEach((i, k) => {
        if (p[3 * i] < lo[0] || p[3 * i] > hi[0] || p[3 * i + 1] < lo[1] || p[3 * i + 1] > hi[1] || p[3 * i + 2] < lo[2] || p[3 * i + 2] > hi[2]) return;
        const r = C.nearest(p[3 * i], p[3 * i + 1], p[3 * i + 2], ok, maxR);
        if (!r || r.rim) return;
        const d = base ? Math.min(0, r.d - base[k]) : r.d;
        if (d < worst.d) worst = { d, part: r.part, i };
      });
      return worst;
    },
    depthEach(set, ok) {
      const p = M[set.mesh].pos;
      return Float32Array.from(set.ids, i => { const r = C.nearest(p[3 * i], p[3 * i + 1], p[3 * i + 2], ok, .06); return r && !r.rim ? Math.min(0, r.d) : 0; });
    },
    // smallest signed distance from `set` to the surface of parts accepted by `ok` (with the vertex pair)
    gap(set, ok, maxR = .25) {
      const p = M[set.mesh].pos; let best = { d: Infinity };
      for (const i of set.ids) {
        const r = C.nearest(p[3 * i], p[3 * i + 1], p[3 * i + 2], ok, maxR);
        if (r && r.d < best.d) best = { ...r, i };
      }
      return best;
    },
    lowest(set, who) { const p = M[set.mesh].pos; let y = Infinity, at = -1; for (const i of set.ids) if (p[3 * i + 1] < y) { y = p[3 * i + 1]; at = i; } if (who) who.part = M[set.mesh].part[at]; return y; },
  };
  return C;
}
