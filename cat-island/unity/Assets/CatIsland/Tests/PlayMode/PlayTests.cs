using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CatIsland.Tests
{
    /// <summary>공통: 장면을 띄우고 가짜 손가락을 끼운다.</summary>
    public abstract class SceneFixture
    {
        protected GameBootstrap game;
        protected ScriptedPointers fingers;
        protected CatBrain Cat => game.Cat;
        protected Camera Cam => game.IslandCam.GetComponent<Camera>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PlayerPrefs.DeleteAll();
            Haptics.ResetCounters();
            GameBootstrap.NewFiles = () => new CatIsland.Game.MemoryFiles();   // (사용자 저장을 건드리지 않는다)
            GameBootstrap.OpenCatMakerIfEmpty = false;
            Time.timeScale = 1f;
            var go = new GameObject("Bootstrap");
            game = go.AddComponent<GameBootstrap>();
            fingers = new ScriptedPointers();
            game.Router.source = fingers;
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name.Contains("tests runner") || root.name.Contains("TestRunner")) continue;
                UnityEngine.Object.Destroy(root);
            }
            yield return null;
        }

        protected Vector2 Screen(Vector3 world) => Cam.WorldToScreenPoint(world);

        protected Vector3 BackPoint => Cat.Rig.BodyZone.TransformPoint(new Vector3(0f, Cat.Rig.BodyHalf.y * 0.85f / Cat.Rig.BodyZone.lossyScale.y, 0f));
        protected Vector3 BellyPoint => Cat.Rig.BodyZone.TransformPoint(new Vector3(0f, -Cat.Rig.BodyHalf.y * 0.85f / Cat.Rig.BodyZone.lossyScale.y, 0f));
        protected Vector3 HeadPoint => Cat.Rig.HeadZone.position;

        /// <summary>부드럽게 문지르기: 목표 지점을 중심으로 좌우로 왕복.</summary>
        protected IEnumerator Stroke(Func<Vector3> target, float seconds, Func<bool> until = null, float amp = 0.014f, float hz = 7f)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (until != null && until()) break;
                Vector2 c = Screen(target());
                float off = Mathf.Sin(t * hz * Mathf.PI * 2f) * amp * UnityEngine.Screen.height;
                fingers.Press(c + new Vector2(off, off * 0.3f));
                yield return null;
                t += Time.deltaTime;
            }
            fingers.Release();
            yield return null;
        }

        protected IEnumerator Tap(Vector2 screen)
        {
            fingers.Press(screen);
            yield return null;
            yield return null;
            fingers.Release();
            yield return null;
            yield return null;
        }

        protected IEnumerator WaitUntil(Func<bool> cond, float maxSeconds)
        {
            float t = 0f;
            while (!cond() && t < maxSeconds) { yield return null; t += Time.deltaTime; }
        }

        protected static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    }

    public class CorePlayTests : SceneFixture
    {
        [UnityTest]
        public IEnumerator Boot_PipelineCatAndItemsLoad()
        {
            Assert.IsNotNull(Cat.Rig.Anim, "animator");
            Assert.IsNotNull(Cat.Rig.Info.jump, "jump root curve");
            Assert.Greater(Cat.Rig.HeadRadius, 0.1f, "head zone fitted from mesh");
            Assert.IsTrue(Cat.Rig.HasClip("Walk") && Cat.Rig.HasClip("Sit") && Cat.Rig.HasClip("Sleep") && Cat.Rig.HasClip("Drink"));
            Assert.IsNotNull(game.Bowl.transform.Find("Model"));
            Assert.IsNotNull(game.Tower);
            Assert.AreEqual(0.2f, game.Tower.DeckHeight, 0.001f);
            Assert.AreEqual(0.134f, game.Cushion.TopHeight, 0.002f);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(Posture.Stand, Cat.Rig.Current);
        }

        [UnityTest]
        public IEnumerator GentleStrokes_CatSits_Purrs_EyesClose_Hearts()
        {
            Cat.Needs.SetForTest(1f, 1f);
            Cat.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // 옆모습: 등이 잘 보이게
            int maxFx = 0, hits = 0, frames = 0;
            float affection0 = Cat.Affection.Points;
            yield return Stroke(() => BackPoint, 3.5f, () =>
            {
                maxFx = Mathf.Max(maxFx, FxPool.Instance.ActiveCount);
                frames++;
                if (game.Router.LastZone != PetZone.None) hits++;
                return false;
            });
            Assert.Greater(hits, frames / 2, "strokes should land on the cat");
            Assert.IsTrue(Cat.Pet.Purring, "purring");
            Assert.AreEqual(Posture.Sit, Cat.Rig.Current, "sits down to enjoy it");
            Assert.Less(Cat.Rig.eyeOpen, 0.7f, "happy eyes closing");
            Assert.Greater(game.Audio.PurrVolume, 0f, "purr audio");
            Assert.Greater(maxFx, 0, "hearts floated up");
            Assert.Greater(Cat.Affection.Points, affection0 + 5f);
        }

        [UnityTest]
        public IEnumerator Walking_BodyStaysOnTheCat_NoSnapBack()
        {
            // 클립의 루트 이동이 남아 있으면 몸이 앞으로 밀려 나갔다가 주기마다 제자리로 튕긴다
            Cat.Needs.SetForTest(1f, 1f);
            var hips = System.Array.Find(Cat.Rig.Model.GetComponentsInChildren<Transform>(), b => b.name == "Hips");
            float bindZ = Cat.transform.InverseTransformPoint(hips.position).z;
            Cat.OnTapGround(new Vector3(0f, 0f, 3.5f));
            float t = 0f, maxDev = 0f, maxJump = 0f, prev = bindZ;
            while (t < 4f)
            {
                yield return null;
                t += Time.deltaTime;
                float z = Cat.transform.InverseTransformPoint(hips.position).z;
                if (Cat.Rig.Playing == "Move") { maxDev = Mathf.Max(maxDev, Mathf.Abs(z - bindZ)); maxJump = Mathf.Max(maxJump, Mathf.Abs(z - prev)); }
                prev = z;
            }
            Assert.Less(maxDev, 0.08f, "hips stay within a few cm of their place while walking");
            Assert.Less(maxJump, 0.03f, "no per-frame snap");
        }

        [UnityTest]
        public IEnumerator HoldingStillOnCat_IsNotPetting()
        {
            Cat.Needs.SetForTest(1f, 1f);
            float t = 0f;
            while (t < 1.5f) { fingers.Press(Screen(BackPoint)); yield return null; t += Time.deltaTime; }
            Assert.AreEqual(0f, Cat.Pet.Pleasure, 0.01f);
            Assert.IsFalse(Cat.Pet.Purring);
            fingers.Release();
            yield return null;
        }

        [UnityTest]
        public IEnumerator LongPetting_Flop_ThenBellyTrap_GentleNip()
        {
            Cat.Needs.SetForTest(1f, 1f);
            Time.timeScale = 2f;
            Cat.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            bool nipped = false;
            Cat.Nipped += () => nipped = true;

            yield return Stroke(() => BackPoint, 14f, () => Cat.State == CatState.BellyUp);
            Assert.AreEqual(CatState.BellyUp, Cat.State, "cat flops over after enough love");
            yield return WaitUntil(() => Cat.Rig.Current == Posture.Flop, 3f);
            Assert.AreEqual(Posture.Flop, Cat.Rig.Current, "Flop clip finished, lying on its back");

            int bellyHits = 0, frames = 0;
            yield return Stroke(() => BellyPoint, 2f, () =>
            {
                frames++;
                if (game.Router.LastZone == PetZone.Belly) bellyHits++;
                return nipped;
            });
            Assert.IsFalse(nipped, "no nip while trusting");
            Assert.Greater(bellyHits, frames / 3, "belly is reachable while flopped");

            yield return Stroke(() => BellyPoint, GameConfig.TrustWindow + 3f, () => nipped);
            Assert.IsTrue(nipped, "belly trap nip");
            Assert.AreEqual(Icon.Exclaim, Cat.Bubble.Current);
            yield return WaitUntil(() => Cat.State != CatState.Nip, 5f);
            Assert.AreNotEqual(CatState.Nip, Cat.State, "nip is brief, cat moves on");
        }

        [UnityTest]
        public IEnumerator HungryCat_FillBowl_DrinksAtBowlSpot_ThenSleepsOnCushion()
        {
            Cat.Needs.SetForTest(0.1f, 0.8f);
            yield return WaitUntil(() => Cat.Bubble.Current == Icon.Fish, 4f);
            Assert.AreEqual(Icon.Fish, Cat.Bubble.Current, "hungry bubble");

            Time.timeScale = 3f;
            yield return Tap(Screen(game.Bowl.transform.position + Vector3.up * 0.04f));
            Assert.IsTrue(game.Bowl.HasFood, "tap bowl fills it");

            yield return WaitUntil(() => Cat.Rig.ActionClip == "Drink", 25f);
            Assert.AreEqual("Drink", Cat.Rig.ActionClip, "cat eats with the Drink clip");
            // 그릇 중심이 clips.json Drink.drink.bowl 위치에 있어야 혀가 표면에 닿는다
            Vector3 local = Cat.transform.InverseTransformPoint(game.Bowl.transform.position);
            Assert.AreEqual(Cat.Rig.Info.bowlZ, local.z, 0.03f, "bowl forward offset");
            Assert.AreEqual(Cat.Rig.Info.bowlX, local.x, 0.03f, "bowl side offset");
            float hungerBefore = Cat.Needs.Hunger;

            bool lay = false;
            yield return WaitUntil(() => { lay |= Cat.State == CatState.LieDown; return Cat.State == CatState.Sleep && Cat.Rig.Current == Posture.Sleep; }, 45f);
            Assert.Greater(Cat.Needs.Hunger, hungerBefore);
            Assert.IsTrue(lay, "lies down (loaf) first");
            Assert.AreEqual(CatState.Sleep, Cat.State, "naps on the cushion");
            Vector3 c = Cat.transform.InverseTransformPoint(game.Cushion.transform.position);
            Assert.AreEqual(Cat.Rig.Info.cushionZ, c.z, 0.05f, "cushion under the body (clips.json itemSpots.cushion)");
            Assert.AreEqual(game.Cushion.TopHeight, Cat.transform.position.y, 0.01f, "raised onto the cushion top");
            yield return WaitUntil(() => Cat.StateTime > 2f, 5f);
            Assert.AreEqual(Icon.Sleep, Cat.Bubble.Current);
        }

        [UnityTest]
        public IEnumerator JumpOntoTower_StandsOnDeck_ThenJumpsDown()
        {
            Cat.Needs.SetForTest(1f, 1f);
            Time.timeScale = 3f;
            Cat.ForceState(CatState.GoToTower);
            float maxY = 0f;
            yield return WaitUntil(() => { maxY = Mathf.Max(maxY, Cat.transform.position.y); return Cat.State == CatState.OnTower; }, 30f);
            Assert.AreEqual(CatState.OnTower, Cat.State, "jumped up");
            Assert.IsTrue(Cat.OnTower);
            Assert.AreEqual(game.Tower.DeckHeight, Cat.transform.position.y, 0.01f, "standing on the deck");
            // qa_items.mjs 와 같은 배치: 판 뒤쪽 끝이 출발점에서 deckBack 앞 → 착지 지점은 판 중심에서 (deckBack + 깊이/2 - D) 뒤
            float expectBehind = Cat.Rig.Info.deckBack + game.Tower.DeckSize.y * 0.5f - Cat.Rig.Info.jumpD;
            Vector3 rel = game.Tower.transform.InverseTransformPoint(Cat.transform.position);
            Assert.AreEqual(-expectBehind, rel.z, 0.03f, "landed where qa_items checked the jump");
            Assert.Less(Mathf.Abs(rel.x), 0.03f);
            Assert.Greater(maxY, game.Tower.DeckHeight, "arc goes above the deck");

            Cat.OnTapGround(new Vector3(0f, 0f, 0.5f));
            yield return WaitUntil(() => Cat.State == CatState.JumpDown, 15f);
            Assert.AreEqual(CatState.JumpDown, Cat.State);
            if (Cat.Rig.HasClip("JumpDown"))
            {
                // 전용 내려오기: 올라온 쪽을 향해, 판 안쪽으로 turnIn 들어온 자리에서 출발 (qa_items JumpDown 장면과 같은 자리)
                Assert.AreEqual("JumpDown", Cat.Rig.ActionClip);
                Assert.Greater(Vector3.Dot(Cat.transform.forward, -game.Tower.transform.forward), 0.99f, "faces back the way it came up");
                Vector3 relStart = game.Tower.transform.InverseTransformPoint(Cat.transform.position);
                float expect = -(Cat.Rig.Info.deckBack + game.Tower.DeckSize.y * 0.5f - Cat.Rig.Info.jumpD) + Cat.Rig.Info.turnIn;
                Assert.AreEqual(expect, relStart.z, 0.03f, "starts where qa_items checked the jump down");
            }
            yield return WaitUntil(() => Cat.State != CatState.JumpDown, 10f);
            Assert.IsFalse(Cat.OnTower);
            Assert.AreEqual(0f, Cat.transform.position.y, 0.01f, "back on the ground");
            Assert.Greater(Flat(Cat.transform.position, game.Tower.transform.position), 0.6f, "landed off the tower");
        }

        [UnityTest]
        public IEnumerator PettingSleepingCat_Purrs_StaysAsleep()
        {
            Cat.Needs.SetForTest(1f, 0.1f);
            Cat.ForceState(CatState.Sleep);
            Time.timeScale = 3f;
            yield return WaitUntil(() => Cat.Rig.Current == Posture.Sleep, 10f);
            Time.timeScale = 1f;
            yield return Stroke(() => BackPoint, 2.5f);
            Assert.AreEqual(CatState.Sleep, Cat.State);
            Assert.Greater(Cat.Pet.Pleasure, 0.3f);
        }

        [UnityTest]
        public IEnumerator TapCat_Meows_TapGround_CatTrotsOver()
        {
            Cat.Needs.SetForTest(1f, 1f);
            int played = game.Audio.PlayedCount;
            yield return Tap(Screen(HeadPoint));
            Assert.Greater(game.Audio.PlayedCount, played, "meow");

            Vector3 spot = new Vector3(-1.6f, 0f, -1.2f);
            float before = Flat(Cat.transform.position, spot);
            yield return Tap(Screen(spot));
            Assert.AreEqual(CatState.Called, Cat.State);
            yield return new WaitForSeconds(2f);
            Assert.Less(Flat(Cat.transform.position, spot), before - 0.5f);
        }

        [UnityTest]
        public IEnumerator Footsteps_MatchTheGround()
        {
            Cat.Needs.SetForTest(1f, 1f);
            Time.timeScale = 2f;
            var seen = new System.Collections.Generic.HashSet<Surface>();
            foreach (var (want, expect) in new[] { (new Vector3(0.8f, 0f, 1.8f), Surface.Wood), (new Vector3(4.2f, 0f, -1.2f), Surface.Grass), (IslandBuilder.SandCenter, Surface.Sand) })
            {
                var spot = game.Nav.NearestFree(want);   // (꽃·덤불 근처면 길찾기가 옮기는 자리)
                Assert.AreEqual(expect, IslandBuilder.SurfaceAt(spot), "test spot surface");
                Cat.OnTapGround(spot);
                int before = game.Audio.StepCount;
                yield return WaitUntil(() => Flat(Cat.transform.position, spot) < 0.3f && game.Audio.StepCount > before + 1, 20f);
                Assert.Greater(game.Audio.StepCount, before, "footsteps while walking");
                Assert.AreEqual(expect, game.Audio.LastStepSurface, "step sound for " + expect);
                seen.Add(game.Audio.LastStepSurface);
                yield return new WaitForSeconds(0.5f);
            }
            Assert.AreEqual(3, seen.Count);
        }

        [UnityTest]
        public IEnumerator Taps_PlayRealMeows_NeverTheSameTwiceInARow()
        {
            Cat.Needs.SetForTest(1f, 1f);
            Assert.IsTrue(game.Audio.UsingRecordings, "real cat recordings (assets/sounds/cat) are used");
            Assert.GreaterOrEqual(game.Audio.MeowCount, 6);
            string prev = null;
            var heard = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 12; i++)
            {
                yield return Tap(Screen(HeadPoint));
                Assert.AreNotEqual(prev, game.Audio.LastMeow, "a different meow each tap");
                prev = game.Audio.LastMeow;
                heard.Add(prev);
                yield return new WaitForSeconds(0.3f);
            }
            Assert.GreaterOrEqual(heard.Count, 5, "many different meows over a dozen taps");
        }

        [UnityTest]
        public IEnumerator Roaming_NeverPassesThroughItemsOrPlants()
        {
            Cat.Needs.SetForTest(1f, 1f);
            Time.timeScale = 3f;
            var nav = game.Nav;
            float worst = 0f; string worstWhat = "";
            // 장애물을 가로지르게 되는 목적지들 (캣타워 건너편, 그릇 너머, 방석 너머, 나무 쪽, 모래밭, 덤불 쪽)
            var targets = new[]
            {
                game.Tower.transform.position + new Vector3(-1.4f, 0f, 0.2f), game.Tower.transform.position + new Vector3(1.3f, 0f, -0.3f),
                game.Bowl.transform.position + new Vector3(0.9f, 0f, 0.6f), new Vector3(-1.0f, 0f, -0.8f),
                game.Cushion.transform.position + new Vector3(-0.2f, 0f, 1.2f), new Vector3(-3.2f, 0f, 3.0f), IslandBuilder.SandCenter,
                new Vector3(3.6f, 0f, 2.2f), new Vector3(0.5f, 0f, -1.5f), game.Tower.transform.position + new Vector3(0f, 0f, 1.1f),
            };
            foreach (var target in targets)
            {
                Cat.OnTapGround(target);
                float t = 0f;
                while (t < 9f)
                {
                    yield return null;
                    t += Time.deltaTime;
                    var rig = Cat.Rig;
                    var samples = new System.Collections.Generic.List<(Vector3 p, float r)>
                    {
                        (rig.HeadZone.position, rig.HeadRadius * 0.75f),
                        (rig.BodyZone.position, rig.BodyHalf.x * 0.8f),
                        (rig.BodyZone.position + Cat.transform.forward * rig.BodyHalf.z * 0.7f, rig.BodyHalf.x * 0.8f),
                        (rig.BodyZone.position - Cat.transform.forward * rig.BodyHalf.z * 0.7f, rig.BodyHalf.x * 0.8f),
                    };
                    foreach (var o in nav.obstacles)
                        if (o.owner == null)   // (고양이 자신·다른 고양이의 움직이는 자리는 물건이 아니다)
                        foreach (var (p, r) in samples)
                        {
                            float d = o.Distance(p) - r;
                            if (d < worst) { worst = d; worstWhat = $"{o.name} at {Cat.transform.position} state {Cat.State}"; }
                        }
                    if (Cat.State == CatState.Idle && t > 1f) break;
                }
            }
            Debug.Log($"[NoPassThrough] worst overlap {worst * 1000f:F0} mm {worstWhat}");
            Assert.Greater(worst, -0.03f, "the cat's body never sinks more than 3 cm into an item or plant: " + worstWhat);
        }

        [UnityTest]
        public IEnumerator Groom_PlaysGroomFaceWhileSitting()
        {
            Cat.Needs.SetForTest(1f, 1f);
            Cat.NoteUserActivity();
            Cat.ForceState(CatState.Groom);
            yield return WaitUntil(() => Cat.Rig.ActionClip == "GroomFace", 5f);
            Assert.AreEqual("GroomFace", Cat.Rig.ActionClip);
            Assert.AreEqual(Posture.Sit, Cat.Rig.Current);
        }

        [UnityTest]
        public IEnumerator Zoomies_RunsAtGallopSpeed()
        {
            Cat.Needs.SetForTest(1f, 1f);
            Cat.NoteUserActivity();
            Cat.ForceState(CatState.Zoomies);
            float maxSpeed = 0f;
            yield return WaitUntil(() => { maxSpeed = Mathf.Max(maxSpeed, Cat.Speed); return false; }, 2.5f);
            Assert.Greater(maxSpeed, GameConfig.TrotSpeed, "gallop (우다다)");
        }

        [UnityTest]
        public IEnumerator CameraDragAndPinch()
        {
            Vector2 empty = new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.04f);
            float yaw0 = game.IslandCam.Yaw;
            for (int i = 0; i < 20; i++) { fingers.Press(empty + new Vector2(i * UnityEngine.Screen.height * 0.01f, 0f)); yield return null; }
            fingers.Release();
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(Mathf.Abs(game.IslandCam.Yaw - yaw0), 5f, "drag rotates view");

            int zoom0 = game.IslandCam.zoomLevel;
            Vector2 c = new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.5f);
            for (int i = 0; i < 12; i++)
            {
                float d = UnityEngine.Screen.height * (0.15f - i * 0.01f);
                fingers.Press(c + Vector2.left * d, 0);
                fingers.Press(c + Vector2.right * d, 1);
                yield return null;
            }
            fingers.Release(0); fingers.Release(1);
            yield return null;
            Assert.AreNotEqual(zoom0, game.IslandCam.zoomLevel, "pinch changes zoom");
        }

        [UnityTest]
        public IEnumerator Idle_CatInvitesPetting_WithHandBubble()
        {
            Cat.Needs.SetForTest(1f, 1f);
            Time.timeScale = 3f;
            Cat.ForceState(CatState.Invite);
            bool hand = false;
            yield return WaitUntil(() => { hand |= Cat.Bubble.Current == Icon.Hand; return hand; }, 25f);
            Assert.IsTrue(hand, "cat walks up, sits and asks to be petted");
        }
    }

    /// <summary>화면 확인용 스크린샷. unity/Shots 에 저장한다.</summary>
    [Category("Shots")]
    public class ScreenshotTests : SceneFixture
    {
        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Shots"));

        void Capture(string name)
        {
            Directory.CreateDirectory(Dir);
            var cam = Cam;
            var rt = new RenderTexture(1179, 2556, 24) { antiAliasing = 4 };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render(); // 첫 렌더는 재질 데이터 업로드용으로 버린다
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prev;
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            rt.Release();
            Debug.Log("[Shots] " + name);
        }

        IEnumerator Fast(Func<bool> cond, float max, float scale = 3f)
        {
            Time.timeScale = scale;
            yield return WaitUntil(cond, max);
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator CaptureKeyMoments()
        {
            Cat.Needs.SetForTest(1f, 1f);
            Cat.NoteUserActivity();
            yield return new WaitForSeconds(1.2f);
            game.IslandCam.zoomLevel = 0; game.IslandCam.SnapNow();
            yield return new WaitForSeconds(0.3f);
            Capture("01_overview");

            game.IslandCam.zoomLevel = 1; game.IslandCam.SnapNow();
            Cat.transform.position = new Vector3(0f, 0f, -0.6f);
            Cat.transform.rotation = Quaternion.Euler(0f, 150f, 0f);
            Cat.ForceState(CatState.Idle);
            yield return new WaitForSeconds(0.8f);
            Capture("02_close_idle");

            Cat.transform.rotation = Quaternion.Euler(0f, 110f, 0f);
            yield return Stroke(() => BackPoint, 3f);
            fingers.Press(Screen(BackPoint));
            yield return null;
            Capture("03_petting_sit_purr");
            fingers.Release();

            yield return Stroke(() => BackPoint, 12f, () => Cat.State == CatState.BellyUp);
            yield return Fast(() => Cat.Rig.Current == Posture.Flop, 4f);
            yield return Stroke(() => BellyPoint, 1.2f);
            fingers.Press(Screen(BellyPoint));
            yield return new WaitForSeconds(0.2f);
            Capture("04_flop_belly");
            fingers.Release();

            Cat.ForceState(CatState.Nip);
            yield return Fast(() => Cat.Rig.ActionClip == "PawBat", 4f);
            yield return new WaitForSeconds(0.45f);
            Capture("05_nip_pawbat");

            yield return new WaitForSeconds(1.5f);
            Cat.Needs.SetForTest(0.1f, 0.9f);
            Cat.ForceState(CatState.WaitAtBowl);
            yield return Fast(() => Cat.State == CatState.WaitAtBowl && Cat.Rig.Current == Posture.Sit && Cat.StateTime > 1f, 20f);
            Capture("06_hungry_wait");

            game.Bowl.Fill();
            Cat.OnBowlFilled();
            yield return Fast(() => Cat.Rig.ActionClip == "Drink", 15f);
            yield return new WaitForSeconds(1.0f);
            Capture("07_eating");

            yield return Fast(() => Cat.State == CatState.LieDown && Cat.Rig.Current == Posture.Loaf, 30f);
            yield return new WaitForSeconds(0.5f);
            Capture("08_loaf_cushion");
            yield return Fast(() => Cat.State == CatState.Sleep && Cat.StateTime > 2f, 15f);
            Capture("09_sleeping");

            Cat.Needs.SetForTest(1f, 1f);
            Cat.transform.position = new Vector3(0f, 0f, -0.4f);
            Cat.ForceOnTower(false);
            Cat.ForceState(CatState.Idle);
            yield return Fast(() => Cat.Rig.Current == Posture.Stand && !Cat.Rig.Busy, 10f);
            Cat.ForceState(CatState.Invite);
            yield return Fast(() => Cat.Bubble.Current == Icon.Hand, 20f);
            yield return new WaitForSeconds(0.5f);
            Capture("10_invite");

            Cat.ForceState(CatState.GoToTower);
            yield return Fast(() => Cat.State == CatState.JumpUp && Cat.transform.position.y > 0.25f, 30f);
            Capture("11_jump_up");
            yield return Fast(() => Cat.State == CatState.OnTower && Cat.Rig.Current != Posture.Stand && !Cat.Rig.Busy, 15f);
            yield return new WaitForSeconds(0.5f);
            Capture("12_on_tower");

            Cat.OnTapGround(new Vector3(0f, 0f, 0.5f));
            yield return Fast(() => Cat.State == CatState.Idle, 20f);
            Cat.ForceState(CatState.Groom);
            yield return Fast(() => Cat.Rig.ActionClip == "GroomFace", 8f);
            yield return new WaitForSeconds(1.2f);
            Capture("13_groom");

            Cat.ForceState(CatState.Zoomies);
            yield return WaitUntil(() => Cat.Speed > 2f, 4f);
            Capture("14_zoomies");

            Cat.ForceState(CatState.Idle);
            yield return Fast(() => Cat.Rig.CanMove, 6f);
            Cat.ForceState(CatState.Stretch);
            yield return WaitUntil(() => Cat.Rig.ActionClip == "Stretch" && Cat.Rig.ActionProgress > 0.45f, 6f);
            Capture("15_stretch");

            game.IslandCam.zoomLevel = 0; game.IslandCam.SnapNow();
            yield return new WaitForSeconds(0.3f);
            Capture("16_overview_late");
            Assert.Pass("shots saved to " + Dir);
        }
    }
}

namespace CatIsland.Tests
{
    using System.Collections;
    using System.IO;
    using CatIsland.UI;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// 화면 점검: 모든 창을 아이폰 SE · 기본 · Pro Max 크기로 찍어 unity/Shots/ui 에 남긴다 (사람이 보고 겹침·잘림 확인).
    /// 자동 확인: 글자가 상자를 넘치지 않는지, 버튼이 44 pt 이상인지, 안전 영역 밖에 버튼이 없는지.
    /// </summary>
    [Category("Shots")]
    public class UIShotTests : SceneFixture
    {
        static readonly (string name, int w, int h, Rect safe)[] Devices =
        {
            ("se", 750, 1334, new Rect(0, 0, 1, 1)),
            ("iphone16", 1179, 2556, new Rect(0, 34f / 852, 1, 1 - (59f + 34f) / 852)),
            ("promax", 1320, 2868, new Rect(0, 34f / 956, 1, 1 - (62f + 34f) / 956)),
        };

        IEnumerator Shot(string name)
        {
            yield return null; yield return null;
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Shots/ui")); Directory.CreateDirectory(dir);
            var canvas = game.UI.GetComponent<Canvas>(); var cam = Cam;
            foreach (var d in Devices)
            {
                SafeArea.NormOverride = d.safe;
                var rt = new RenderTexture(d.w, d.h, 24) { antiAliasing = 4 };
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f;
                cam.targetTexture = rt;
                foreach (var sa in game.UI.GetComponentsInChildren<SafeArea>()) sa.Apply();
                for (int k = 0; k < 3; k++)   // (글자 크기가 정해진 뒤 배치를 다시: 해상도가 바뀐 첫 그림은 배치 전 값)
                {
                    Canvas.ForceUpdateCanvases();
                    foreach (var lg in game.UI.GetComponentsInChildren<UnityEngine.UI.LayoutGroup>()) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)lg.transform);
                    Canvas.ForceUpdateCanvases(); cam.Render();
                }
                CheckLayout(name + "/" + d.name);
                RenderTexture.active = rt; var tex = new Texture2D(d.w, d.h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, d.w, d.h), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, $"{name}_{d.name}.png"), tex.EncodeToPNG());
                Object.Destroy(tex); cam.targetTexture = null; rt.Release();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; SafeArea.NormOverride = default;
        }

        void CheckLayout(string where)
        {
            float scale = game.UI.GetComponent<Canvas>().scaleFactor;
            foreach (var t in game.UI.GetComponentsInChildren<UnityEngine.UI.Text>())
            {
                if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text)) continue;
                var gen = t.cachedTextGenerator; if (gen.characterCount == 0) continue;
                Assert.GreaterOrEqual(gen.characterCountVisible, t.text.Replace("\n", "").Length - 1, $"{where}: text cut \"{t.text}\"");
            }
            foreach (var b in game.UI.GetComponentsInChildren<UnityEngine.UI.Button>())
            {
                if (!b.isActiveAndEnabled || b.name == "Shade" || b.name == "Sheet") continue;
                var r = ((RectTransform)b.transform).rect;
                Assert.GreaterOrEqual(Mathf.Min(r.width, r.height), 43.5f, $"{where}: button too small {b.name} {r.size}");
            }
        }

        [UnityTest]
        public IEnumerator AllScreens()
        {
            var g = game.Logic; g.AddCat("korean_shorthair", "나비", CatIsland.Game.Personality.Playful); g.AddCoins(5000); g.AddJelly(200);
            g.S.idleBank = 320; g.S.idleHours = 3;
            game.UI.Refresh();
            yield return Shot("hud");
            game.UI.Open(b => { var m = typeof(GameUI).GetMethod("BuildIdle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); return (string)m.Invoke(game.UI, new object[] { b }); });
            yield return Shot("idle");
            foreach (var sheet in new[] { "BuildShop", "BuildJellyShop", "BuildBag", "BuildTasks", "BuildCats", "BuildSettings", "BuildGuest", "BuildOdds" })
            {
                var m = typeof(GameUI).GetMethod(sheet, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                game.UI.Open(b => (string)m.Invoke(game.UI, new object[] { b }));
                yield return Shot(sheet.Substring(5).ToLower());
            }
            CatMaker.Open(game.UI); yield return Shot("catmaker");
            StarLandUI.Open(game.UI); yield return Shot("starland");
            game.UI.CloseAll();
        }
    }
}

namespace CatIsland.Tests
{
    using System.Collections;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>고양이 여러 마리: 저장대로 섬에 서고(품종마다), 돌아다녀도 서로·물건에 박히지 않는다. 산책 가면 섬에서 사라진다.</summary>
    public class MultiCatTests : SceneFixture
    {
        [UnityTest]
        public IEnumerator ThreeBreeds_RoamWithoutOverlap()
        {
            var g = game.Logic; g.S.catSlots = 4;
            var a = g.AddCat("korean_shorthair", "나비", CatIsland.Game.Personality.Playful);
            var b = g.AddCat("persian", "보리", CatIsland.Game.Personality.Easygoing);
            var c = g.AddCat("munchkin", "콩", CatIsland.Game.Personality.Foodie);
            game.SyncCats();
            yield return null;
            var cats = Object.FindObjectsByType<CatBrain>(FindObjectsSortMode.None).Where(x => x.isActiveAndEnabled).ToList();
            Assert.AreEqual(3, cats.Count, "one cat on the island per cat in the save");
            Assert.IsTrue(cats.Any(x => x.Rig.breed == "persian") && cats.Any(x => x.Rig.breed == "munchkin"));
            Time.timeScale = 3f;
            float worstItem = 0f, closest = 9f; string what = "";
            for (float t = 0; t < 40f; t += Time.deltaTime)
            {
                yield return null;
                foreach (var k in cats)
                {
                    foreach (var o in game.Nav.obstacles.Where(o => o.owner == null))
                    {
                        float d = o.Distance(k.Rig.BodyZone.position) - k.Rig.BodyHalf.x * .8f;
                        if (d < worstItem) { worstItem = d; what = $"{k.name} in {o.name}"; }
                    }
                    foreach (var k2 in cats) if (k2 != k && !k.OnTower && !k2.OnTower) closest = Mathf.Min(closest, Vector3.Distance(k.Rig.BodyZone.position, k2.Rig.BodyZone.position));
                }
            }
            Time.timeScale = 1f;
            Debug.Log($"[MultiCat] worst item overlap {worstItem * 1000:F0} mm ({what}), closest cats {closest:F2} m");
            Assert.Greater(worstItem, -0.03f, what);
            Assert.Greater(closest, 0.25f, "cats never stand inside each other");
            // 산책 보내면 섬에서 사라지고, 돌아오면 다시 선다
            g.SendWalk(b.uid, 1); game.SyncCats(); yield return null;
            Assert.AreEqual(2, Object.FindObjectsByType<CatBrain>(FindObjectsSortMode.None).Count(x => x.isActiveAndEnabled));
        }
    }
}
