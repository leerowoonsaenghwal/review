using System.Linq;
using System.Text;
using CatIsland.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CatIsland.EditorTools
{
    /// <summary>화면 배치 값 찍어 보기 (배치가 이상할 때): -executeMethod CatIsland.EditorTools.UIDebug.Dump</summary>
    public static class UIDebug
    {
        public static void Dump()
        {
            var g = new CatIsland.Game.Game(new CatIsland.Game.FakeClock(System.DateTime.UtcNow), new CatIsland.Game.MemoryFiles()); g.LoadOrNew(); g.AddCat("korean_shorthair", "나비", CatIsland.Game.Personality.Playful); g.AddCoins(5000);
            var ui = GameUI.Create(g, null);
            var m = typeof(GameUI).GetMethod("BuildShop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            ui.Open(b => (string)m.Invoke(ui, new object[] { b }));
            Canvas.ForceUpdateCanvases(); foreach (var lg in ui.GetComponentsInChildren<LayoutGroup>()) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)lg.transform); Canvas.ForceUpdateCanvases();
            var sb = new StringBuilder();
            var row = ui.GetComponentsInChildren<RectTransform>().First(r => r.name == "In");
            void D(RectTransform r, int depth) { sb.AppendLine($"{new string(' ', depth * 2)}{r.name} pos={r.anchoredPosition} size={r.rect.size} amin={r.anchorMin} amax={r.anchorMax} piv={r.pivot}" + (r.GetComponent<Text>() is Text t ? $" text='{t.text}' pw={t.preferredWidth}" : "")); foreach (RectTransform c in r) D(c, depth + 1); }
            D((RectTransform)row.parent, 0);
            var btn = ui.GetComponentsInChildren<Button>().First(b => b.name.StartsWith("Btn_전체")); D((RectTransform)btn.transform, 0);
            Debug.Log("[UIDebug]\n" + sb);
        }
    }
}
