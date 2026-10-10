namespace CatIsland.Tests
{
    using System.Collections;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UI;

    /// <summary>
    /// 무작위 조작 (몽키 테스트): 화면에 보이는 버튼 아무거나 누르기, 섬 아무 곳 톡 치기, 끌어 돌리기, 바닥 깔기·꾸미기 모드에서 손가락 놀리기를
    /// 고정 씨앗으로 섞어 오래 한다. 오류·예외 로그가 하나라도 나오면 실패 (테스트 틀이 잡는다), 끝나고 저장·재화·고양이 상태가 멀쩡한지.
    /// 사람이 미처 눌러 보지 않은 순서에서 나는 오류를 찾는다.
    /// </summary>
    public class MonkeyTest : SceneFixture
    {
        [UnityTest] public IEnumerator Monkey_Seed1_TwoMinutes() => Run(1, 120f);

        [UnityTest, Explicit, Timeout(1800000)] public IEnumerator Monkey_Seed2_TenMinutes() => Run(2, 600f);
        [UnityTest, Explicit, Timeout(1800000)] public IEnumerator Monkey_Seed3_TenMinutes() => Run(3, 600f);
        [UnityTest, Explicit, Timeout(1800000)] public IEnumerator Monkey_Seed4_TenMinutes() => Run(4, 600f);
        [UnityTest, Explicit, Timeout(1800000)] public IEnumerator Monkey_Seed5_TenMinutes() => Run(5, 600f);

        IEnumerator Run(int seed, float seconds)
        {
            var g = game.Logic; g.S.catSlots = 4; g.AddCoins(50000); g.AddJelly(500);
            g.AddCat("korean_shorthair", "나비", CatIsland.Game.Personality.Playful); g.AddCat("persian", "보리", CatIsland.Game.Personality.Easygoing);
            if (!g.S.zonesUnlocked.Contains(1)) g.S.zonesUnlocked.Add(1);
            game.WorldLink.Refresh(); game.SyncCats(); yield return null;
            var rng = new System.Random(seed); int actions = 0, clicks = 0;
            Time.timeScale = 2f;
            float W = UnityEngine.Screen.width, H = UnityEngine.Screen.height;
            for (float t = 0; t < seconds; actions++)
            {
                float r = (float)rng.NextDouble(); float t0 = Time.time;
                if (TileMode.Active && r < .5f)
                {
                    var tm = TileMode.Active;
                    if (rng.Next(6) == 0) tm.SetTool((TileMode.Tool)rng.Next(3));
                    tm.PaintAtWorld(new Vector3((float)rng.NextDouble() * 12f - 6f, 0f, (float)rng.NextDouble() * 12f - 5.5f));
                    yield return null;
                }
                else if (PlaceMode.Active && r < .5f)
                {
                    var pm = PlaceMode.Active; pm.MoveToWorld(new Vector3((float)rng.NextDouble() * 10f - 5f, 0f, (float)rng.NextDouble() * 10f - 4.5f));
                    yield return null; if (rng.Next(3) == 0) pm.Confirm();
                }
                else if (r < .55f)
                {
                    // 화면에 보이고 누를 수 있는 버튼 하나 (영구히 고양이를 보내는 '별 만들기'는 빼고)
                    var btns = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.isActiveAndEnabled && b.interactable && b.onClick.GetPersistentEventCount() + 1 > 0)
                        .Where(b => { var txt = b.GetComponentInChildren<Text>(); return txt == null || !txt.text.Contains("별 만들기"); }).ToArray();
                    if (btns.Length > 0) { var b = btns[rng.Next(btns.Length)]; b.onClick.Invoke(); clicks++; }
                    yield return null; yield return null;
                }
                else if (r < .8f)
                {
                    yield return Tap(new Vector2((float)rng.NextDouble() * W, (float)rng.NextDouble() * H * .8f + H * .1f));
                }
                else if (r < .92f)
                {
                    // 끌기 (카메라 돌리기·쓰다듬기)
                    var a = new Vector2((float)rng.NextDouble() * W, (float)rng.NextDouble() * H * .6f + H * .2f); var d = new Vector2((float)rng.NextDouble() - .5f, (float)rng.NextDouble() - .5f) * W * .6f;
                    for (int i = 0; i <= 10; i++) { fingers.Press(a + d * (i / 10f)); yield return null; }
                    fingers.Release(); yield return null;
                }
                else { for (int i = 0; i < 20; i++) yield return null; }
                t += Mathf.Max(Time.time - t0, Time.deltaTime);
            }
            Time.timeScale = 1f;
            if (TileMode.Active) TileMode.Active.Finish();
            if (PlaceMode.Active) PlaceMode.Active.Finish(false);
            if (CatIsland.UI.PhotoModeUI.Active) CatIsland.UI.PhotoModeUI.Active.Close();
            game.UI.CloseAll(); yield return null;
            Debug.Log($"[Monkey] seed {seed}: {actions} actions, {clicks} button presses, coins {g.S.coins}, jelly {g.S.jelly}, cats {g.S.cats.Count}, placed {g.S.placed.Count}, tiles {g.S.floor.Count}");
            // 끝난 뒤 상태가 멀쩡한가
            Assert.GreaterOrEqual(g.S.coins, 0); Assert.GreaterOrEqual(g.S.jelly, 0);
            Assert.IsTrue(g.S.placed.All(p => CatIsland.Game.Catalog.Item(p.item) != null));
            Assert.IsTrue(g.S.floor.All(f => CatIsland.Game.Catalog.Tile(f.id) != null));
            g.Save(); var back = JsonUtility.FromJson<CatIsland.Game.GameState>(JsonUtility.ToJson(g.S));
            Assert.AreEqual(g.S.cats.Count, back.cats.Count, "저장 그대로"); Assert.AreEqual(g.S.floor.Count, back.floor.Count);
            Assert.IsTrue(game.Router.enabled, "모드가 끝나면 손가락 입력이 돌아온다");
        }
    }
}
