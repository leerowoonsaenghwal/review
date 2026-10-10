using System.Collections.Generic;
using CatIsland.Game;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatIsland
{
    /// <summary>
    /// 깔린 데크 타일을 섬 풀밭 위에 그린다: 무늬(그리고 구역)마다 메시 하나 (타일이 많아도 그리기 횟수가 늘지 않는다).
    /// 무늬는 세상 좌표로 깔아 이웃 타일끼리 판자 결·줄눈이 이어지고, 칸 경계와 무늬 경계가 맞는다.
    /// 타일 덩어리 바깥 가장자리에만 얇은 옆면(두께)을 붙인다. 고양이 발소리 재질도 여기서 알려 준다 (SurfaceAt).
    /// </summary>
    public class FloorTiles : MonoBehaviour
    {
        public const float Top = .012f;      // (타일 윗면: 풀밭보다 1.2 cm 위)
        const float Skirt = .03f;            // (옆면이 풀 속으로 내려가는 깊이)
        public static FloorTiles Instance { get; private set; }
        static readonly Dictionary<(Zone, int, int), string> map = new Dictionary<(Zone, int, int), string>();
        readonly Dictionary<(string, Zone), MeshFilter> parts = new Dictionary<(string, Zone), MeshFilter>();
        string lastKey = "";

        public static FloorTiles Create(Transform parent)
        {
            var go = new GameObject("FloorTiles"); go.transform.SetParent(parent, false);
            Instance = go.AddComponent<FloorTiles>(); map.Clear(); return Instance;
        }
        void OnDestroy() { if (Instance == this) { Instance = null; map.Clear(); } }

        /// <summary>저장의 깔린 타일과 맞춘다 (바뀐 것이 있을 때만 다시 만든다).</summary>
        public void Sync(List<FloorTile> floor)
        {
            var sb = new System.Text.StringBuilder(floor.Count * 12);
            foreach (var t in floor) sb.Append(t.id).Append((int)t.zone).Append(',').Append(t.x).Append(',').Append(t.z).Append(';');
            string key = sb.ToString(); if (key == lastKey) return; lastKey = key;
            map.Clear(); foreach (var t in floor) map[(t.zone, t.x, t.z)] = t.id;
            var groups = new Dictionary<(string, Zone), List<FloorTile>>();
            foreach (var t in floor) { var k = (t.id, t.zone); if (!groups.TryGetValue(k, out var l)) groups[k] = l = new List<FloorTile>(); l.Add(t); }
            foreach (var kv in parts) if (!groups.ContainsKey(kv.Key) && kv.Value) kv.Value.gameObject.SetActive(false);
            foreach (var kv in groups)
            {
                if (!parts.TryGetValue(kv.Key, out var mf) || !mf) parts[kv.Key] = mf = MakePart(kv.Key.Item1, kv.Key.Item2);
                mf.gameObject.SetActive(true);
                var old = mf.sharedMesh; mf.sharedMesh = BuildMesh(kv.Value, kv.Key.Item2); if (old) Destroy(old);
            }
        }

        MeshFilter MakePart(string id, Zone zone)
        {
            var go = new GameObject($"Tiles_{id}_{zone}"); go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>(); var mr = go.AddComponent<MeshRenderer>();
            var m = Materials.Painted("Tile_" + id + "_" + zone, PaintedTextures.Tile(id), Vector2.one / WorldSync.Cell, true);
            var o = WorldSync.CellToWorld(zone, 0, 0);   // (칸 (0,0) 의 모서리에서 무늬가 시작되게)
            m.SetTextureOffset("_BaseMap", new Vector2(-o.x / WorldSync.Cell, -o.z / WorldSync.Cell));
            m.SetFloat("_RimStrength", .08f);
            mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true;
            return mf;
        }

        static Mesh BuildMesh(List<FloorTile> tiles, Zone zone)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            float h = WorldSync.Cell * .5f;
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm)
            {
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); for (int k = 0; k < 4; k++) n.Add(nrm);
                tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); tri.Add(i); tri.Add(i + 2); tri.Add(i + 3);
            }
            foreach (var t in tiles)
            {
                var c = WorldSync.CellToWorld(zone, t.x + .5f, t.z + .5f);
                float x0 = c.x - h, x1 = c.x + h, z0 = c.z - h, z1 = c.z + h, y = Top, yb = -Skirt;
                Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up);
                // (옆면: 이웃 칸에 아무 타일도 없을 때만 - 타일 덩어리의 바깥 테두리)
                if (!map.ContainsKey((zone, t.x, t.z - 1))) Quad(new Vector3(x1, y, z0), new Vector3(x1, yb, z0), new Vector3(x0, yb, z0), new Vector3(x0, y, z0), Vector3.back);
                if (!map.ContainsKey((zone, t.x, t.z + 1))) Quad(new Vector3(x0, y, z1), new Vector3(x0, yb, z1), new Vector3(x1, yb, z1), new Vector3(x1, y, z1), Vector3.forward);
                if (!map.ContainsKey((zone, t.x - 1, t.z))) Quad(new Vector3(x0, y, z0), new Vector3(x0, yb, z0), new Vector3(x0, yb, z1), new Vector3(x0, y, z1), Vector3.left);
                if (!map.ContainsKey((zone, t.x + 1, t.z))) Quad(new Vector3(x1, y, z1), new Vector3(x1, yb, z1), new Vector3(x1, yb, z0), new Vector3(x1, y, z0), Vector3.right);
            }
            var mesh = new Mesh { name = "FloorTiles" };
            if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>그 자리의 타일 칸 (없으면 null): 구역 격자 안이면 칸 좌표.</summary>
        public static bool CellAt(Vector3 p, out Zone zone, out int x, out int z)
        {
            foreach (var zn in new[] { Zone.Indoor, Zone.Yard })
            {
                var o = WorldSync.CellToWorld(zn, 0, 0); var (w, hh) = CatIsland.Game.Game.GridSize(zn);
                int cx = Mathf.FloorToInt((p.x - o.x) / WorldSync.Cell), cz = Mathf.FloorToInt((p.z - o.z) / WorldSync.Cell);
                if (cx >= 0 && cz >= 0 && cx < w && cz < hh) { zone = zn; x = cx; z = cz; return true; }
            }
            zone = Zone.Indoor; x = z = -1; return false;
        }

        /// <summary>그 자리에 타일이 깔려 있으면 발소리 재질.</summary>
        public static Surface? SurfaceAt(Vector3 p)
        {
            if (map.Count == 0 || !CellAt(p, out var zone, out int x, out int z)) return null;
            if (!map.TryGetValue((zone, x, z), out var id)) return null;
            var d = Catalog.Tile(id); return d != null ? (Surface)d.surface : Surface.Wood;
        }

        /// <summary>테스트용: 그 칸에 그려진 무늬.</summary>
        public static string IdAt(Zone zone, int x, int z) => map.TryGetValue((zone, x, z), out var id) ? id : null;
    }
}
