using System;
using System.Linq;
using CatIsland.Game;
using UnityEngine;
using UnityEngine.UI;

namespace CatIsland.UI
{
    /// <summary>
    /// 고양이 만들기 (기획서 1부 4장): 품종 고르기 또는 사진으로. 눈 모양 3가지·수염 3가지를 버튼으로 바꾼다 (ART_DIRECTION 3-1).
    /// 사진은 기기 안에서만 읽고 밖으로 보내지 않는다 (PhotoReader). 이름은 비우면 품종 이름.
    /// </summary>
    public static class CatMaker
    {
        static string breed = "korean_shorthair", eye = "dark", whisker = "short", name = ""; static Personality pers = Personality.Playful; static string coatJson = "";
        public static readonly string[] EyeIds = { "dark", "iris", "rim" }, WhiskerIds = { "short", "long", "dots" };
        public static Action<string> OnPreview;      // (3D 미리보기: 품종이 바뀔 때)

        public static void Open(GameUI ui) => ui.Open(b => Build(ui, b));

        static string Build(GameUI ui, RectTransform body)
        {
            Kit.VList(body, 10); Kit.Scroll(body, out var list); Kit.Size(list.parent.GetComponent<RectTransform>(), -1, 480).flexibleHeight = 1;
            // 사진으로
            var pr = Kit.Box(list, "PhotoRow", Theme.MilkTea, 14); Kit.Size(pr, -1, 92);
            var prIn = Kit.Rect(pr.transform, "In"); Kit.Fill(prIn, 12, 6, 10, 6); Kit.HList(prIn, 10).childAlignment = TextAnchor.MiddleLeft;
            Kit.Size(Kit.IconImage(prIn, UIIcon.Camera, 42), 42, 42);
            Kit.Size(Kit.Label(prIn, Str.PhotoPrivacy, Theme.Caption, null, TextAnchor.MiddleLeft), -1, 70, 1);
            Kit.Size(Kit.Btn(prIn, Str.FromPhoto, () => PhotoReader.Pick(res =>
            {
                if (res == null) { ui.Toast("사진을 고르지 않았어요"); return; }
                breed = res.breed; eye = res.eyeStyle; whisker = res.whiskerStyle; coatJson = res.coatJson; OnPreview?.Invoke(breed);
                ui.Toast($"{Josa.EulReul(Catalog.BreedKo(breed))} 닮았어요"); Open(ui);
            }), Kit.Style.Primary), 90, 46);
            // 품종
            Kit.Size(Kit.Label(list, Str.FromBreed, Theme.Caption, Theme.Latte, TextAnchor.MiddleLeft), -1, 24);
            var grid = Kit.Rect(list, "Grid"); var gl = grid.gameObject.AddComponent<GridLayoutGroup>(); gl.cellSize = new Vector2(108, 44); gl.spacing = new Vector2(6, 6); gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = 3;
            Kit.Size(grid, -1, Mathf.CeilToInt(Catalog.Breeds.Length / 3f) * 50);
            foreach (var b in Catalog.Breeds)
            {
                var bb = b; var btn = Kit.Btn(grid, b.ko.Length > 7 ? b.ko.Substring(0, 7) : b.ko, () => { breed = bb.id; coatJson = ""; OnPreview?.Invoke(breed); Open(ui); }, breed == b.id ? Kit.Style.Primary : Kit.Style.Secondary);
                btn.GetComponentInChildren<Text>().fontSize = Theme.Caption;
            }
            // 눈 · 수염 · 성격
            Choice(list, Str.EyeStyle, Str.EyeNames, Array.IndexOf(EyeIds, eye), i => { eye = EyeIds[i]; Open(ui); });
            Choice(list, Str.Whisker, Str.WhiskerNames, Array.IndexOf(WhiskerIds, whisker), i => { whisker = WhiskerIds[i]; Open(ui); });
            var ps = Enum.GetValues(typeof(Personality)).Cast<Personality>().ToArray();
            Choice(list, "성격", ps.Select(Catalog.Ko).ToArray(), Array.IndexOf(ps, pers), i => { pers = ps[i]; Open(ui); });
            // 이름
            var nr = Kit.Box(list, "NameRow", Theme.MilkTea, 14); Kit.Size(nr, -1, 60);
            var field = MakeInput(nr.transform, name, s => name = s);
            Kit.Size(Kit.Btn(list, "이 고양이와 함께할게요", () =>
            {
                var c = ui.G.AddCat(breed, name, pers, eye, whisker, coatJson);
                if (c == null) { ui.Toast(Str.Oops); return; }
                name = ""; ui.CloseAll(); ui.Toast($"{Josa.IGa(c.name)} 섬에 왔어요"); ui.World?.Refresh();
            }, Kit.Style.Primary, UIIcon.Heart), -1, 56);
            return Str.MakeCat;
        }

        static void Choice(RectTransform list, string label, string[] options, int sel, Action<int> pick)
        {
            Kit.Size(Kit.Label(list, label, Theme.Caption, Theme.Latte, TextAnchor.MiddleLeft), -1, 22);
            var row = Kit.Rect(list, "Choice_" + label); Kit.HList(row, 6); Kit.Size(row, -1, 46);
            for (int i = 0; i < options.Length; i++) { int k = i; var b = Kit.Btn(row, options[i], () => pick(k), i == sel ? Kit.Style.Primary : Kit.Style.Secondary); b.GetComponentInChildren<Text>().fontSize = Theme.Caption; Kit.Size(b, -1, 44, 1); }
        }

        public static InputField MakeInput(Transform parent, string value, Action<string> changed)
        {
            var rt = Kit.Rect(parent, "Input"); Kit.Fill(rt, 12, 6, 12, 6);
            var img = rt.gameObject.AddComponent<Image>(); img.color = new Color(1, 1, 1, 0);
            var text = Kit.Label(rt, value, Theme.Body, null, TextAnchor.MiddleLeft, "Text"); Kit.Fill(text.rectTransform, 6, 2, 6, 2); text.raycastTarget = true; text.resizeTextForBestFit = false; text.supportRichText = false;
            var ph = Kit.Label(rt, "이름 (비우면 품종 이름)", Theme.Body, Theme.Latte, TextAnchor.MiddleLeft, "Placeholder"); Kit.Fill(ph.rectTransform, 6, 2, 6, 2);
            var f = rt.gameObject.AddComponent<InputField>(); f.textComponent = text; f.placeholder = ph; f.characterLimit = 12; f.text = value;
            f.onValueChanged.AddListener(s => changed(s)); return f;
        }
    }

    /// <summary>사진에서 읽은 고양이 (기기 안 처리).</summary>
    public class PhotoCat { public string breed, eyeStyle, whiskerStyle, coatJson; }
}
