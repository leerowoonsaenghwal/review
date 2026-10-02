// Signed-distance sculpting -> one continuous, watertight mesh.
//
// The cat body is described as a set of primitives (ellipsoids, round cones) merged with smooth unions,
// the way a sculptor blends clay: no seams where the leg meets the body or the muzzle meets the head.
// surfaceNets() turns that field into triangles; relax() then smooths the staircase out of the grid
// and snaps every vertex back onto the true surface.

// ---- primitives (Inigo Quilez's distance functions)
export function sdEllipsoid(px, py, pz, rx, ry, rz) {
  const k0 = Math.hypot(px / rx, py / ry, pz / rz), k1 = Math.hypot(px / (rx * rx), py / (ry * ry), pz / (rz * rz));
  return k1 > 1e-9 ? k0 * (k0 - 1) / k1 : -Math.min(rx, ry, rz);
}
export function sdRoundCone(px, py, pz, a, b, r1, r2) {
  const bx = b[0] - a[0], by = b[1] - a[1], bz = b[2] - a[2];
  const l2 = bx * bx + by * by + bz * bz, rr = r1 - r2, a2 = l2 - rr * rr, il2 = 1 / l2;
  const qx = px - a[0], qy = py - a[1], qz = pz - a[2];
  const y = qx * bx + qy * by + qz * bz, z = y - l2;
  const wx = qx * l2 - bx * y, wy = qy * l2 - by * y, wz = qz * l2 - bz * y;
  const x2 = wx * wx + wy * wy + wz * wz, y2 = y * y * l2, z2 = z * z * l2;
  const k = Math.sign(rr) * rr * rr * x2;
  if (Math.sign(z) * a2 * z2 > k) return Math.sqrt(x2 + z2) * il2 - r2;
  if (Math.sign(y) * a2 * y2 < k) return Math.sqrt(x2 + y2) * il2 - r1;
  return (Math.sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
}
export const smin = (a, b, k) => { if (k <= 0) return Math.min(a, b); const h = Math.max(k - Math.abs(a - b), 0) / k; return Math.min(a, b) - h * h * k * .25; };

// ---- a field made of primitives. Each primitive: { d(x,y,z) -> distance, box: [x0,y0,z0,x1,y1,z1], k: blend }
// Primitives far from a point (outside their box grown by k) cannot change a smooth union near the surface,
// so they are skipped; this keeps sculpts with a few hundred pieces fast.
export class Field {
  constructor() { this.prims = []; }
  add(p) { this.prims.push(p); return p; }
  eval(x, y, z) {
    let d = 1e9, far = 1e9;
    for (const p of this.prims) {
      const b = p.box, g = p.k + .05;
      const ox = Math.max(b[0] - g - x, 0, x - b[3] - g), oy = Math.max(b[1] - g - y, 0, y - b[4] - g), oz = Math.max(b[2] - g - z, 0, z - b[5] - g);
      if (ox || oy || oz) { far = Math.min(far, Math.hypot(ox, oy, oz) + g); continue; }
      d = d === 1e9 ? p.d(x, y, z) : smin(d, p.d(x, y, z), p.k);
    }
    return d === 1e9 ? far : Math.min(d, far);
  }
  // distance of each nearby primitive on its own (used to work out which body part a surface point belongs to)
  near(x, y, z, reach = .12) {
    const out = [];
    for (const p of this.prims) {
      const b = p.box;
      if (x < b[0] - reach || x > b[3] + reach || y < b[1] - reach || y > b[4] + reach || z < b[2] - reach || z > b[5] + reach) continue;
      out.push([p, p.d(x, y, z)]);
    }
    return out;
  }
  grad(x, y, z, e = 1e-3) {
    return [(this.eval(x + e, y, z) - this.eval(x - e, y, z)) / (2 * e), (this.eval(x, y + e, z) - this.eval(x, y - e, z)) / (2 * e), (this.eval(x, y, z + e) - this.eval(x, y, z - e)) / (2 * e)];
  }
  bounds() {
    const b = [1e9, 1e9, 1e9, -1e9, -1e9, -1e9];
    for (const p of this.prims) for (let i = 0; i < 3; i++) { b[i] = Math.min(b[i], p.box[i]); b[i + 3] = Math.max(b[i + 3], p.box[i + 3]); }
    return b;
  }
}

// ---- surface nets: one vertex per grid cell that the surface passes through, one quad per crossed grid edge
export function surfaceNets(field, h, pad = .04) {
  const bb = field.bounds();
  const x0 = bb[0] - pad, y0 = bb[1] - pad, z0 = bb[2] - pad;
  const nx = Math.ceil((bb[3] - bb[0] + 2 * pad) / h) + 1, ny = Math.ceil((bb[4] - bb[1] + 2 * pad) / h) + 1, nz = Math.ceil((bb[5] - bb[2] + 2 * pad) / h) + 1;
  const F = new Float32Array(nx * ny * nz), I = (i, j, k) => (k * ny + j) * nx + i;
  for (let k = 0; k < nz; k++) for (let j = 0; j < ny; j++) for (let i = 0; i < nx; i++) F[I(i, j, k)] = field.eval(x0 + i * h, y0 + j * h, z0 + k * h);
  const cx = nx - 1, cy = ny - 1, cz = nz - 1, C = (i, j, k) => (k * cy + j) * cx + i;
  const vid = new Int32Array(cx * cy * cz).fill(-1), pos = [];
  const corners = [[0, 0, 0], [1, 0, 0], [0, 1, 0], [1, 1, 0], [0, 0, 1], [1, 0, 1], [0, 1, 1], [1, 1, 1]];
  const edges = [[0, 1], [2, 3], [4, 5], [6, 7], [0, 2], [1, 3], [4, 6], [5, 7], [0, 4], [1, 5], [2, 6], [3, 7]];
  const v = new Float32Array(8);
  for (let k = 0; k < cz; k++) for (let j = 0; j < cy; j++) for (let i = 0; i < cx; i++) {
    let mask = 0;
    for (let c = 0; c < 8; c++) { const [a, b, d] = corners[c]; v[c] = F[I(i + a, j + b, k + d)]; if (v[c] < 0) mask |= 1 << c; }
    if (mask === 0 || mask === 255) continue;
    let sx = 0, sy = 0, sz = 0, n = 0;
    for (const [e0, e1] of edges) {
      if ((v[e0] < 0) === (v[e1] < 0)) continue;
      const t = v[e0] / (v[e0] - v[e1]), A = corners[e0], B = corners[e1];
      sx += A[0] + (B[0] - A[0]) * t; sy += A[1] + (B[1] - A[1]) * t; sz += A[2] + (B[2] - A[2]) * t; n++;
    }
    vid[C(i, j, k)] = pos.length / 3;
    pos.push(x0 + (i + sx / n) * h, y0 + (j + sy / n) * h, z0 + (k + sz / n) * h);
  }
  const idx = [];
  const quad = (a, b, c, d, flip) => { if (a < 0 || b < 0 || c < 0 || d < 0) return; if (flip) idx.push(a, c, b, a, d, c); else idx.push(a, b, c, a, c, d); };
  for (let k = 1; k < cz; k++) for (let j = 1; j < cy; j++) for (let i = 1; i < cx; i++) {
    const inside = F[I(i, j, k)] < 0;
    if (inside !== (F[I(i + 1, j, k)] < 0)) quad(vid[C(i, j - 1, k - 1)], vid[C(i, j, k - 1)], vid[C(i, j, k)], vid[C(i, j - 1, k)], !inside);   // x edge
    if (inside !== (F[I(i, j + 1, k)] < 0)) quad(vid[C(i - 1, j, k - 1)], vid[C(i - 1, j, k)], vid[C(i, j, k)], vid[C(i, j, k - 1)], !inside);   // y edge
    if (inside !== (F[I(i, j, k + 1)] < 0)) quad(vid[C(i - 1, j - 1, k)], vid[C(i, j - 1, k)], vid[C(i, j, k)], vid[C(i - 1, j, k)], !inside);   // z edge
  }
  return { pos: new Float32Array(pos), idx: new Uint32Array(idx) };
}

// ---- smoothing (Taubin: shrink-free) + snapping back onto the surface
export function relax(field, mesh, iters = 4) {
  const { pos, idx } = mesh, n = pos.length / 3;
  const nb = Array.from({ length: n }, () => new Set());
  for (let t = 0; t < idx.length; t += 3) for (let e = 0; e < 3; e++) { const a = idx[t + e], b = idx[t + (e + 1) % 3]; nb[a].add(b); nb[b].add(a); }
  const adj = nb.map(s => Int32Array.from(s));
  const tmp = new Float32Array(pos.length);
  const pass = f => {
    for (let i = 0; i < n; i++) {
      const a = adj[i]; if (!a.length) { tmp.set(pos.subarray(i * 3, i * 3 + 3), i * 3); continue; }
      let sx = 0, sy = 0, sz = 0;
      for (const j of a) { sx += pos[j * 3]; sy += pos[j * 3 + 1]; sz += pos[j * 3 + 2]; }
      const m = a.length;
      tmp[i * 3] = pos[i * 3] + f * (sx / m - pos[i * 3]); tmp[i * 3 + 1] = pos[i * 3 + 1] + f * (sy / m - pos[i * 3 + 1]); tmp[i * 3 + 2] = pos[i * 3 + 2] + f * (sz / m - pos[i * 3 + 2]);
    }
    pos.set(tmp);
  };
  const snap = () => {
    for (let i = 0; i < n; i++) for (let it = 0; it < 2; it++) {
      const x = pos[i * 3], y = pos[i * 3 + 1], z = pos[i * 3 + 2], d = field.eval(x, y, z);
      if (Math.abs(d) < 1e-5) break;
      const g = field.grad(x, y, z), gg = g[0] * g[0] + g[1] * g[1] + g[2] * g[2];
      if (gg < 1e-8) break;
      pos[i * 3] -= d * g[0] / gg; pos[i * 3 + 1] -= d * g[1] / gg; pos[i * 3 + 2] -= d * g[2] / gg;
    }
  };
  for (let k = 0; k < iters; k++) { pass(.5); pass(-.53); }
  snap();
  pass(.3); pass(-.31);
  mesh.adj = adj;
  return mesh;
}
