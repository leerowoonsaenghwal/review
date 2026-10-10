using System;
using System.Collections.Generic;
using CatIsland.Game;
using CatIsland.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CatIsland
{
    /// <summary>
    /// 꾸미기 모드: 용품이 손가락을 따라 격자 위를 움직이고, 발자리가 초록(놓을 수 있음) · 분홍(안 됨)으로 보인다.
    /// 고양이가 쓰는 앞자리도 함께 보여 비워 둔다 (기획서 1부 6장). 돌리기 · 놓기 · 그만 버튼.
    /// 새 용품(가방에서) 또는 놓인 용품 옮기기(index) 둘 다.
    /// </summary>
    public class PlaceMode : MonoBehaviour
    {
        public static PlaceMode Active { get; private set; }
        public string Item { get; private set; }
        public int X { get; private set; }
        public int Z { get; private set; }
        public int Rot { get; private set; }
        public Zone Zone { get; private set; }
        CatIsland.Game.Game g; GameBootstrap boot; Action<bool> done; int moveIndex = -1;
        GameObject ghost; Transform foot; MeshRenderer footR; RectTransform bar;
        bool dragging;

        public static PlaceMode Begin(GameBootstrap boot, string item, int moveIndex, Action<bool> done)
        {
            if (Active) Active.Finish(false);
            if (TileMode.Active) TileMode.Active.Finish(); if (CatIsland.UI.PhotoModeUI.Active) CatIsland.UI.PhotoModeUI.Active.Close();   // (모드는 한 번에 하나)
            var m = new GameObject("PlaceMode").AddComponent<PlaceMode>();
            m.boot = boot; m.g = boot.Logic; m.Item = item; m.done = done; m.moveIndex = moveIndex;
            var d = Catalog.Item(item); m.Zone = d.zone;
            if (moveIndex >= 0) { var p = m.g.S.placed[moveIndex]; m.X = p.x; m.Z = p.z; m.Rot = p.rot; m.Zone = p.zone; }
            else { var (w, h) = CatIsland.Game.Game.GridSize(m.Zone); m.X = w / 2 - d.w / 2; m.Z = h / 2 - d.h / 2; m.Rot = 2; m.FindFreeNear(); }
            m.Build();
            boot.Router.enabled = false;
            if (boot.UI) boot.UI.Root.Find("Bottom")?.gameObject.SetActive(false);
            boot.IslandCam.ShowZone(m.Zone);
            Active = m; return m;
        }

        void FindFreeNear()
        {
            var (w, h) = CatIsland.Game.Game.GridSize(Zone);
            for (int r = 0; r < Mathf.Max(w, h); r++)
                for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        if (g.CanPlace(Item, Zone, X + dx, Z + dz, Rot, Ignore)) { X += dx; Z += dz; return; }
                    }
        }
        Placement Ignore => moveIndex >= 0 ? g.S.placed[moveIndex] : null;

        void Build()
        {
            var d = Catalog.Item(Item);
            ghost = ItemLoader.Spawn(d.model, transform, Vector3.zero, 0) ?? new GameObject("Ghost");
            ghost.transform.SetParent(transform, true);
            foreach (var c in ghost.GetComponentsInChildren<Collider>()) c.enabled = false;
            foot = Shapes.Make(transform, "Footprint", MeshFactory.RoundedCylinder(.02f), Palette.Mint, Vector3.zero, Vector3.one, default, false);
            footR = foot.GetComponent<MeshRenderer>(); footR.sharedMaterial = new Material(footR.sharedMaterial);
            // 아래 버튼: 돌리기 · 놓기 · 그만
            if (boot.UI)
            {
                bar = Kit.Rect(boot.UI.Root, "PlaceBar"); Kit.Anchor(bar, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 14), new Vector2(360, 64));
                Kit.HList(bar, 12);
                Kit.Size(Kit.Btn(bar, Str.Rotate, () => { Rot = (Rot + 1) & 3; Refresh(); }, Kit.Style.Secondary, UIIcon.Rotate), 110, 56);
                Kit.Size(Kit.Btn(bar, Str.Place, Confirm, Kit.Style.Primary, UIIcon.Check), 120, 56);
                Kit.Size(Kit.Btn(bar, "그만", () => Finish(false), Kit.Style.Secondary, UIIcon.Close), 100, 56);
            }
            Refresh();
        }

        public bool Valid => g.CanPlace(Item, Zone, X, Z, Rot, Ignore);

        void Refresh()
        {
            var d = Catalog.Item(Item); int w = d.w, h = d.h; if (Rot % 2 == 1) (w, h) = (h, w);
            var c = WorldSync.CellToWorld(Zone, X + w / 2f, Z + h / 2f);
            ghost.transform.position = c + new Vector3(0, .006f, 0); ghost.transform.rotation = Quaternion.Euler(0, 180f - Rot * 90f, 0);
            foot.position = c + new Vector3(0, .003f, 0); foot.localScale = new Vector3(w * WorldSync.Cell * .96f, .01f, h * WorldSync.Cell * .96f);
            footR.sharedMaterial.SetColor("_BaseColor", Valid ? new Color(.56f, .86f, .56f) : new Color(1f, .6f, .7f));
        }

        void Update()
        {
            // 손가락(또는 마우스)이 놓은 칸으로 용품이 따라온다 (버튼 위에서 시작한 손가락은 제외)
            Vector2? pos = null; bool pressed = false;
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.isPressed) { pos = ts.primaryTouch.position.ReadValue(); pressed = true; }
            else if (Mouse.current != null && Mouse.current.leftButton.isPressed) { pos = Mouse.current.position.ReadValue(); pressed = true; }
            if (!pressed) { dragging = false; return; }
            if (!dragging) { if (GameUI.PointerOverUI(ts != null && ts.primaryTouch.press.isPressed ? ts.primaryTouch.touchId.ReadValue() : -1)) return; dragging = true; }
            MoveToScreen(pos.Value);
        }

        /// <summary>화면의 한 점 아래 칸으로 옮긴다 (테스트에서도 쓴다).</summary>
        public void MoveToScreen(Vector2 screen)
        {
            var cam = boot.IslandCam.GetComponent<Camera>(); var ray = cam.ScreenPointToRay(screen);
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float t)) return;
            var wp = ray.GetPoint(t); MoveToWorld(wp);
        }
        public void MoveToWorld(Vector3 wp)
        {
            var d = Catalog.Item(Item); int w = d.w, h = d.h; if (Rot % 2 == 1) (w, h) = (h, w);
            var (gw, gh) = CatIsland.Game.Game.GridSize(Zone);
            var o = WorldSync.CellToWorld(Zone, 0, 0);
            int nx = Mathf.Clamp(Mathf.RoundToInt((wp.x - o.x) / WorldSync.Cell - w / 2f), 0, gw - w), nz = Mathf.Clamp(Mathf.RoundToInt((wp.z - o.z) / WorldSync.Cell - h / 2f), 0, gh - h);
            if (nx != X || nz != Z) { X = nx; Z = nz; Refresh(); }
        }

        public void Confirm()
        {
            if (!Valid) { boot.UI?.Toast("여기는 자리가 좁아요. 다른 곳은 어때요?"); return; }
            bool ok = moveIndex >= 0 ? g.Move(moveIndex, X, Z, Rot) : g.Place(Item, Zone, X, Z, Rot);
            if (ok) { g.Save(); boot.UI?.Toast("좋은 자리예요"); }
            Finish(ok);
        }

        public void Finish(bool ok)
        {
            if (Active == this) Active = null;
            if (bar) Destroy(bar.gameObject);
            if (boot)
            {
                boot.Router.enabled = true;
                if (boot.UI) boot.UI.Root.Find("Bottom")?.gameObject.SetActive(true);
                boot.WorldLink.Refresh();
                if (Zone != Zone.Indoor) boot.IslandCam.ShowZone(Zone.Indoor);
            }
            Destroy(gameObject);
            var d = done; done = null; d?.Invoke(ok);
        }
    }
}
