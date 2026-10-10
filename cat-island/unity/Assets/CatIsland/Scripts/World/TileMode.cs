using System;
using System.Collections.Generic;
using System.Linq;
using CatIsland.Game;
using CatIsland.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CatIsland
{
    /// <summary>
    /// 바닥 깔기 모드: 산 데크 타일을 풀밭 격자 칸에 손가락으로 쓸어 깐다 (깔기), 깐 타일을 걷는다 (걷기),
    /// 넓은 섬을 손가락으로 끌어 옮겨 본다 (화면). 격자선이 보이고, 카메라는 고양이를 따라가지 않는다.
    /// 아래 막대: 무늬(누르면 가진 무늬를 차례로) · 깔기 · 걷기 · 화면 · 다 했어요.
    /// </summary>
    public class TileMode : MonoBehaviour
    {
        public enum Tool { Lay, Lift, Pan }
        public static TileMode Active { get; private set; }
        public string TileId { get; private set; }
        public Tool Current { get; private set; } = Tool.Lay;
        public Zone Zone { get; private set; } = Zone.Indoor;
        public int Changed { get; private set; }   // (이번에 깔고 걷은 칸 수)
        CatIsland.Game.Game g; GameBootstrap boot; Action done;
        RectTransform bar; Text tileLabel; RawImage tileImg; readonly Dictionary<Tool, Button> toolBtns = new Dictionary<Tool, Button>();
        GameObject grid; bool dragging; Vector2 lastScreen; (int x, int z)? lastCell;

        public static TileMode Begin(GameBootstrap boot, string tileId, Action done = null)
        {
            if (Active) Active.Finish();
            if (PlaceMode.Active) PlaceMode.Active.Finish(false);
            var m = new GameObject("TileMode").AddComponent<TileMode>();
            m.boot = boot; m.g = boot.Logic; m.TileId = tileId ?? Catalog.Tiles.Select(t => t.id).FirstOrDefault(id => boot.Logic.Tiles(id) > 0) ?? Catalog.StarterTile; m.done = done;
            boot.Router.enabled = false;
            if (boot.UI) boot.UI.Root.Find("Bottom")?.gameObject.SetActive(false);
            m.Build(); m.ShowZone(Zone.Indoor);
            Active = m; boot.UI?.Refresh(); return m;
        }

        void ShowZone(Zone z)
        {
            Zone = z; var cam = boot.IslandCam;
            cam.Manual = true; cam.ManualDist = 13f;
            cam.ManualFocus = WorldSync.CellToWorld(z, CatIsland.Game.Game.GridSize(z).w / 2f, CatIsland.Game.Game.GridSize(z).h / 2f - 2f) + Vector3.up * .3f;
            if (grid) Destroy(grid); grid = MakeGrid(z);
        }

        /// <summary>격자선: 칸 경계마다 얇은 크림색 줄 (깔기 모드에서만).</summary>
        static GameObject MakeGrid(Zone z)
        {
            var (w, h) = CatIsland.Game.Game.GridSize(z); var o = WorldSync.CellToWorld(z, 0, 0); float c = WorldSync.Cell, th = .012f, y = FloorTiles.Top + .012f;
            var v = new List<Vector3>(); var tri = new List<int>();
            void Strip(Vector3 a, Vector3 b, Vector3 side) { int i = v.Count; v.Add(a - side); v.Add(a + side); v.Add(b + side); v.Add(b - side); tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
            for (int x = 0; x <= w; x++) Strip(o + new Vector3(x * c, y, 0), o + new Vector3(x * c, y, h * c), new Vector3(-th, 0, 0));   // (세로줄: 면이 위를 보게 옆 방향을 반대로)
            for (int zz = 0; zz <= h; zz++) Strip(o + new Vector3(0, y, zz * c), o + new Vector3(w * c, y, zz * c), new Vector3(0, 0, th));
            var mesh = new Mesh { name = "TileGrid" }; mesh.SetVertices(v); mesh.SetTriangles(tri, 0); mesh.SetNormals(v.Select(_ => Vector3.up).ToList()); mesh.RecalculateBounds();
            var go = new GameObject("TileGrid"); go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); var m = new Material(Materials.Soft(new Color(1f, .98f, .9f))); m.SetFloat("_Emission", .8f); mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        void Build()
        {
            if (!boot.UI) return;
            bar = Kit.Rect(boot.UI.Root, "TileBar"); Kit.Anchor(bar, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 12), new Vector2(370, 126));
            Kit.VList(bar, 6);
            // 위 줄: 지금 무늬 (누르면 다음 무늬) + 남은 장 수
            var top = Kit.Box(bar, "TileRow", Theme.Cream, 18); Kit.Size(top.rectTransform, -1, 56);
            var line = Kit.Box(top.transform, "Line", Theme.Cocoa, 18, 2); Kit.Fill(line.rectTransform); line.raycastTarget = false;
            var tb = top.gameObject.AddComponent<Button>(); tb.transition = Selectable.Transition.None; tb.onClick.AddListener(NextTile); top.gameObject.AddComponent<Press>();
            var ig = new GameObject("TileImg", typeof(RectTransform)); ig.transform.SetParent(top.transform, false); tileImg = ig.AddComponent<RawImage>(); tileImg.raycastTarget = false;
            Kit.Anchor(tileImg.rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(10, 0), new Vector2(40, 40));
            tileLabel = Kit.Label(top.transform, "", Theme.Body, null, TextAnchor.MiddleLeft); Kit.Fill(tileLabel.rectTransform, 60, 2, 12, 2); tileLabel.raycastTarget = false;
            // 아래 줄: 도구 + 다 했어요
            var row = Kit.Rect(bar, "Tools"); Kit.HList(row, 6); Kit.Size(row, -1, 56);
            toolBtns[Tool.Lay] = Kit.Btn(row, "깔기", () => SetTool(Tool.Lay), Kit.Style.Primary, UIIcon.Plus); Kit.Size(toolBtns[Tool.Lay], 82, 52);
            toolBtns[Tool.Lift] = Kit.Btn(row, "걷기", () => SetTool(Tool.Lift), Kit.Style.Secondary, UIIcon.Trash); Kit.Size(toolBtns[Tool.Lift], 82, 52);
            toolBtns[Tool.Pan] = Kit.Btn(row, "화면", () => SetTool(Tool.Pan), Kit.Style.Secondary, UIIcon.Paw); Kit.Size(toolBtns[Tool.Pan], 82, 52);
            Kit.Size(Kit.Btn(row, "다 했어요", Finish, Kit.Style.Primary, UIIcon.Check), 100, 52);
            if (g.S.zonesUnlocked.Contains(1))
            {
                var zb = Kit.IconBtn(boot.UI.Root, UIIcon.Fence, Str.Yard, () => ShowZone(Zone == Zone.Indoor ? Zone.Yard : Zone.Indoor), 56);
                var zr = (RectTransform)zb.transform.parent; zr.SetParent(bar, false); zr.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                Kit.Anchor(zr, new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 8), new Vector2(56, 74));
            }
            RefreshBar();
        }

        void RefreshBar()
        {
            if (!bar) return;
            var d = Catalog.Tile(TileId); tileImg.texture = PaintedTextures.Tile(TileId);
            tileLabel.text = $"{d?.ko}  ·  {g.Tiles(TileId)}장 남음";
            foreach (var kv in toolBtns)
            {
                bool on = kv.Key == Current; var img = kv.Value.GetComponent<Image>();
                if (img) img.color = on ? Theme.StrawberryLight : Theme.Cream;
            }
        }

        /// <summary>가진 무늬를 차례로 (깐 것밖에 없으면 그대로).</summary>
        public void NextTile()
        {
            var owned = Catalog.Tiles.Where(t => g.Tiles(t.id) > 0 || t.id == TileId).Select(t => t.id).ToList();
            int i = owned.IndexOf(TileId); TileId = owned[(i + 1) % owned.Count]; SetTool(Tool.Lay);
        }

        public void SetTool(Tool t) { Current = t; lastCell = null; RefreshBar(); }   // (도구를 바꾸면 손가락 자취를 잇지 않는다)

        void Update()
        {
            Vector2? pos = null;
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.isPressed) pos = ts.primaryTouch.position.ReadValue();
            else if (Mouse.current != null && Mouse.current.leftButton.isPressed) pos = Mouse.current.position.ReadValue();
            if (pos == null) { dragging = false; lastCell = null; return; }
            if (!dragging)
            {
                if (GameUI.PointerOverUI(ts != null && ts.primaryTouch.press.isPressed ? ts.primaryTouch.touchId.ReadValue() : -1)) return;
                dragging = true; lastScreen = pos.Value; lastCell = null;
            }
            if (Current == Tool.Pan) { PanBy(lastScreen, pos.Value); lastScreen = pos.Value; return; }
            PaintAtScreen(pos.Value); lastScreen = pos.Value;
        }

        /// <summary>손가락을 끈 만큼 보는 곳을 옮긴다 (땅이 손가락을 따라오게). 격자 밖으로 너무 나가지 않게.</summary>
        public void PanBy(Vector2 from, Vector2 to)
        {
            var cam = boot.IslandCam; if (!cam.GroundAt(from, out var a) || !cam.GroundAt(to, out var b)) return;
            var f = cam.ManualFocus + (a - b);
            var (w, h) = CatIsland.Game.Game.GridSize(Zone); var o = WorldSync.CellToWorld(Zone, 0, 0); float c = WorldSync.Cell;
            f.x = Mathf.Clamp(f.x, o.x, o.x + w * c); f.z = Mathf.Clamp(f.z, o.z - 2f, o.z + h * c - 2f);
            cam.ManualFocus = f;
        }

        /// <summary>화면의 한 점 아래 칸에 지금 도구를 쓴다 (테스트에서도). 빨리 쓸어도 칸이 비지 않게 지난 칸과 이은 줄의 칸을 모두.</summary>
        public void PaintAtScreen(Vector2 screen)
        {
            if (!boot.IslandCam.GroundAt(screen, out var p)) return;
            PaintAtWorld(p);
        }
        public void PaintAtWorld(Vector3 p)
        {
            var (w, h) = CatIsland.Game.Game.GridSize(Zone); var o = WorldSync.CellToWorld(Zone, 0, 0);
            int cx = Mathf.FloorToInt((p.x - o.x) / WorldSync.Cell), cz = Mathf.FloorToInt((p.z - o.z) / WorldSync.Cell);
            if (cx < 0 || cz < 0 || cx >= w || cz >= h) { lastCell = null; return; }
            var from = lastCell ?? (cx, cz); lastCell = (cx, cz);
            int n = Mathf.Max(Mathf.Abs(cx - from.x), Mathf.Abs(cz - from.z));
            bool any = false;
            for (int i = 0; i <= n; i++)
            {
                float u = n == 0 ? 1f : i / (float)n;
                int x = Mathf.RoundToInt(Mathf.Lerp(from.x, cx, u)), z = Mathf.RoundToInt(Mathf.Lerp(from.z, cz, u));
                bool ok = Current == Tool.Lay ? g.LayTile(TileId, Zone, x, z) : g.LiftTile(Zone, x, z);
                if (ok) { any = true; Changed++; }
                else if (Current == Tool.Lay && g.Tiles(TileId) <= 0 && g.TileAt(Zone, x, z)?.id != TileId) { boot.UI?.Toast("이 무늬를 다 깔았어요. 상점 '바닥'에서 더 살 수 있어요"); dragging = false; break; }
            }
            if (!any) return;
            FloorTiles.Instance?.Sync(g.S.floor);
            boot.Audio?.Pop(); Haptics.Impact(ImpactStyle.Soft, .25f);
            RefreshBar();
        }

        public void Finish()
        {
            if (Active == this) Active = null;
            if (Changed > 0) g.Save();
            if (bar) Destroy(bar.gameObject);
            if (grid) Destroy(grid);
            if (boot)
            {
                boot.IslandCam.Manual = false;
                boot.Router.enabled = true;
                if (boot.UI) { boot.UI.Root.Find("Bottom")?.gameObject.SetActive(true); if (Changed > 0) boot.UI.Toast("멋진 바닥이 됐어요"); }
                boot.WorldLink.Refresh();
            }
            Destroy(gameObject);
            var d = done; done = null; d?.Invoke();
        }
    }
}
