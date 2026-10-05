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

        protected Vector3 BackPoint => Cat.Rig.BodyZone.TransformPoint(new Vector3(0f, 0.17f, 0.02f));
        protected Vector3 BellyPoint => Cat.Rig.BodyZone.TransformPoint(new Vector3(0f, -0.18f, 0.02f));

        /// <summary>부드럽게 문지르기: 목표 지점을 중심으로 좌우로 왕복.</summary>
        protected IEnumerator Stroke(Func<Vector3> target, float seconds, Func<bool> until = null, float amp = 0.018f, float hz = 6.5f)
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
    }

    public class CorePlayTests : SceneFixture
    {
        [UnityTest]
        public IEnumerator Boot_BuildsIsland_Cat_Bowl_Cushion()
        {
            Assert.IsNotNull(Cat);
            Assert.IsNotNull(game.Bowl);
            Assert.IsNotNull(game.Cushion);
            Assert.AreEqual(Camera.main, Cam);
            Assert.IsFalse(game.Bowl.HasFood);
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(Cat.Rig.Head.gameObject.activeInHierarchy);
        }

        [UnityTest]
        public IEnumerator GentleStrokesOnBack_Purr_EyesClose_Hearts()
        {
            Cat.Needs.SetForTest(1f, 1f);
            Cat.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // 옆모습: 등이 잘 보이게
            int maxFx = 0, backHits = 0, frames = 0;
            int affection0 = Mathf.RoundToInt(Cat.Affection.Points);
            yield return Stroke(() => BackPoint, 3f, () =>
            {
                maxFx = Mathf.Max(maxFx, FxPool.Instance.ActiveCount);
                frames++;
                if (game.Router.LastZone != PetZone.None) backHits++;
                return false;
            });
            Assert.Greater(backHits, frames / 2, "strokes should land on the cat");
            Assert.IsTrue(Cat.Pet.Purring, "purring");
            Assert.Greater(Cat.Pet.Pleasure, 0.5f);
            Assert.Less(Cat.Rig.eyeOpen, 0.7f, "happy eyes closing");
            Assert.Greater(game.Audio.PurrVolume, 0f, "purr audio");
            Assert.Greater(maxFx, 0, "hearts floated up");
            Assert.Greater(Cat.Affection.Points, affection0 + 5f);
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
        public IEnumerator LongPetting_BellyUp_ThenBellyTrap_GentleNip()
        {
            Cat.Needs.SetForTest(1f, 1f);
            Time.timeScale = 2f;
            Cat.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            bool nipped = false;
            Cat.Nipped += () => nipped = true;

            yield return Stroke(() => BackPoint, 12f, () => Cat.State == CatState.BellyUp);
            Assert.AreEqual(CatState.BellyUp, Cat.State, "cat flops over after enough love");

            // 믿음 시간 동안 배는 좋아한다
            int bellyHits = 0, frames = 0;
            yield return Stroke(() => BellyPoint, 2f, () =>
            {
                frames++;
                if (game.Router.LastZone == PetZone.Belly) bellyHits++;
                return nipped;
            });
            Assert.IsFalse(nipped, "no nip while trusting");
            Assert.Greater(bellyHits, frames / 2, "belly is reachable while belly-up");

            // 계속 문지르면 믿음 시간이 끝난 뒤 살짝 깨문다
            yield return Stroke(() => BellyPoint, GameConfig.TrustWindow + 3f, () => nipped);
            Assert.IsTrue(nipped, "belly trap nip");
            Assert.AreEqual(Icon.Exclaim, Cat.Bubble.Current);
            yield return WaitUntil(() => Cat.State != CatState.Nip, 3f);
            Assert.AreNotEqual(CatState.Nip, Cat.State, "nip is brief, cat moves on");
        }

        [UnityTest]
        public IEnumerator HungryCat_FillBowl_Eats_ThenNapsOnCushion()
        {
            Cat.Needs.SetForTest(0.1f, 0.8f);
            yield return WaitUntil(() => Cat.Bubble.Current == Icon.Fish, 4f);
            Assert.AreEqual(Icon.Fish, Cat.Bubble.Current, "hungry bubble");

            Time.timeScale = 3f;
            yield return Tap(Screen(game.Bowl.transform.position + Vector3.up * 0.06f));
            Assert.IsTrue(game.Bowl.HasFood, "tap bowl fills it");

            bool ate = false;
            yield return WaitUntil(() => { ate |= Cat.State == CatState.Eat; return ate; }, 15f);
            Assert.IsTrue(ate, "cat walks to bowl and eats");
            float hungerBefore = Cat.Needs.Hunger;

            bool knead = false;
            yield return WaitUntil(() => { knead |= Cat.State == CatState.Knead; return Cat.State == CatState.Sleep; }, 30f);
            Assert.Greater(Cat.Needs.Hunger, hungerBefore);
            Assert.IsTrue(knead, "kneads the cushion first");
            Assert.AreEqual(CatState.Sleep, Cat.State, "naps on the cushion");
            Vector3 d = Cat.transform.position - game.Cushion.transform.position; d.y = 0f;
            Assert.Less(d.magnitude, 0.3f);
            yield return WaitUntil(() => Cat.StateTime > 2f, 5f);
            Assert.AreEqual(Icon.Sleep, Cat.Bubble.Current);
        }

        [UnityTest]
        public IEnumerator PettingSleepingCat_Purrs_StaysAsleep()
        {
            Cat.Needs.SetForTest(1f, 0.1f);
            Cat.transform.position = game.Cushion.transform.position;
            Cat.ForceState(CatState.Sleep);
            yield return Stroke(() => BackPoint, 2.5f);
            Assert.AreEqual(CatState.Sleep, Cat.State);
            Assert.Greater(Cat.Pet.Pleasure, 0.3f);
        }

        [UnityTest]
        public IEnumerator TapCat_Meows_TapGround_CatComes()
        {
            Cat.Needs.SetForTest(1f, 1f);
            int played = game.Audio.PlayedCount;
            yield return Tap(Screen(Cat.Rig.Head.position));
            Assert.Greater(game.Audio.PlayedCount, played, "meow");

            Vector3 spot = new Vector3(-0.8f, 0f, -0.6f);
            float before = Vector3.Distance(Cat.transform.position, spot);
            yield return Tap(Screen(spot));
            Assert.AreEqual(CatState.Called, Cat.State);
            yield return new WaitForSeconds(1.2f);
            Assert.Less(Vector3.Distance(Cat.transform.position, spot), before - 0.4f);
        }

        [UnityTest]
        public IEnumerator CameraDragAndPinch()
        {
            Vector2 empty = new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.08f);
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
            bool hand = false;
            yield return WaitUntil(() => { hand |= Cat.Bubble.Current == Icon.Hand; return hand; }, 25f);
            Assert.IsTrue(hand, "cat walks up and asks to be petted");
        }
    }

    /// <summary>화면 확인용 스크린샷. Builds 옆 shots 폴더에 저장한다.</summary>
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
            Cat.transform.position = new Vector3(0f, 0f, -0.2f);
            Cat.transform.rotation = Quaternion.Euler(0f, 150f, 0f);
            Cat.ForceState(CatState.Idle);
            yield return new WaitForSeconds(0.8f);
            Capture("02_close_idle");

            Cat.transform.rotation = Quaternion.Euler(0f, 100f, 0f);
            yield return Stroke(() => BackPoint, 2.2f);
            fingers.Press(Screen(BackPoint));
            yield return null;
            Capture("03_petting_purr");
            fingers.Release();

            Cat.transform.rotation = Quaternion.Euler(0f, 100f, 0f);
            yield return Stroke(() => BackPoint, 10f, () => Cat.State == CatState.BellyUp);
            yield return Stroke(() => BellyPoint, 1.2f);
            fingers.Press(Screen(BellyPoint));
            yield return new WaitForSeconds(0.2f);
            Capture("04_belly_up");
            fingers.Release();

            Cat.ForceState(CatState.Nip);
            yield return new WaitForSeconds(0.25f);
            Capture("05_nip");

            yield return new WaitForSeconds(1.5f);
            Cat.Needs.SetForTest(0.1f, 0.9f);
            Cat.ForceState(CatState.WaitAtBowl);
            yield return WaitUntil(() => Cat.State == CatState.WaitAtBowl && Cat.Speed < 0.05f && Cat.StateTime > 2.5f, 8f);
            Capture("06_hungry_wait");

            game.Bowl.Fill();
            Cat.OnBowlFilled();
            yield return WaitUntil(() => Cat.State == CatState.Eat, 6f);
            yield return new WaitForSeconds(0.8f);
            Capture("07_eating");

            Time.timeScale = 3f;
            yield return WaitUntil(() => Cat.State == CatState.Knead, 20f);
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.6f);
            Capture("08_knead");
            yield return WaitUntil(() => Cat.State == CatState.Sleep && Cat.StateTime > 2f, 8f);
            Capture("09_sleeping");

            Cat.Needs.SetForTest(1f, 1f);
            Cat.ForceState(CatState.Invite);
            yield return new WaitForSeconds(2.5f);
            Capture("10_invite");
            game.IslandCam.zoomLevel = 0; game.IslandCam.SnapNow();
            yield return new WaitForSeconds(0.3f);
            Capture("11_overview_late");
            Assert.Pass("shots saved to " + Dir);
        }
    }
}
