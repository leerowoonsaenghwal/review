namespace CatIsland.Tests
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using Unity.Profiling;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// 메모리 만들기 찾기 (실기에서 GC 가 초당 8번): 고양이 만들기 창을 연 첫 화면에서 프레임마다 새로 만드는 메모리를 재고,
    /// 부품(MonoBehaviour 종류)을 하나씩 꺼 보며 어디서 나오는지 로그로 남긴다. 평소 테스트에서는 돌지 않는다.
    /// </summary>
    [Explicit]
    public class AllocProbe : SceneFixture
    {
        static IEnumerator Measure(ProfilerRecorder rec, List<long> into, int frames = 60)
        {
            for (int i = 0; i < 10; i++) yield return null;
            long sum = 0; for (int i = 0; i < frames; i++) { yield return null; sum += rec.LastValue; }
            into.Add(sum / frames);
        }

        /// <summary>고양이 상태별 메모리: 고양이 4마리·용품이 있는 섬을 2분(2배속) 돌리고 상태마다 프레임당 평균·최대를 로그로.</summary>
        [UnityTest]
        public IEnumerator Cats_BytesPerFrame_ByState()
        {
            var g = game.Logic; g.S.catSlots = 5; g.AddCoins(99999);
            foreach (var (b, n) in new[] { ("korean_shorthair", "나비"), ("persian", "보리"), ("siamese", "달이"), ("maine_coon", "호두") }) g.AddCat(b, n, CatIsland.Game.Personality.Playful);
            foreach (var (id, x, z) in new[] { ("cushion", 7, 9), ("hideout", 10, 6), ("mouse_toy", 8, 11), ("scratcher", 5, 7) }) if (g.Buy(id)) g.Place(id, CatIsland.Game.Zone.Indoor, x, z, 2);
            game.WorldLink.Refresh(); yield return null; game.SyncCats(); yield return null;
            CatBrain.AllocByState.Clear(); CatBrain.AllocTrace = true; Time.timeScale = 2f;
            for (float t = 0; t < 120f; t += Time.deltaTime) yield return null;
            Time.timeScale = 1f; CatBrain.AllocTrace = false;
            foreach (var kv in CatBrain.AllocByState.OrderByDescending(k => k.Value.bytes))
                Debug.Log($"[AllocState] {kv.Key}: avg {kv.Value.bytes / System.Math.Max(1, kv.Value.frames)} B/frame, max {kv.Value.max} B, frames {kv.Value.frames}, total {kv.Value.bytes / 1024} KB");
            Assert.Pass();
        }

        [UnityTest]
        public IEnumerator FirstScreen_BytesPerFrame_ByComponent()
        {
            CatIsland.UI.CatMaker.Open(game.UI);
            for (int i = 0; i < 60; i++) yield return null;
            using var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            var all = new List<long>(); yield return Measure(rec, all);
            Debug.Log($"[Alloc] all {all[0]} B/frame");
            var types = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Where(m => m.enabled && !(m is GameBootstrap)).GroupBy(m => m.GetType()).ToList();
            foreach (var g in types)
            {
                foreach (var m in g) m.enabled = false;
                var r = new List<long>(); yield return Measure(rec, r, 30);
                foreach (var m in g) if (m) m.enabled = true;
                Debug.Log($"[Alloc] without {g.Key.Name} x{g.Count()}: {r[0]} B/frame (saves {all[0] - r[0]})");
            }
            // GameBootstrap 자체 (고양이 Tick, UI 갱신 등)
            game.enabled = false; var b = new List<long>(); yield return Measure(rec, b, 30); game.enabled = true;
            Debug.Log($"[Alloc] without GameBootstrap: {b[0]} B/frame (saves {all[0] - b[0]})");
            Assert.Pass();
        }
    }
}
