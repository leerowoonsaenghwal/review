using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatIsland.Game;
using CatIsland.UI;
using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 게임 규칙(Game)과 3D 섬을 잇는다: 저장에 놓인 용품을 섬에 세우고(격자 → 세상 좌표), 길찾기 장애물로 등록하고,
    /// 사진을 찍어 기기 안에 저장한다. (방석·사료 그릇·캣타워 1단은 고양이 동작과 묶인 장면 용품이라 GameBootstrap 이 세운다.)
    /// </summary>
    public class WorldSync : IWorldHooks
    {
        public const float Cell = 0.6f;
        static readonly HashSet<string> SceneItems = new HashSet<string> { "food_bowl", "cushion", "cat_tower_1" };
        readonly GameBootstrap boot; readonly Transform root;
        readonly Dictionary<Placement, GameObject> spawned = new Dictionary<Placement, GameObject>();
        readonly Dictionary<Placement, Obstacle> obstacles = new Dictionary<Placement, Obstacle>();

        public WorldSync(GameBootstrap boot)
        {
            this.boot = boot; root = new GameObject("PlacedItems").transform;
        }

        /// <summary>격자 칸 (x, z) 의 세상 좌표 (용품 바닥 가운데). 집 안 격자는 마루 가운데에 맞춘다.</summary>
        public static Vector3 CellToWorld(Zone zone, float x, float z)
        {
            var (w, h) = CatIsland.Game.Game.GridSize(zone);
            var c = zone == Zone.Indoor ? IslandBuilder.DeckCenter : IslandBuilder.YardCenter;
            return c + new Vector3((x - w / 2f) * Cell, 0f, (z - h / 2f) * Cell);
        }
        public static Vector3 PlacementCenter(Placement p)
        {
            var d = Catalog.Item(p.item); int w = d.w, h = d.h; if (p.rot % 2 == 1) (w, h) = (h, w);
            return CellToWorld(p.zone, p.x + w / 2f, p.z + h / 2f);
        }

        public void Refresh()
        {
            var g = boot.Logic; if (g?.S == null) return;
            boot.SyncCats();
            foreach (var p in spawned.Keys.ToList()) if (!g.S.placed.Contains(p)) { UnityEngine.Object.Destroy(spawned[p]); spawned.Remove(p); if (obstacles.TryGetValue(p, out var o)) { boot.Nav.obstacles.Remove(o); obstacles.Remove(p); } }
            foreach (var p in g.S.placed)
            {
                if (spawned.ContainsKey(p) || SceneItems.Contains(p.item) && FirstOf(g, p.item) == p) continue;
                var d = Catalog.Item(p.item); var pos = PlacementCenter(p); float yaw = 180f - p.rot * 90f;
                var go = ItemLoader.Spawn(d.model, root, pos + new Vector3(0, .004f, 0), yaw) ?? Placeholder(d, pos, yaw);
                spawned[p] = go;
                int w = d.w, h = d.h; if (p.rot % 2 == 1) (w, h) = (h, w);
                var ob = boot.Nav.Add(new Obstacle { name = d.id, item = go.transform, center = pos, half = new Vector2(w * Cell * .45f, h * Cell * .45f), yaw = 0 });
                obstacles[p] = ob;
            }
        }
        static Placement FirstOf(CatIsland.Game.Game g, string item) => g.S.placed.FirstOrDefault(x => x.item == item);

        /// <summary>모델이 아직 없는 용품: 크기에 맞는 둥근 상자 (용품 60종 모델이 다 생기면 쓰이지 않는다).</summary>
        static GameObject Placeholder(ItemDef d, Vector3 pos, float yaw)
        {
            var go = new GameObject("Item_" + d.id); go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var c = d.theme switch { "딸기" => Palette.StrawberryMilk, "바다" => Palette.Hex("6fc3ec"), "숲" => Palette.Hex("7cc85b"), _ => Palette.Butter };
            Shapes.Make(go.transform, "Body", MeshFactory.RoundedCylinder(.06f), c, new Vector3(0, .12f, 0), new Vector3(d.w * Cell * .8f, .24f, d.h * Cell * .8f));
            return go;
        }

        public void BeginPlace(string item, Action<bool> done)
        {
            // 첫 단계: 마루에서 고양이 자리를 피한 가장 가까운 빈 칸에 놓는다 (손가락으로 옮기는 꾸미기 모드는 Decorate 에서)
            var g = boot.Logic; var d = Catalog.Item(item); var zone = d.zone;
            var (w, h) = CatIsland.Game.Game.GridSize(zone);
            var cells = from z in Enumerable.Range(0, h) from x in Enumerable.Range(0, w) orderby (x - w / 2f) * (x - w / 2f) + (z - h / 2f) * (z - h / 2f) descending select (x, z);
            foreach (var (x, z) in cells) for (int r = 0; r < 4; r++) if (g.Place(item, zone, x, z, r)) { Refresh(); done(true); return; }
            done(false);
        }

        public void ShowZone(Zone z) { if (boot.IslandCam) boot.IslandCam.ShowZone(z); }

        public void Capture(Action<string> saved)
        {
            var cam = boot.IslandCam.GetComponent<Camera>();
            int W = Mathf.Max(720, Screen.width), H = Mathf.Max(1280, Screen.height);
            var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 }; var prev = cam.targetTexture;
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
            RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = null;
            var dir = Path.Combine(Application.persistentDataPath, "photos"); Directory.CreateDirectory(dir);
            var name = $"photo_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex); rt.Release(); UnityEngine.Object.Destroy(rt);
            saved(name);
        }

        public Vector2? CatScreenPos(string uid)
        {
            var cam = boot.IslandCam ? boot.IslandCam.GetComponent<Camera>() : null; if (!cam || !boot.Cat) return null;
            var p = cam.WorldToScreenPoint(boot.Cat.transform.position + Vector3.up * 1.1f); return p.z > 0 ? (Vector2?)new Vector2(p.x, p.y) : null;
        }
    }
}
