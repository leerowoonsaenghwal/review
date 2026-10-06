namespace CatIsland.Tests
{
    using System.Collections;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.Profiling;
    using UnityEngine.TestTools;

    /// <summary>
    /// 출시 점검: 고양이 5마리 + 용품을 놓은 섬에서 프레임 시간·그리기 횟수·삼각형·메모리를 재서 docs/PERF.md 표에 쓸 값을 남긴다
    /// (에디터 수치: 아이폰 실기 값은 RELEASE_TODO). 눈 모양·수염 고르기(CatFace)와 사진 고양이 털 색(CatCoat)이 실제로 바뀌는지.
    /// </summary>
    public class ReleaseChecks : SceneFixture
    {
        [UnityTest]
        public IEnumerator Perf_FiveCatsAndItems()
        {
            var g = game.Logic; g.S.catSlots = 5; g.S.coins = 999999;
            foreach (var (b, n) in new[] { ("korean_shorthair", "나비"), ("persian", "보리"), ("munchkin", "콩"), ("maine_coon", "호두"), ("siamese", "달이") })
                g.AddCat(b, n, CatIsland.Game.Personality.Playful);
            foreach (var id in new[] { "cushion", "hideout", "mouse_toy", "scratcher", "tower2", "plant_pot", "rug_round" })
            {
                if (CatIsland.Game.Catalog.Item(id) == null) continue;
                g.Buy(id);
                for (int x = -3; x <= 3; x++) for (int z = -3; z <= 3; z++) if (g.CanPlace(id, 0, x, z, 0)) { g.Place(id, 0, x, z, 0); x = 99; break; }
            }
            game.SyncCats(); game.WorldLink.Refresh();
            for (int i = 0; i < 60; i++) yield return null;   // (자리 잡기)
            int frames = 0; float total = 0f, worst = 0f;
            while (total < 6f) { yield return null; frames++; total += Time.unscaledDeltaTime; worst = Mathf.Max(worst, Time.unscaledDeltaTime); }
            int draws = 0, tris = 0, batches = 0;
#if UNITY_EDITOR
            draws = UnityEditor.UnityStats.drawCalls; tris = UnityEditor.UnityStats.triangles; batches = UnityEditor.UnityStats.batches;
#endif
            long mono = Profiler.GetMonoUsedSizeLong() / (1024 * 1024), alloc = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
            int cats = Object.FindObjectsByType<CatBrain>(FindObjectsSortMode.None).Count(c => c.isActiveAndEnabled);
            string line = $"cats={cats} avgMs={total / frames * 1000:F1} worstMs={worst * 1000:F1} drawCalls={draws} batches={batches} tris={tris} monoMB={mono} allocMB={alloc}";
            Debug.Log("[Perf] " + line);
            Directory.CreateDirectory("Builds"); File.WriteAllText("Builds/perf.txt", line + "\n");
            Assert.AreEqual(5, cats);
            if (draws > 0) Assert.Less(draws, 400, "그리기 횟수가 휴대폰에 너무 많다");
        }

        [UnityTest]
        public IEnumerator Weather_RainAndSnow_Show()
        {
            game.Logic.AddCat("korean_shorthair", "나비", CatIsland.Game.Personality.Playful); game.SyncCats();
            game.IslandCam.zoomLevel = 0; game.IslandCam.SnapNow();
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Shots/day")); Directory.CreateDirectory(dir);
            foreach (var w in new[] { CatIsland.Game.WeatherKind.Rain, CatIsland.Game.WeatherKind.Snow })
            {
                WeatherFx.Override = w; game.Weather.Set(w); game.Day.Apply(13f);
                yield return new WaitForSeconds(w == CatIsland.Game.WeatherKind.Snow ? 5f : 1.5f);
                var ps = game.Weather.GetComponentInChildren<ParticleSystem>();
                Assert.Greater(ps.particleCount, 20, w + " particles fall");
                var cam = Cam; var rt = new RenderTexture(590, 1278, 24) { antiAliasing = 4 }; cam.targetTexture = rt; cam.Render(); cam.Render();
                RenderTexture.active = rt; var tex = new Texture2D(590, 1278, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 590, 1278), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, w.ToString().ToLower() + ".png"), tex.EncodeToPNG()); cam.targetTexture = null; rt.Release();
            }
            WeatherFx.Override = CatIsland.Game.WeatherKind.Clear; game.Weather.Set(CatIsland.Game.WeatherKind.Clear);
        }

        [UnityTest]
        public IEnumerator Friends_ChaseAndVisit()
        {
            var g = game.Logic; g.S.catSlots = 4;
            var a = g.AddCat("korean_shorthair", "나비", CatIsland.Game.Personality.Playful);
            var b = g.AddCat("persian", "보리", CatIsland.Game.Personality.Easygoing);
            game.SyncCats(); yield return new WaitForSeconds(.5f);
            var cats = Object.FindObjectsByType<CatBrain>(FindObjectsSortMode.None).Where(c => c.Data != null).ToList();
            var A = cats.First(c => c.Data == a); var B = cats.First(c => c.Data == b);
            foreach (var c in cats) { c.Needs.SetForTest(1f, 1f); c.ForceState(CatState.Idle); }
            A.transform.position = new Vector3(-.8f, 0, -.8f); B.transform.position = new Vector3(.8f, 0, -.8f);
            yield return new WaitForSeconds(1.5f);   // (서기 자세로)
            for (int k = 0; k < 20 && !A.TryFriend(); k++) yield return new WaitForSeconds(.3f);
            Assert.AreEqual(CatState.Chase, A.State, "장난꾸러기는 쫓기 놀이를 건다");
            Assert.AreEqual(CatState.Flee, B.State);
            Time.timeScale = 2f; float closest = 9f;
            for (float t = 0; t < 7f && (A.State == CatState.Chase || B.State == CatState.Flee); t += Time.deltaTime) { yield return null; closest = Mathf.Min(closest, Vector3.Distance(A.Rig.BodyZone.position, B.Rig.BodyZone.position)); }
            Time.timeScale = 1f;
            Assert.AreNotEqual(CatState.Chase, A.State, "쫓기 놀이는 짧게 끝난다");
            Assert.Greater(closest, .25f, "쫓아도 몸이 겹치지 않는다");
            // 느긋한 고양이는 옆에 가서 같이 앉는다
            yield return new WaitForSeconds(1f);
            B.ForceState(CatState.Idle); A.ForceState(CatState.Idle); yield return new WaitForSeconds(1.2f);
            for (int k = 0; k < 20 && !B.TryFriend(); k++) yield return new WaitForSeconds(.3f);
            Assert.AreEqual(CatState.Visit, B.State);
            Time.timeScale = 2f; yield return new WaitForSeconds(5f); Time.timeScale = 1f;
            Assert.Less(Vector3.Distance(A.transform.position, B.transform.position), 1.1f, "친구 옆에 앉는다");
        }

        [UnityTest]
        public IEnumerator FaceVariants_AndPhotoCoat()
        {
            var face = Cat.Rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.sharedMesh.blendShapeCount > 0);
            if (!CatFace.Variants(face).Any() && !face.sharedMaterials.Any(m => m.name.Contains("__")))
            {
                // (예전 에셋: 얼굴에 고를 조각이 없다. 33품종을 다시 만든 뒤부터 검사)
                Assert.Ignore("face variants not in this asset yet");
            }
            yield return null;
            int Tris(SkinnedMeshRenderer r) => Enumerable.Range(0, r.sharedMesh.subMeshCount).Sum(i => (int)r.sharedMesh.GetIndexCount(i)) / 3;
            Cat.Rig.SetFace("dark", "short"); int dark = Tris(face); int mats = face.sharedMaterials.Length;
            Cat.Rig.SetFace("rim", "long"); int rim = Tris(face);
            Assert.AreNotEqual(dark, rim, "눈 모양을 바꾸면 얼굴 조각이 바뀐다");
            Assert.AreEqual(mats, face.sharedMaterials.Length, "고른 조각은 원래 재질에 합쳐진다 (그리기 횟수 그대로)");
            Assert.IsFalse(face.sharedMaterials.Any(m => m.name.Contains("__")), "고르지 않은 조각은 남지 않는다");

            // 사진 고양이 털: 마스크가 있으면 셰이더 색 바꾸기가 켜진다
            var body = Cat.Rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.sharedMesh.blendShapeCount == 0);
            bool ok = CatCoat.Apply(body, Cat.Rig.breed, "{\"pattern\":\"tuxedo\",\"variant\":\"tuxedo\",\"base\":\"#303034\",\"dark\":\"#26262a\",\"white\":\"#f6f2ea\",\"second\":\"#d98a3a\",\"whiteLevel\":0.4}");
            if (Resources.Load<Texture2D>("Art/Cats/" + Cat.Rig.breed + "_coatmask") != null)
            {
                Assert.IsTrue(ok);
                Assert.IsTrue(body.material.IsKeywordEnabled("_COATMASK"));
            }
            Assert.IsFalse(CatCoat.Apply(body, Cat.Rig.breed, "not json"), "잘못된 값은 아무것도 바꾸지 않는다");
        }
    }
}
