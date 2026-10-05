using System.Collections.Generic;
using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 시제품용 둥근 메시를 코드로 만든다. 모든 모양은 단면(반지름, 높이)을 돌려 만드는 회전체.
    /// 같은 위치의 점을 두 번 넣으면 그 자리는 각진 모서리가 된다.
    /// </summary>
    public static class MeshFactory
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        public static Mesh Lathe(string key, IList<Vector2> profile, int segments = 32)
        {
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;

            int rings = profile.Count;
            var verts = new Vector3[rings * (segments + 1)];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            var tris = new List<int>((rings - 1) * segments * 6);

            var n2 = new Vector2[rings];
            for (int i = 0; i < rings; i++)
            {
                Vector2 acc = Vector2.zero;
                if (i > 0) acc += SegNormal(profile[i - 1], profile[i]);
                if (i < rings - 1) acc += SegNormal(profile[i], profile[i + 1]);
                // 중복점(각진 모서리): 한쪽 선분만 사용
                if (i > 0 && (profile[i] - profile[i - 1]).sqrMagnitude < 1e-10f && i < rings - 1)
                    acc = SegNormal(profile[i], profile[i + 1]);
                else if (i < rings - 1 && (profile[i + 1] - profile[i]).sqrMagnitude < 1e-10f && i > 0)
                    acc = SegNormal(profile[i - 1], profile[i]);
                n2[i] = acc.sqrMagnitude > 1e-12f ? acc.normalized : Vector2.up;
            }

            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j <= segments; j++)
                {
                    float a = (j / (float)segments) * Mathf.PI * 2f;
                    float c = Mathf.Cos(a), s = Mathf.Sin(a);
                    int idx = i * (segments + 1) + j;
                    verts[idx] = new Vector3(profile[i].x * c, profile[i].y, profile[i].x * s);
                    norms[idx] = new Vector3(n2[i].x * c, n2[i].y, n2[i].x * s).normalized;
                    uvs[idx] = new Vector2(j / (float)segments, i / (float)(rings - 1));
                }
            }

            for (int i = 0; i < rings - 1; i++)
            {
                if ((profile[i + 1] - profile[i]).sqrMagnitude < 1e-10f) continue;
                for (int j = 0; j < segments; j++)
                {
                    int a = i * (segments + 1) + j;
                    int b = (i + 1) * (segments + 1) + j;
                    tris.Add(a); tris.Add(b); tris.Add(b + 1);
                    tris.Add(a); tris.Add(b + 1); tris.Add(a + 1);
                }
            }

            var m = new Mesh { name = key };
            m.vertices = verts;
            m.normals = norms;
            m.uv = uvs;
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            cache[key] = m;
            return m;
        }

        static Vector2 SegNormal(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-12f) return Vector2.zero;
            return new Vector2(d.y, -d.x).normalized;
        }

        static void Arc(List<Vector2> p, Vector2 center, float radius, float fromDeg, float toDeg, int steps)
        {
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)steps) * Mathf.Deg2Rad;
                p.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }

        /// <summary>반지름 0.5 구 (지름 1).</summary>
        public static Mesh Sphere()
        {
            var p = new List<Vector2>();
            Arc(p, Vector2.zero, 0.5f, -90f, 90f, 20);
            p[0] = new Vector2(0f, -0.5f);
            p[p.Count - 1] = new Vector2(0f, 0.5f);
            return Lathe("sphere", p, 36);
        }

        /// <summary>y축 캡슐. 전체 높이 1, 반지름 radius.</summary>
        public static Mesh Capsule(float radius = 0.25f)
        {
            var p = new List<Vector2>();
            float half = 0.5f - radius;
            Arc(p, new Vector2(0f, -half), radius, -90f, 0f, 8);
            Arc(p, new Vector2(0f, half), radius, 0f, 90f, 8);
            p[0] = new Vector2(0f, -0.5f);
            p[p.Count - 1] = new Vector2(0f, 0.5f);
            return Lathe("capsule" + radius.ToString("0.000"), p, 24);
        }

        /// <summary>끝이 둥근 원뿔 (귀). 밑면 반지름 0.5, 높이 1, 밑면이 y=0.</summary>
        public static Mesh RoundCone()
        {
            var p = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(0.42f, 0f), new Vector2(0.5f, 0.06f),
                new Vector2(0.38f, 0.4f), new Vector2(0.22f, 0.72f), new Vector2(0.1f, 0.92f),
                new Vector2(0.04f, 0.985f), new Vector2(0f, 1f)
            };
            return Lathe("roundcone", p, 24);
        }

        /// <summary>모서리가 둥근 원기둥. 반지름 0.5, 높이 1, 밑면 y=0. bevel은 지름 대비 비율.</summary>
        public static Mesh RoundedCylinder(float bevel = 0.12f, float topDip = 0f)
        {
            string key = "rcyl" + bevel.ToString("0.000") + "_" + topDip.ToString("0.000");
            var p = new List<Vector2> { new Vector2(0f, 0f) };
            float b = bevel;
            Arc(p, new Vector2(0.5f - b, b), b, -90f, 0f, 6);
            Arc(p, new Vector2(0.5f - b, 1f - b), b, 0f, 90f, 6);
            if (topDip > 0f)
            {
                p.Add(new Vector2(0.3f, 1f - topDip * 0.5f));
                p.Add(new Vector2(0.15f, 1f - topDip * 0.9f));
                p.Add(new Vector2(0f, 1f - topDip));
            }
            else p.Add(new Vector2(0f, 1f));
            return Lathe(key, p, 40);
        }

        /// <summary>밥그릇. 바깥 반지름 0.5, 높이 1 기준.</summary>
        public static Mesh Bowl()
        {
            var p = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(0.34f, 0f), new Vector2(0.38f, 0.04f),
                new Vector2(0.46f, 0.45f), new Vector2(0.5f, 0.85f), new Vector2(0.49f, 0.97f),
                new Vector2(0.45f, 1f), new Vector2(0.41f, 0.95f), new Vector2(0.37f, 0.55f),
                new Vector2(0.28f, 0.3f), new Vector2(0f, 0.27f)
            };
            return Lathe("bowl", p, 40);
        }

        /// <summary>섬 땅. 위가 평평하고 옆이 둥글게 흙으로 내려감. 반지름 0.5, 윗면 y=0.</summary>
        public static Mesh IslandTop()
        {
            var p = new List<Vector2> { new Vector2(0f, -0.06f), new Vector2(0.47f, -0.06f) };
            Arc(p, new Vector2(0.47f, -0.03f), 0.03f, -90f, 90f, 6);
            p.Add(new Vector2(0f, 0f));
            return Lathe("islandtop", p, 64);
        }

        public static Mesh IslandSoil()
        {
            var p = new List<Vector2>
            {
                new Vector2(0f, -0.5f), new Vector2(0.18f, -0.48f), new Vector2(0.34f, -0.36f),
                new Vector2(0.44f, -0.2f), new Vector2(0.49f, -0.08f), new Vector2(0.5f, -0.03f),
                new Vector2(0.49f, 0f), new Vector2(0f, 0f)
            };
            return Lathe("islandsoil", p, 64);
        }

        public static Mesh Quad()
        {
            if (cache.TryGetValue("quad", out var q) && q != null) return q;
            var m = new Mesh { name = "quad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            cache["quad"] = m;
            return m;
        }
    }
}
