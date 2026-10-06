using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using CatIsland.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CatIsland.UI
{
    /// <summary>
    /// 사진 모드 (기획서 3부 6장: 사진 모드에서만 자유 시점 + 고양이 눈높이). 손가락으로 돌리고 두 손가락으로 확대,
    /// 눈높이 버튼, 액자 고르기(호감도 10단계에 특별 액자), 셔터. 사진은 앱 안 photos 폴더에만 저장하고,
    /// 공유는 이용자가 공유 버튼을 누를 때만 아이폰 공유 화면으로 (사진은 기기 밖으로 나가지 않는다는 약속).
    /// </summary>
    public class PhotoModeUI : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CatShare_Image(string path, string text);
#else
        static void CatShare_Image(string path, string text) { }
#endif
        public static PhotoModeUI Active { get; private set; }
        public static readonly string[] FrameNames = { "없음", "크림", "꽃", "별", "특별" };
        GameUI ui; GameBootstrap boot; RectTransform bar, frameView; int frame; Text eyeLabel;
        Vector2 lastPos; float lastPinch; bool dragging;

        public static string PhotoDir => Path.Combine(Application.persistentDataPath, "photos");

        public static void Open(GameUI ui, GameBootstrap boot)
        {
            if (Active) return;
            var m = new GameObject("PhotoMode").AddComponent<PhotoModeUI>(); m.ui = ui; m.boot = boot; Active = m;
            ui.CloseAll();
            boot.IslandCam.BeginPhoto(); boot.Router.enabled = false;
            ui.Root.Find("Top")?.gameObject.SetActive(false); ui.Root.Find("Bottom")?.gameObject.SetActive(false); ui.Root.Find("Side")?.gameObject.SetActive(false); ui.Root.Find("Hint_shadow")?.gameObject.SetActive(false);
            m.Build();
        }

        void Build()
        {
            frameView = Kit.Rect(ui.Root, "PhotoFrame"); Kit.Fill(frameView); ApplyFrame();
            bar = Kit.Rect(ui.Root, "PhotoBar"); Kit.Anchor(bar, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 14), new Vector2(370, 150));
            var top = Kit.Rect(bar, "Row1"); Kit.Anchor(top, new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(370, 50)); Kit.HList(top, 8);
            var eye = Kit.Btn(top, "눈높이", ToggleEye, Kit.Style.Secondary, UIIcon.Cat); Kit.Size(eye, 110, 46); eyeLabel = eye.GetComponentInChildren<Text>();
            Kit.Size(Kit.Btn(top, "액자", NextFrame, Kit.Style.Secondary, UIIcon.Star), 96, 46);
            Kit.Size(Kit.Btn(top, "앨범", () => { Close(); ui.Open(b => BuildAlbum(ui, b)); }, Kit.Style.Secondary, UIIcon.Book), 96, 46);
            var row = Kit.Rect(bar, "Row2"); Kit.Anchor(row, new Vector2(.5f, 0), new Vector2(.5f, 0), Vector2.zero, new Vector2(370, 90)); Kit.HList(row, 40);
            Kit.IconBtn(row, UIIcon.Close, "닫기", Close, 56);
            Kit.IconBtn(row, UIIcon.Camera, "찍기", () => Shoot(), 78, Theme.StrawberryLight);
            Kit.IconBtn(row, UIIcon.Rotate, "처음", () => { boot.IslandCam.EndPhoto(); boot.IslandCam.BeginPhoto(); }, 56);
        }

        void ToggleEye() { boot.IslandCam.SetEyeLevel(!boot.IslandCam.EyeLevel); eyeLabel.text = boot.IslandCam.EyeLevel ? "위에서" : "눈높이"; }
        int MaxFrame => ui.G.S.cats.Any(c => c.Level >= 10) ? FrameNames.Length : FrameNames.Length - 1;   // (특별 액자: 호감도 10단계)
        void NextFrame() { frame = (frame + 1) % MaxFrame; ApplyFrame(); ui.Toast("액자: " + FrameNames[frame]); }
        void ApplyFrame()
        {
            foreach (Transform c in frameView) Destroy(c.gameObject);
            if (frame == 0) return;
            var col = frame == 1 ? Theme.Cream : frame == 2 ? Theme.StrawberryLight : frame == 3 ? Theme.Butter : Theme.Sea;
            var border = Kit.Box(frameView, "Border", col, 28, 14); Kit.Fill(border.rectTransform, 10, 10, 10, 10); border.raycastTarget = false;
            var icon = frame == 2 ? UIIcon.Heart : frame == 3 ? UIIcon.Star : frame == 4 ? UIIcon.Paw : UIIcon.Paw;
            for (int i = 0; i < 4; i++) { var ic = Kit.IconImage(frameView, icon, 34); var a = new Vector2(i % 2, i / 2); Kit.Anchor(ic.rectTransform, a, new Vector2(.5f, .5f), new Vector2(a.x > .5f ? -26 : 26, a.y > .5f ? -26 : 26), new Vector2(34, 34)); }
            var stamp = Kit.Label(frameView, Str.AppName + "  " + DateTime.Now.ToString("yyyy.MM.dd"), Theme.Caption, Theme.Cocoa); Kit.Anchor(stamp.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 16), new Vector2(300, 22));
        }

        void Update()
        {
            // 한 손가락: 돌리기 / 두 손가락: 확대 (버튼 위에서 시작한 손가락은 무시)
            var ts = Touchscreen.current; int n = 0; Vector2 p0 = default, p1 = default;
            if (ts != null) foreach (var t in ts.touches) if (t.press.isPressed) { if (n == 0) p0 = t.position.ReadValue(); else if (n == 1) p1 = t.position.ReadValue(); n++; }
            if (n == 0 && Mouse.current != null && Mouse.current.leftButton.isPressed) { p0 = Mouse.current.position.ReadValue(); n = 1; }
            if (Mouse.current != null && Mathf.Abs(Mouse.current.scroll.ReadValue().y) > .01f) boot.IslandCam.PhotoZoom(Mouse.current.scroll.ReadValue().y > 0 ? .9f : 1.1f);
            if (n == 0) { dragging = false; lastPinch = 0; return; }
            if (!dragging) { if (GameUI.PointerOverUI(ts != null && n > 0 && ts.primaryTouch.press.isPressed ? ts.primaryTouch.touchId.ReadValue() : -1)) return; dragging = true; lastPos = p0; lastPinch = 0; }
            if (n >= 2) { float d = (p0 - p1).magnitude; if (lastPinch > 0) boot.IslandCam.PhotoZoom(lastPinch / Mathf.Max(1, d)); lastPinch = d; return; }
            var dlt = (p0 - lastPos) / Mathf.Max(1, Screen.height); lastPos = p0;
            boot.IslandCam.PhotoOrbit(dlt.x * 220f, -dlt.y * 120f);
        }

        /// <summary>찍기: 화면(섬 + 액자, 버튼 없이)을 그대로 저장한다.</summary>
        public string Shoot()
        {
            bar.gameObject.SetActive(false);
            var cam = boot.IslandCam.GetComponent<Camera>(); var canvas = ui.GetComponent<Canvas>();
            int W = Mathf.Max(720, Screen.width), H = Mathf.Max(1280, Screen.height);
            var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
            var mode = canvas.renderMode; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = .6f;
            cam.targetTexture = rt; Canvas.ForceUpdateCanvases(); cam.Render(); cam.Render(); cam.targetTexture = null;
            canvas.renderMode = mode;
            RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = null;
            Directory.CreateDirectory(PhotoDir); var name = $"photo_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            File.WriteAllBytes(Path.Combine(PhotoDir, name), tex.EncodeToPNG()); Destroy(tex); rt.Release(); Destroy(rt);
            bar.gameObject.SetActive(true);
            ui.G.TakePhoto(name, ui.G.S.cats.FirstOrDefault(c => c.status == "home")?.uid ?? ""); ui.G.Save();
            ui.Toast("찰칵! 휴대폰 안 앨범에 남겼어요");
            Haptics.Impact(ImpactStyle.Light, .6f);
            return name;
        }

        public void Close()
        {
            if (Active == this) Active = null;
            boot.IslandCam.EndPhoto(); boot.Router.enabled = true;
            if (bar) Destroy(bar.gameObject); if (frameView) Destroy(frameView.gameObject);
            foreach (var n in new[] { "Top", "Bottom", "Side" }) ui.Root.Find(n)?.gameObject.SetActive(true);
            ui.Refresh(); Destroy(gameObject);
        }

        // ---------------------------------------------------------------- 앨범 (앱 안 사진만)
        public static string BuildAlbum(GameUI ui, RectTransform body)
        {
            Kit.VList(body, 10);
            Kit.Size(Kit.Label(body, Str.PhotoPrivacy, Theme.Caption, Theme.Latte), -1, 40);
            Kit.Scroll(body, out var list); Kit.Size(list.parent.GetComponent<RectTransform>(), -1, 440).flexibleHeight = 1;
            var photos = ui.G.S.photos.AsEnumerable().Reverse().Where(p => File.Exists(Path.Combine(PhotoDir, p.file))).Take(30).ToList();
            if (photos.Count == 0) Kit.Size(Kit.Label(list, "아직 사진이 없어요. 사진 버튼으로 남겨 봐요.", Theme.Body, Theme.Latte), -1, 60);
            foreach (var p in photos)
            {
                var row = Kit.Box(list, "Photo", Theme.MilkTea, 14); Kit.Size(row, -1, 120);
                var inner = Kit.Rect(row.transform, "In"); Kit.Fill(inner, 10, 8, 10, 8); Kit.HList(inner, 10).childAlignment = TextAnchor.MiddleLeft;
                var path = Path.Combine(PhotoDir, p.file);
                var tex = new Texture2D(2, 2); tex.LoadImage(File.ReadAllBytes(path));
                var thumb = Kit.Rect(inner, "Thumb"); var raw = thumb.gameObject.AddComponent<RawImage>(); raw.texture = tex; Kit.Size(raw, 58, 104);
                var t = Kit.Label(inner, DateTimeOffset.FromUnixTimeSeconds(p.takenAt).ToLocalTime().ToString("M월 d일 HH:mm"), Theme.Body, null, TextAnchor.MiddleLeft); Kit.Size(t, -1, 40, 1);
                Kit.Size(Kit.Btn(inner, "공유", () => CatShare_Image(path, "#놀러와요고양이섬"), Kit.Style.Primary), 70, 46);
                Kit.Size(Kit.Btn(inner, "지우기", () => { File.Delete(path); ui.G.S.photos.Remove(p); ui.G.Save(); ui.Open(b => BuildAlbum(ui, b)); }, Kit.Style.Secondary), 76, 46);
            }
            return "앨범";
        }
    }
}
