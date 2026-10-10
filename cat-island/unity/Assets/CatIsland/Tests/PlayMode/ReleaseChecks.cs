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
        public IEnumerator Items_JiggleAndRoll_ThenSettleBack()
        {
            var g = game.Logic; g.AddCoins(5000);
            Assert.IsTrue(g.Buy("mouse_toy")); Assert.IsTrue(g.Place("mouse_toy", CatIsland.Game.Zone.Indoor, 6, 11, 2));
            game.WorldLink.Refresh(); yield return null;
            var tag = Object.FindObjectsByType<ItemTag>(FindObjectsSortMode.None).First(t => t.id == "mouse_toy");
            var model = tag.transform.Find("Model"); var s0 = model.localScale; var p0 = model.localPosition;
            ItemJiggle.Poke(tag.transform, .6f); yield return null; yield return null;
            Assert.AreNotEqual(s0, model.localScale, "톡 치면 눌린다");
            ItemJiggle.Kick(tag.transform, Vector3.right, .2f);
            yield return new WaitForSeconds(.8f);
            Assert.Greater(Vector3.Distance(model.localPosition, p0), .1f, "굴러간다");
            yield return new WaitForSeconds(2.5f);
            Assert.Less(Vector3.Distance(model.localPosition, p0), .002f, "제자리로 돌아온다");
            Assert.Less(Vector3.Distance(model.localScale, s0), .002f);
        }

        /// <summary>고양이 셋: 다른 고양이를 누르면 '지금 고양이'가 되어 카메라가 따라가고, 바닥을 누르면 그 고양이가 온다(울지 않고). 손가락이 닿자마자 기댄다.</summary>
        [UnityTest]
        public IEnumerator ThreeCats_TapToSelect_GroundTapCallsSelected_NoMeowPerTap()
        {
            var g = game.Logic; g.S.catSlots = 4;
            foreach (var (b, n) in new[] { ("korean_shorthair", "나비"), ("persian", "보리"), ("siamese", "달이") }) g.AddCat(b, n, CatIsland.Game.Personality.Easygoing);
            game.SyncCats(); for (int i = 0; i < 30; i++) yield return null;
            var cats = Object.FindObjectsByType<CatBrain>(FindObjectsSortMode.None).Where(c => c.Data != null).OrderBy(c => c.Data.name).ToList();
            Assert.AreEqual(3, cats.Count);
            var other = cats.First(c => c != game.Router.cat);
            foreach (var c in cats) { c.Needs.SetForTest(1f, 1f); c.ForceState(CatState.SitIdle); }
            game.IslandCam.SnapNow(); yield return null;
            // (그 고양이 머리를 톡)
            game.IslandCam.follow = other.transform; game.IslandCam.SnapNow(); yield return null; game.IslandCam.follow = cats.First(c => c != other).transform;
            yield return Tap(Screen(other.Rig.HeadZone.position));
            Assert.AreSame(other, game.Router.cat, "누른 고양이가 지금 고양이"); Assert.AreSame(other.transform, game.IslandCam.follow, "카메라가 따라간다");
            var ring = GameObject.Find("SelectRing"); Assert.IsNotNull(ring, "발밑 고리"); yield return new WaitForSeconds(.3f);
            var rb = ring.GetComponent<MeshRenderer>().bounds; Debug.Log($"[Ring] active {ring.activeInHierarchy} visible {ring.GetComponent<MeshRenderer>().isVisible} center {rb.center} size {rb.size} scale {ring.transform.localScale} cat {other.transform.position} verts {ring.GetComponent<MeshFilter>().sharedMesh.vertexCount}");
            // (카메라 화면을 받아 고리 자리 둘레에 분홍(딸기우유) 화소가 있는지)
            var camC = game.IslandCam.GetComponent<Camera>(); var rt = new RenderTexture(540, 1170, 24); var prevT = camC.targetTexture; camC.targetTexture = rt; camC.Render(); camC.targetTexture = prevT;
            RenderTexture.active = rt; var shot = new Texture2D(540, 1170, TextureFormat.RGB24, false); shot.ReadPixels(new Rect(0, 0, 540, 1170), 0, 0); shot.Apply(); RenderTexture.active = null;
            var sp = camC.WorldToViewportPoint(rb.center); int pink = 0;
            for (int y = -60; y <= 60; y += 2) for (int x = -90; x <= 90; x += 2)
                { var c = shot.GetPixel((int)(sp.x * 540) + x, (int)(sp.y * 1170) + y); if (c.r > .85f && c.g > .35f && c.g < .65f && c.b > .45f && c.b < .75f) pink++; }
            Debug.Log($"[Ring] pink {pink} at viewport {sp}"); rt.Release(); Object.Destroy(shot);
            Assert.Greater(pink, 20, "고리가 화면에 그려진다");
            var mesh = ring.GetComponent<MeshFilter>().sharedMesh; Assert.Greater(mesh.normals.Average(n => n.y), .5f, "고리 면이 위를 본다");
            // 바닥을 누르면 그 고양이가 온다, 울지 않는다
            yield return new WaitForSeconds(2.2f);
            var au = other.GetComponent<CatAudio>(); int played = au.PlayedCount;
            var ground = other.transform.position + new Vector3(1.2f, 0f, 0f);
            yield return Tap(Screen(ground));
            Assert.AreEqual(CatState.Called, other.State, "바닥을 누르면 지금 고양이가 온다");
            Assert.AreEqual(played, au.PlayedCount, "바닥을 누를 때 울지 않는다");
            // 손가락이 닿자마자 기댄다 (문지르기 전에도)
            other.ForceState(CatState.SitIdle); yield return new WaitForSeconds(.5f);
            fingers.Press(Screen(other.Rig.HeadZone.position)); for (int i = 0; i < 6; i++) yield return null;
            Assert.Greater(other.Rig.petLean, .4f, "닿자마자 기댄다"); fingers.Release(); yield return null;
        }

        /// <summary>'지금 고양이'가 산책을 나가 섬에서 사라진 뒤 바닥·그릇을 눌러도 오류 없이 남은 고양이가 받는다.</summary>
        [UnityTest]
        public IEnumerator CurrentCatLeavesForWalk_TapsStillWork()
        {
            var g = game.Logic; g.S.catSlots = 3;
            var a = g.AddCat("korean_shorthair", "나비", CatIsland.Game.Personality.Easygoing); var b = g.AddCat("persian", "보리", CatIsland.Game.Personality.Easygoing);
            game.SyncCats(); for (int i = 0; i < 20; i++) yield return null;
            var cats = Object.FindObjectsByType<CatBrain>(FindObjectsSortMode.None).Where(c => c.Data != null).ToList();
            var cur = cats.First(c => c.Data == a); game.SelectCat(cur); yield return null;
            Assert.IsTrue(g.SendWalk(a.uid, CatIsland.Game.Catalog.WalkHours[0]), "산책 보내기");
            game.SyncCats(); game.WorldLink.Refresh();
            Time.timeScale = 4f; for (float t = 0; t < 40f && cur.isActiveAndEnabled; t += Time.deltaTime) yield return null; Time.timeScale = 1f;   // (바닷가로 걸어 나가 숨을 때까지)
            Assert.IsFalse(cur.isActiveAndEnabled, "산책 간 고양이는 섬에서 사라진다");
            var bowlPos = game.Bowl ? game.Bowl.transform.position : Vector3.zero;
            yield return Tap(Screen(new Vector3(.5f, 0f, -1.2f)));
            if (game.Bowl && game.Bowl.gameObject.activeInHierarchy) yield return Tap(Screen(bowlPos + Vector3.up * .05f));
            yield return null;
            Assert.IsTrue(game.Router.cat && game.Router.cat.isActiveAndEnabled, "남은 고양이가 지금 고양이");
            Assert.AreEqual(b.uid, game.Router.cat.Data.uid); Assert.AreSame(game.Router.cat.transform, game.IslandCam.follow, "카메라도 남은 고양이를 본다");
        }

        /// <summary>처음 7일 안내 카드: 보이면 글자가 있다 (글자 넣기가 빠져 빈 카드가 뜬 적이 있다), 바닥 깔기 중에는 숨는다.</summary>
        [UnityTest]
        public IEnumerator HintCard_ShowsItsText_AndHidesWhileTiling()
        {
            game.Logic.AddCat("korean_shorthair", "나비", CatIsland.Game.Personality.Playful); game.Logic.S.onboardingStep = 1; game.SyncCats(); yield return null;
            game.UI.Refresh(); yield return null;
            var hint = Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(t => t.transform.parent && t.transform.parent.name == "Hint");
            Assert.IsTrue(hint.gameObject.activeInHierarchy); Assert.AreEqual(CatIsland.UI.Str.Hint("hint_feed"), hint.text);
            var mode = TileMode.Begin(game, null); yield return null;
            Assert.IsFalse(hint.gameObject.activeInHierarchy, "바닥 깔기 막대와 겹치지 않게");
            mode.Finish(); yield return null; game.UI.Refresh();
            Assert.IsTrue(hint.gameObject.activeInHierarchy);
        }

        /// <summary>바닥 깔기: 손가락으로 쓸면 지나간 칸이 이어서 깔리고(빨리 쓸어도 빈칸 없음), 발소리 재질이 바뀌고, 걷으면 가방으로, 끝내면 저장·원래 화면.</summary>
        [UnityTest]
        public IEnumerator DeckTiles_PaintLine_Lift_AndFinish()
        {
            var g = game.Logic; int bag = g.Tiles("deck_honey"); Assert.Greater(bag, 4);
            var mode = TileMode.Begin(game, "deck_honey"); yield return null;
            Assert.IsTrue(game.IslandCam.Manual, "깔기 모드는 카메라를 이용자가 옮긴다"); Assert.IsFalse(game.Router.enabled);
            Vector3 Cell(int x, int z) => WorldSync.CellToWorld(CatIsland.Game.Zone.Indoor, x + .5f, z + .5f);
            mode.PaintAtWorld(Cell(2, 3)); mode.PaintAtWorld(Cell(6, 3));   // (한 번에 4칸 건너뛰어도)
            for (int x = 2; x <= 6; x++) Assert.AreEqual("deck_honey", FloorTiles.IdAt(CatIsland.Game.Zone.Indoor, x, 3), $"칸 {x}");
            Assert.AreEqual(bag - 5, g.Tiles("deck_honey"));
            Assert.AreEqual(Surface.Wood, IslandBuilder.SurfaceAt(Cell(4, 3))); Assert.AreEqual(Surface.Grass, IslandBuilder.SurfaceAt(Cell(4, 6)));
            var mf = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).First(m => m.name.StartsWith("Tiles_deck_honey"));
            Assert.AreEqual(5 + 5 + 5 + 2, mf.sharedMesh.vertexCount / 4, "윗면 5 + 바깥 옆면만 (앞뒤 5씩, 양 끝 1씩)");
            mode.SetTool(TileMode.Tool.Lift); mode.PaintAtWorld(Cell(4, 3)); yield return null;
            Assert.IsNull(FloorTiles.IdAt(CatIsland.Game.Zone.Indoor, 4, 3)); Assert.AreEqual(bag - 4, g.Tiles("deck_honey"));
            // (깔다가 손가락이 왼쪽 가장자리에 닿으면 화면이 왼쪽으로 따라간다, 멀리·가까이)
            var f0 = game.IslandCam.ManualFocus; for (int i = 0; i < 20; i++) mode.EdgeScroll(new Vector2(2f, UnityEngine.Screen.height * .5f), .05f);
            Assert.Less(game.IslandCam.ManualFocus.x, f0.x - .5f, "왼쪽 가장자리 → 왼쪽으로");
            float d0 = game.IslandCam.ManualDist; mode.ToggleZoom(); Assert.Greater(game.IslandCam.ManualDist, d0); mode.ToggleZoom(); Assert.AreEqual(d0, game.IslandCam.ManualDist, 1e-4);
            mode.Finish(); yield return null;
            Assert.IsFalse(game.IslandCam.Manual); Assert.IsTrue(game.Router.enabled); Assert.IsNull(TileMode.Active);
            Assert.AreEqual(4, g.S.floor.Count);
        }

        /// <summary>고양이를 가리는 용품은 점무늬로 비치고(_FADE 재질), 비키면 원래 재질로 돌아온다.</summary>
        [UnityTest]
        public IEnumerator Occluder_InFrontOfCat_FadesThenComesBack()
        {
            var cam = game.IslandCam; var fade = cam.GetComponent<OccluderFade>(); Assert.IsNotNull(fade);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(box.GetComponent<Collider>());
            box.AddComponent<ItemTag>().id = "test_box"; box.transform.localScale = new Vector3(.6f, .6f, .6f);
            var mr = box.GetComponent<MeshRenderer>(); var mat = Resources.Load<Material>("CatSoftLit"); mr.sharedMaterial = mat;
            for (float t = 0; t < .8f; t += Time.deltaTime)
            {
                var c = game.Cat.transform.position + Vector3.up * .3f;
                box.transform.position = Vector3.Lerp(cam.transform.position, c, .5f);   // (카메라와 고양이 사이)
                yield return null;
            }
            Assert.IsTrue(fade.IsFaded(box.transform), "고양이를 가리면 비친다");
            Assert.IsTrue(mr.sharedMaterial.IsKeywordEnabled("_FADE"));
            Assert.Less(mr.sharedMaterial.GetFloat("_Fade"), .5f);
            box.transform.position = game.Cat.transform.position + new Vector3(30f, 0f, 0f);   // (멀리 비킨다)
            yield return new WaitForSeconds(.6f);
            Assert.IsFalse(fade.IsFaded(box.transform), "비키면 돌아온다");
            Assert.AreSame(mat, mr.sharedMaterial, "원래 재질 그대로");
            Object.Destroy(box);
        }

        [UnityTest]
        public IEnumerator NoBrokenBounds_WhileBootingAndPlaying()
        {
            LogAssert.ignoreFailingMessages = true;   // (엔진 경고 대신 어떤 물체인지 직접 찾는다)
            var g = game.Logic; g.S.catSlots = 4; g.AddCat("korean_shorthair", "나비", CatIsland.Game.Personality.Playful); g.AddCat("persian", "보리", CatIsland.Game.Personality.Easygoing);
            game.SyncCats();
            string bad = null;
            for (int f = 0; f < 240 && bad == null; f++)
            {
                yield return null;
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    var b = r.bounds; var c = b.center; var e = b.extents;
                    if (float.IsNaN(c.x + c.y + c.z + e.x + e.y + e.z) || e.magnitude > 1000f || c.magnitude > 10000f)
                    { bad = $"{r.name} ({r.GetType().Name}) frame {f} center {c} ext {e} parent {(r.transform.parent ? r.transform.parent.name : "-")}"; break; }
                }
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                {
                    var p = t.position; var sc = t.lossyScale;
                    if (float.IsNaN(p.x + p.y + p.z + sc.x + sc.y + sc.z)) { bad = $"transform {t.name} parent {(t.parent ? t.parent.name : "-")} frame {f} pos {p} scale {sc}"; break; }
                }
            }
            LogAssert.ignoreFailingMessages = false;
            Assert.IsNull(bad, bad);
        }

        /// <summary>오래 놀게 두기: 고양이 5마리가 2분(4배속) 동안 돌아다니고 쫓고 옆에 앉아도 물건·서로에 박히지 않는다.</summary>
        [UnityTest]
        public IEnumerator Soak_FiveCats_NoOneInsideItemsOrEachOther()
        {
            var g = game.Logic; g.S.catSlots = 5; g.AddCoins(99999);
            foreach (var (b, n, p) in new[] { ("korean_shorthair", "나비", CatIsland.Game.Personality.Playful), ("munchkin", "콩", CatIsland.Game.Personality.Playful), ("persian", "보리", CatIsland.Game.Personality.Easygoing), ("siamese", "달이", CatIsland.Game.Personality.Playful), ("maine_coon", "호두", CatIsland.Game.Personality.Easygoing) })
                g.AddCat(b, n, p);
            foreach (var (id, x, z) in new[] { ("cushion", 7, 9), ("hideout", 10, 6), ("mouse_toy", 8, 11), ("scratcher", 5, 7) })
                if (g.Buy(id)) g.Place(id, CatIsland.Game.Zone.Indoor, x, z, 2);
            game.WorldLink.Refresh(); yield return null; game.SyncCats(); yield return null;   // (용품을 먼저 세워야 고양이가 그 밖에 선다)
            var cats = Object.FindObjectsByType<CatBrain>(FindObjectsSortMode.None).Where(c => c.Data != null).ToList();
            foreach (var c in cats) c.Needs.SetForTest(1f, 1f);
            foreach (var c in cats) Debug.Log($"[Soak] start {c.name} {c.transform.position} {c.State}");
            foreach (var o in game.Nav.obstacles.Where(o => o.owner == null && o.item)) Debug.Log($"[Soak] item {o.name} {o.center} half {o.half} r {o.radius}");
            float logT = 0f;
            Time.timeScale = 4f; float worst = 0f, closest = 9f; string what = "", pair = "";
            float maxStep = 0f; string stepWhat = ""; var hist = new System.Collections.Generic.Dictionary<CatBrain, System.Collections.Generic.List<string>>();
            bool jumpLogged = false; var prevPos = new System.Collections.Generic.Dictionary<CatBrain, Vector3>(); var prevState = new System.Collections.Generic.Dictionary<CatBrain, string>();
            foreach (var c in cats) { prevPos[c] = c.transform.position; prevState[c] = c.State.ToString(); }
            var run = new System.Collections.Generic.Dictionary<string, float>(); float longest = 0f; string longPair = "";   // (3 cm 넘게 겹친 채 이어진 시간)
            for (float t = 0; t < 120f; t += Time.deltaTime)
            {
                if (t > 0f) foreach (var c in cats)
                    {
                        if (prevState[c] != c.State.ToString()) { if (!hist.TryGetValue(c, out var hl)) hist[c] = hl = new System.Collections.Generic.List<string>(); hl.Add($"{t:F1}s {prevState[c]}->{c.State} @({c.transform.position.x:F2},{c.transform.position.z:F2})"); if (hl.Count > 6) hl.RemoveAt(0); }
                        prevPos[c] = c.transform.position; prevState[c] = c.State.ToString();
                    }
                yield return null;
                if (t < 8f && t - logT > .5f) { logT = t; foreach (var c in cats) Debug.Log($"[Soak] t {t:F1} {c.name} {c.transform.position} {c.State}"); }
                foreach (var k in cats)
                {
                    if (!k.isActiveAndEnabled) continue;
                    // (순간이동: 걷기·달리기로는 한 프레임에 0.2 m 도 못 간다 - 점프·캣타워·산책 나가고 들어오기는 빼고)
                    string ks = k.State.ToString();
                    if (!k.OnTower && !ks.Contains("Jump") && !ks.Contains("Tower") && !ks.Contains("Walk"))
                    {
                        // (그 프레임 동안 가장 빠른 달리기로 갈 수 있는 거리를 뺀 나머지: 긴 프레임에서 정상으로 달린 것은 순간이동이 아니다)
                        float step = Vector3.Distance(new Vector3(k.transform.position.x, 0, k.transform.position.z), new Vector3(prevPos[k].x, 0, prevPos[k].z));
                        float extra = step - GameConfig.RunSpeed * 1.6f * Time.deltaTime;
                        if (extra > maxStep) { maxStep = extra; stepWhat = $"{k.name} {prevState[k]}->{ks} {prevPos[k]}->{k.transform.position} step {step * 1000:F0} mm dt {Time.deltaTime * 1000:F0} ms t {t:F1}"; }
                    }
                    bool up = k.OnTower || k.State.ToString().Contains("Jump") || k.State.ToString().Contains("Tower") || k.State == CatState.UseItem || k.State == CatState.GoToItem;
                    bool onCushion = k.transform.position.y > .05f;   // (방석 위에 올라서 있음: 방석 '안'이 아니다)
                    foreach (var o in game.Nav.obstacles.Where(o => o.owner == null && !(up && o.item == game.Tower.transform)))
                    {
                        if (up && k.State == CatState.UseItem) continue;   // (쓰는 중인 용품 안·위는 맞다)
                        if (onCushion && game.Cushion && o.item == game.Cushion.transform) continue;   // (방석 위에 올라서 있음)
                        if (game.Cushion && o.item == game.Cushion.transform && (k.State == CatState.GoToCushion || k.State == CatState.LieDown || k.State == CatState.Sleep)) continue;
                        if (k.LeftItem && o.item == k.LeftItem && Time.time - k.LeftAt < 12f) continue;   // (방금 쓰고 나오는 중)
                        if (k.EnteringItem && o.item == k.EnteringItem) continue;   // (문으로 들어가는 중)
                        float d = o.Distance(k.Rig.BodyZone.position) - k.Rig.BodyHalf.x * .8f;
                        if (d < worst) { worst = d; what = (hist.TryGetValue(k, out var hh) ? "[" + string.Join(" | ", hh) + "] " : "") + $"{k.name} in {o.name} ({k.State}, {k.Rig.Current}, y {k.transform.position.y:F2}, t {t:F0}) cat {k.transform.position} body {k.Rig.BodyZone.position} ob {o.center} half {o.half} r {o.radius} item {(o.item ? o.item.position.ToString() : "-")} left {(k.LeftItem ? k.LeftItem.name : "-")} {Time.time - k.LeftAt:F1}s target {(k.EnteringItem ? k.EnteringItem.name : "-")}"; }
                    }
                    foreach (var k2 in cats) if (k2 != k && k2.isActiveAndEnabled && !k.OnTower && !k2.OnTower)
                        { float dd = CatBrain.Gap(k, k2, out _);
                            var key = k.name + "|" + k2.name;
                            if (dd < -.08f && !jumpLogged) { jumpLogged = true; Debug.Log($"[Soak] deep {key} gap {dd * 1000:F0} t {t:F2} now {k.transform.position}/{k2.transform.position} {k.State}/{k2.State} {k.Rig.Current}/{k2.Rig.Current} prev {prevPos[k]}/{prevPos[k2]} prevState {prevState[k]}/{prevState[k2]}"); } run[key] = dd < -.03f ? (run.TryGetValue(key, out var r0) ? r0 : 0f) + Time.deltaTime / 4f : 0f;
                            if (run[key] > longest) { longest = run[key]; longPair = $"{k.name}({k.State}) - {k2.name}({k2.State}) t {t:F0} gap {dd * 1000:F0} mm"; } if (dd < closest) { closest = dd; pair = $"{k.name}({k.State}, {k.Rig.Current}) - {k2.name}({k2.State}, {k2.Rig.Current}) t {t:F0} gap {dd * 1000:F0} mm pos {k.transform.position} {k2.transform.position} head {k.Rig.HeadZone.position} {k2.Rig.HeadZone.position} body {k.Rig.BodyZone.position} {k2.Rig.BodyZone.position} hr {k.Rig.HeadRadius:F3} bh {k.Rig.BodyHalf}"; } }
                }
            }
            Time.timeScale = 1f;
            Debug.Log($"[Soak] worst item {worst * 1000:F0} mm {what}; closest cats {pair}; longest overlap {longest:F2} s {longPair}");
            // (용품 막힘은 격자 칸 크기 상자(한 칸 0.54 m)로 잰다: 대부분 용품은 그보다 작아 8 cm 안쪽까지는 실제로 닿지 않는다)
            Debug.Log($"[Soak] biggest step beyond running {maxStep * 1000:F0} mm {stepWhat}");
            Assert.Less(maxStep, .15f, "순간이동: " + stepWhat);
            Assert.Greater(worst, -0.08f, what);
            // (머리·몸통 모양 사이 틈: 3 cm 넘게 겹친 채로는 0.25초를 넘기지 않는다 - 곧 비켜 선다. 제자리에서 몸을 돌리다 엉덩이가
            //  지나가는 고양이를 스치는 순간은 위치로 막을 수 없어 깊이는 15 cm 까지 본다)
            Assert.Greater(closest, -0.15f, pair);
            Assert.Less(longest, .25f, longPair);
        }

        [UnityTest]
        public IEnumerator Settings_SoundMusicHaptics_ReallyTurnOff_AndMusicFollowsNight()
        {
            var g = game.Logic; g.AddCat("korean_shorthair", "나비", CatIsland.Game.Personality.Playful); game.SyncCats(); yield return null;
            var bgm = game.GetComponent<BackgroundMusic>(); Assert.NotNull(bgm);
            var audio = Cat.GetComponent<CatAudio>();
            g.S.soundOn = false; g.S.musicOn = false; g.S.hapticsOn = false; yield return null;
            int before = audio.PlayedCount; audio.Meow();
            Assert.AreEqual(before, audio.PlayedCount, "효과음을 끄면 야옹도 안 난다");
            Haptics.SetPurr(1f); Assert.AreEqual(0f, Haptics.PurrIntensity, "진동을 끄면 골골 진동도 없다");
            yield return new WaitForSecondsRealtime(1f);
            float vol = game.GetComponents<AudioSource>().Sum(s => s.volume);
            Assert.Less(vol, .02f, "음악을 끄면 조용해진다");
            g.S.soundOn = g.S.musicOn = g.S.hapticsOn = true;
            DayCycle.HourOverride = 23f; game.Day.Apply(23f);
            yield return new WaitForSecondsRealtime(9f);
            Assert.AreEqual("night", bgm.Current, "밤에는 밤 곡");
            DayCycle.HourOverride = 13f; game.Day.Apply(13f);
        }

        [UnityTest]
        public IEnumerator FaceVariants_AndPhotoCoat()
        {
            var face = Cat.Rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.sharedMesh.blendShapeCount > 0);
            var prefab = Resources.Load<GameObject>("Art/Cats/" + Cat.Rig.breed);
            var srcFace = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.sharedMesh.blendShapeCount > 0);
            if (!CatFace.Variants(srcFace).Any())   // (게임 고양이는 이미 고른 조각만 합쳐 둔 상태: 원본 프리팹으로 본다)
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
