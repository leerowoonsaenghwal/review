using System.Collections.Generic;
using UnityEngine;

namespace CatIsland
{
    /// <summary>섬 위의 장애물 하나 (바닥 평면 XZ 기준). 원 또는 돌려진 직사각형.</summary>
    public class Obstacle
    {
        public string name;
        public Transform item;      // 고양이가 "쓰러 가는" 물건이면 그 Transform (그때만 좁게 다가간다)
        public Vector3 center;
        public float radius;        // 원
        public Vector2 half;        // 직사각형 반 크기 (x, z). 0 이면 원
        public float yaw;           // 직사각형 회전 (도)

        public bool IsBox => half.x > 0f;

        /// <summary>점까지의 거리 (밖이면 +, 안이면 -).</summary>
        public float Distance(Vector3 p)
        {
            Vector3 d = p - center; d.y = 0f;
            if (!IsBox) return d.magnitude - radius;
            Vector3 l = Quaternion.Euler(0f, -yaw, 0f) * d;
            Vector2 q = new Vector2(Mathf.Abs(l.x) - half.x, Mathf.Abs(l.z) - half.y);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        }
    }

    /// <summary>
    /// 길찾기: 섬을 칸으로 나누고 장애물을 고양이 몸 크기만큼 부풀려 막은 뒤 A* 로 길을 찾고 곧게 편다.
    /// 고양이가 물건을 뚫고 지나가지 않게 한다.
    /// </summary>
    public class NavGrid
    {
        public const float Cell = 0.2f;
        public const float BodyClearance = 0.5f;   // 고양이 몸(약 1 m)이 돌 때 쓸고 지나가는 반경
        public const float UseClearance = 0.22f;   // 쓰러 가는 물건에는 몸 반폭만큼만

        public readonly List<Obstacle> obstacles = new List<Obstacle>();
        readonly float walkRadius;
        readonly int n;
        readonly Vector3 origin;

        public NavGrid(float walkRadius)
        {
            this.walkRadius = walkRadius;
            n = Mathf.CeilToInt(walkRadius * 2f / Cell) + 1;
            origin = new Vector3(-walkRadius, 0f, -walkRadius);
        }

        public Obstacle Add(Obstacle o) { obstacles.Add(o); return o; }

        /// <summary>점이 막혔는가. using: 지금 쓰러 가는 물건 (좁은 여유).</summary>
        public bool Blocked(Vector3 p, Transform using_ = null, float extra = 0f)
        {
            p.y = 0f;
            if (p.magnitude > walkRadius) return true;
            foreach (var o in obstacles)
            {
                float clear = (o.item != null && o.item == using_ ? UseClearance : BodyClearance) + extra;
                if (o.Distance(p) < clear) return true;
            }
            return false;
        }

        /// <summary>가장 가까운 막히지 않은 점 (목적지가 물건 안이면 바깥으로).</summary>
        public Vector3 NearestFree(Vector3 p, Transform using_ = null)
        {
            p.y = 0f;
            if (!Blocked(p, using_)) return p;
            for (float r = Cell; r < 3f; r += Cell)
                for (int a = 0; a < 24; a++)
                {
                    float ang = a * Mathf.PI * 2f / 24f;
                    var q = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                    if (!Blocked(q, using_)) return q;
                }
            return p;
        }

        /// <summary>두 점 사이 직선이 막힘 없이 지나가는가.</summary>
        public bool Clear(Vector3 a, Vector3 b, Transform using_ = null)
        {
            float len = Vector3.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / (Cell * 0.5f)));
            for (int i = 1; i <= steps; i++)
                if (Blocked(Vector3.Lerp(a, b, i / (float)steps), using_)) return false;
            return true;
        }

        Vector2Int ToCell(Vector3 p) => new Vector2Int(Mathf.Clamp(Mathf.RoundToInt((p.x - origin.x) / Cell), 0, n - 1), Mathf.Clamp(Mathf.RoundToInt((p.z - origin.z) / Cell), 0, n - 1));
        Vector3 ToWorld(Vector2Int c) => origin + new Vector3(c.x * Cell, 0f, c.y * Cell);

        /// <summary>from → to 길 (곧게 편 지점들, to 포함). 못 찾으면 null.</summary>
        public List<Vector3> FindPath(Vector3 from, Vector3 to, Transform using_ = null)
        {
            from.y = 0f; to.y = 0f;
            if (Clear(from, to, using_)) return new List<Vector3> { to };
            var start = ToCell(from);
            var goal = ToCell(to);
            var blocked = new bool[n, n];
            for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                    blocked[x, z] = Blocked(ToWorld(new Vector2Int(x, z)), using_);
            blocked[start.x, start.y] = false;   // (이미 장애물 가까이 서 있어도 빠져나올 수 있게)
            blocked[goal.x, goal.y] = false;

            var g = new float[n, n];
            var came = new Vector2Int[n, n];
            var closed = new bool[n, n];
            for (int x = 0; x < n; x++) for (int z = 0; z < n; z++) g[x, z] = float.MaxValue;
            g[start.x, start.y] = 0f;
            var open = new List<Vector2Int> { start };
            Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1) };
            bool found = false;
            while (open.Count > 0)
            {
                int bi = 0; float bf = float.MaxValue;
                for (int i = 0; i < open.Count; i++)
                {
                    var c = open[i];
                    float f = g[c.x, c.y] + Vector2Int.Distance(c, goal);
                    if (f < bf) { bf = f; bi = i; }
                }
                var cur = open[bi];
                open.RemoveAt(bi);
                if (cur == goal) { found = true; break; }
                if (closed[cur.x, cur.y]) continue;
                closed[cur.x, cur.y] = true;
                foreach (var dv in dirs)
                {
                    var nb = cur + dv;
                    if (nb.x < 0 || nb.y < 0 || nb.x >= n || nb.y >= n || blocked[nb.x, nb.y] || closed[nb.x, nb.y]) continue;
                    if (dv.x != 0 && dv.y != 0 && (blocked[cur.x + dv.x, cur.y] || blocked[cur.x, cur.y + dv.y])) continue;   // (모서리 자르기 금지)
                    float ng = g[cur.x, cur.y] + dv.magnitude;
                    if (ng < g[nb.x, nb.y]) { g[nb.x, nb.y] = ng; came[nb.x, nb.y] = cur; open.Add(nb); }
                }
            }
            if (!found) return null;

            var cells = new List<Vector3>();
            for (var c = goal; c != start; c = came[c.x, c.y]) cells.Add(ToWorld(c));
            cells.Reverse();
            cells[cells.Count - 1] = to;
            // 곧게 펴기: 보이는 가장 먼 지점으로 바로 간다
            var path = new List<Vector3>();
            Vector3 at = from;
            int k = 0;
            while (k < cells.Count)
            {
                int far = k;
                for (int j = cells.Count - 1; j > k; j--) if (Clear(at, cells[j], using_)) { far = j; break; }
                path.Add(cells[far]);
                at = cells[far];
                k = far + 1;
            }
            return path;
        }
    }
}
