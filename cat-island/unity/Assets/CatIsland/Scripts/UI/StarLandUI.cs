using System.Linq;
using CatIsland.Game;
using UnityEngine;
using UnityEngine.UI;

namespace CatIsland.UI
{
    /// <summary>
    /// 별나라 (기획서 1부 5장): 별이 된 고양이를 기억하는 조용한 곳. 광고 없음, 상태 없음, 알림 없음.
    /// 추억 카드(이름·함께한 날·좋아하던 것), 편지(기기에만 저장). 처음 한 번만 안내. 느낌표 없는 말투.
    /// </summary>
    public static class StarLandUI
    {
        public static void Open(GameUI ui) => ui.Open(b => Build(ui, b));

        static string Build(GameUI ui, RectTransform body)
        {
            var G = ui.G;
            var bg = body.parent.GetComponent<Image>(); if (bg) bg.color = Theme.StarCloud;
            Kit.VList(body, 10); Kit.Scroll(body, out var list); Kit.Size(list.parent.GetComponent<RectTransform>(), -1, 480).flexibleHeight = 1;
            Kit.Size(Kit.Label(list, Str.StarIntro, Theme.Caption, Theme.StarViolet), -1, 44);
            foreach (var card in G.S.stars)
            {
                var c = G.Cat(card.catUid); if (c == null) continue;
                var box = Kit.Box(list, "Card", Theme.Cream, 16); Kit.Size(box, -1, 150);
                var inner = Kit.Rect(box.transform, "In"); Kit.Fill(inner, 14, 10, 14, 10); Kit.VList(inner, 4);
                int days = (int)((card.createdAt - c.adoptedAt) / 86400) + 1;
                Kit.Size(Kit.Label(inner, Str.StarRest(c.name), Theme.Body, Theme.StarViolet, TextAnchor.MiddleLeft), -1, 28);
                Kit.Size(Kit.Label(inner, $"함께한 날 {days}일 · 좋아하던 것 {card.favorite}", Theme.Caption, null, TextAnchor.MiddleLeft), -1, 24);
                if (!string.IsNullOrEmpty(card.memo)) Kit.Size(Kit.Label(inner, card.memo, Theme.Caption, Theme.Latte, TextAnchor.MiddleLeft), -1, 24);
                Kit.Size(Kit.Label(inner, $"쓴 편지 {card.letters.Count}통", Theme.Caption, Theme.Latte, TextAnchor.MiddleLeft), -1, 22);
                string letter = ""; var row = Kit.Box(inner, "Letter", Theme.MilkTea, 12); Kit.Size(row, -1, 44);
                CatMaker.MakeInput(row.transform, "", s => letter = s).characterLimit = 200;
                var uid = c.uid;
                Kit.Size(Kit.Btn(list, "편지 보내기", () => { if (G.WriteLetter(uid, letter)) { G.Save(); ui.Toast("편지가 별똥별이 되어 날아갔어요"); Open(ui); } }, Kit.Style.Secondary, UIIcon.Star), -1, 48);
            }
            if (G.S.stars.Count == 0) Kit.Size(Kit.Label(list, "별나라는 지금 조용해요.", Theme.Body, Theme.StarViolet), -1, 50);
            // 고양이를 별나라로 보내기 (이용자가 고를 때만: 실제로 떠나보낸 고양이를 기억하고 싶을 때)
            var home = G.S.cats.Where(c => c.status != "star").ToList();
            if (home.Count > 0)
            {
                Kit.Size(Kit.Label(list, "함께했던 고양이를 별나라에서 기억할 수 있어요.", Theme.Caption, Theme.Latte), -1, 40);
                foreach (var c in home) { var cc = c; Kit.Size(Kit.Btn(list, $"{cc.name}의 별 만들기", () => ui.Open(b => Confirm(ui, b, cc)), Kit.Style.Secondary, UIIcon.Moon), -1, 46); }
            }
            return Str.StarLand;
        }

        static string Confirm(GameUI ui, RectTransform body, CatData c)
        {
            Kit.VList(body, 12);
            Kit.Size(Kit.Label(body, $"{Josa.EulReul(c.name)} 별나라에서 기억할까요? 섬에서는 쉬러 가고, 별나라에서 언제든 만날 수 있어요.", Theme.Body), -1, 80);
            string fav = Catalog.FavoriteItems(c.personality).Select(i => Catalog.Item(i)?.ko).FirstOrDefault(x => x != null) ?? "";
            Kit.Size(Kit.Btn(body, "별로 만들어요", () => { ui.G.SendToStars(c.uid, "", fav); Open(ui); ui.World?.Refresh(); }, Kit.Style.Primary, UIIcon.Star), -1, 52);
            Kit.Size(Kit.Btn(body, Str.Cancel, () => Open(ui), Kit.Style.Secondary), -1, 48);
            return Str.StarLand;
        }
    }
}
