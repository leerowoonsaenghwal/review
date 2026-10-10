using NUnit.Framework;
using UnityEngine;

namespace CatIsland.Tests
{
    public class PetLogicTests
    {
        static PetLogic MakeCat() =>
            new PetLogic(ZonePref.Liked, ZonePref.Loved, ZonePref.Loved, ZonePref.Liked, ZonePref.Neutral);

        static PetFrame Run(PetLogic p, PetZone z, float speed, float seconds, System.Action<PetFrame> each = null)
        {
            var last = new PetFrame();
            const float dt = 1f / 60f;
            for (float t = 0; t < seconds; t += dt)
            {
                last = p.Update(z, speed, dt);
                each?.Invoke(last);
            }
            return last;
        }

        [Test]
        public void HoldingStill_IsNotPetting()
        {
            var p = MakeCat();
            Run(p, PetZone.Chin, 0f, 3f);
            Assert.AreEqual(0f, p.Pleasure, 1e-4);
            Assert.IsFalse(p.Purring);
        }

        [Test]
        public void GentleStrokesOnFavoriteSpot_StartPurring()
        {
            var p = MakeCat();
            bool started = false;
            Run(p, PetZone.Chin, GameConfig.StrokeSpeedBest, 1.0f, f => started |= f.PurrStarted);
            Assert.IsTrue(started, "purr should start within 1s of gentle chin strokes");
            Assert.IsTrue(p.Purring);
        }

        [Test]
        public void FavoriteSpot_BeatsNeutralSpot()
        {
            var a = MakeCat(); var b = MakeCat();
            Run(a, PetZone.Chin, GameConfig.StrokeSpeedBest, 0.5f);
            Run(b, PetZone.Butt, GameConfig.StrokeSpeedBest, 0.5f);
            Assert.Greater(a.Pleasure, b.Pleasure * 2f);
        }

        [Test]
        public void RoughStrokes_AreLessPleasant()
        {
            Assert.Greater(PetLogic.StrokeQuality(GameConfig.StrokeSpeedBest), PetLogic.StrokeQuality(GameConfig.StrokeSpeedRough * 1.5f) + 0.4f);
            Assert.AreEqual(0f, PetLogic.StrokeQuality(GameConfig.StrokeSpeedMin * 0.5f));
        }

        [Test]
        public void BellyRub_WithoutTrust_GentleNip_ThenCooldown()
        {
            var p = MakeCat();
            int nips = 0;
            Run(p, PetZone.Belly, GameConfig.StrokeSpeedBest, GameConfig.BellyTolerance + 0.2f, f => { if (f.Nipped) nips++; });
            Assert.AreEqual(1, nips);
            Assert.Greater(p.NipCooldownLeft, 0f);
            // 쿨다운 중에는 다시 깨물지 않는다
            Run(p, PetZone.Belly, GameConfig.StrokeSpeedBest, GameConfig.NipCooldown * 0.5f, f => { if (f.Nipped) nips++; });
            Assert.AreEqual(1, nips);
        }

        [Test]
        public void MaxPleasure_LeadsToBellyUp_AndBellyIsLovedDuringTrust()
        {
            var p = MakeCat();
            bool bellyUp = false;
            Run(p, PetZone.Chin, GameConfig.StrokeSpeedBest, 6f, f => bellyUp |= f.BellyUp);
            Assert.IsTrue(bellyUp);
            Assert.IsTrue(p.Trusting);
            Assert.AreEqual(ZonePref.Loved, p.PrefFor(PetZone.Belly));
            int nips = 0;
            Run(p, PetZone.Belly, GameConfig.StrokeSpeedBest, 2f, f => { if (f.Nipped) nips++; });
            Assert.AreEqual(0, nips, "no nip while trusting");
        }

        [Test]
        public void BellyTrap_KeepRubbingAfterTrustEnds_Nips()
        {
            var p = MakeCat();
            Run(p, PetZone.Chin, GameConfig.StrokeSpeedBest, 6f);
            Assert.IsTrue(p.Trusting);
            int nips = 0;
            bool nippedWhileTrusting = false;
            Run(p, PetZone.Belly, GameConfig.StrokeSpeedBest, GameConfig.TrustWindow + GameConfig.BellyTolerance + 0.5f, f =>
            {
                if (!f.Nipped) return;
                nips++;
                if (p.Trusting) nippedWhileTrusting = true;
            });
            Assert.GreaterOrEqual(nips, 1, "keeps rubbing after trust ends -> gentle nip");
            Assert.IsFalse(nippedWhileTrusting);
        }

        [Test]
        public void PleasureFades_WhenHandLeaves_AndPurrStops()
        {
            var p = MakeCat();
            Run(p, PetZone.Chin, GameConfig.StrokeSpeedBest, 2f);
            bool stopped = false;
            Run(p, PetZone.None, 0f, 6f, f => stopped |= f.PurrStopped);
            Assert.IsTrue(stopped);
            Assert.AreEqual(0f, p.Pleasure, 1e-4);
        }

        [Test]
        public void OneMinuteOfPetting_GainsSeveralLevels_ButNotAll()
        {
            var p = MakeCat();
            var a = new Affection();
            Run(p, PetZone.Cheek, GameConfig.StrokeSpeedBest, 60f, f => a.Add(f.AffectionGained));
            // 시제품은 체험 시간 안에 반응이 보이도록 압축했다. 1분에 몇 단계 오르되 10단계를 채우지는 않는다.
            // (정식 게임의 목표는 마리당 약 6주. 시험 출시 전에 다시 맞춘다)
            Assert.GreaterOrEqual(a.Level, 3);
            Assert.LessOrEqual(a.Level, 7);
        }
    }

    public class NeedsTests
    {
        [Test]
        public void Needs_NeverGoBelowZero_NoPunishment()
        {
            var n = new CatNeeds(0.1f, 0.1f);
            for (int i = 0; i < 10000; i++) n.Tick(0.1f, false, false);
            Assert.AreEqual(0f, n.Hunger);
            Assert.AreEqual(0f, n.Energy);
            Assert.IsTrue(n.IsHungry && n.IsSleepy);
        }

        [Test]
        public void Sleeping_DoesNotDrainHunger_AndRestoresEnergy()
        {
            var n = new CatNeeds(0.5f, 0.2f);
            for (int i = 0; i < 100; i++) n.Tick(0.1f, false, true);
            Assert.AreEqual(0.5f, n.Hunger, 1e-4);
            Assert.Greater(n.Energy, 0.6f);
        }

        [Test]
        public void StartsSlightlyHungry_SoTheFeedingLoopShowsUpEarly()
        {
            var n = new CatNeeds();
            float t = 0f;
            while (!n.IsHungry && t < 300f) { n.Tick(0.1f, false, false); t += 0.1f; }
            Assert.Less(t, 30f, "first hunger bubble within 30s of a session");
        }
    }

    public class AffectionTests
    {
        [Test]
        public void Levels_Increase_AndCapAtTen()
        {
            var a = new Affection();
            Assert.AreEqual(1, a.Level);
            Assert.IsTrue(a.Add(25f));
            Assert.AreEqual(2, a.Level);
            a.Add(100000f);
            Assert.AreEqual(10, a.Level);
            Assert.AreEqual(1f, a.Progress);
        }
    }

    public class GeneratedAssetTests
    {
        [Test]
        public void Meshes_AreValid()
        {
            foreach (var m in new[] { MeshFactory.Sphere(), MeshFactory.Capsule(0.3f), MeshFactory.RoundCone(), MeshFactory.RoundedCylinder(0.2f, 0.25f), MeshFactory.Bowl(), MeshFactory.IslandTop(), MeshFactory.IslandSoil() })
            {
                Assert.Greater(m.triangles.Length, 0, m.name);
                foreach (var n in m.normals) Assert.AreEqual(1f, n.magnitude, 0.01f, m.name);
            }
        }

        [Test]
        public void Sphere_NormalsPointOutward()
        {
            var m = MeshFactory.Sphere();
            var v = m.vertices; var n = m.normals;
            for (int i = 0; i < v.Length; i++)
                if (v[i].sqrMagnitude > 0.01f) Assert.Greater(Vector3.Dot(v[i].normalized, n[i]), 0.9f);
        }

        [Test]
        public void Sphere_TrianglesFaceOutward()
        {
            // 시계 방향 = 앞면. 삼각형의 면 법선이 바깥을 향해야 보인다
            var m = MeshFactory.Sphere();
            var v = m.vertices; var t = m.triangles;
            int outward = 0, total = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 fn = Vector3.Cross(b - a, c - a);
                if (fn.sqrMagnitude < 1e-12f) continue;
                total++;
                if (Vector3.Dot(fn, (a + b + c) / 3f) > 0f) outward++;
            }
            Assert.AreEqual(total, outward);
        }

        [Test]
        public void Icons_HaveTransparentCorners_AndOpaqueCenters()
        {
            foreach (Icon icon in System.Enum.GetValues(typeof(Icon)))
            {
                var tex = IconPainter.Get(icon);
                Assert.Less(tex.GetPixel(1, 1).a, 0.05f, icon + " corner");
                int opaque = 0;
                foreach (var c in tex.GetPixels()) if (c.a > 0.9f) opaque++;
                Assert.Greater(opaque, 300, icon + " should have visible pixels");
            }
        }

        /// <summary>화질 자동 조절: 느리면 3초 만에 한 단계 낮추고, 넉넉함이 15초 이어져야 올리며, 올리자마자 떨어지면 1분 동안 그 단계로 다시 올리지 않는다.</summary>
        [Test]
        public void QualityGovernor_DropsFast_RisesSlow_NoFlapping()
        {
            var q = new CatIsland.QualityGovernor(0);
            void Run(float fps, float seconds) { for (float t = 0; t < seconds; t += 1f / fps) q.Tick(1f / fps); }
            Run(10f, 4.9f); Assert.AreEqual(0, q.Level, "켤 때 처음 5초는 판단하지 않는다");
            Run(60f, 9f); Assert.AreEqual(0, q.Level, "넉넉하면 그대로");
            Run(40f, 3.1f); Assert.AreEqual(1, q.Level, "느리면 3초 만에 한 단계 낮춤");
            Run(40f, 9.3f); Assert.AreEqual(3, q.Level, "계속 느리면 끝까지"); Run(40f, 6f); Assert.AreEqual(3, q.Level, "가장 낮은 단계에서 멈춤");
            Run(60f, 12f); Assert.AreEqual(3, q.Level, "올리는 것은 천천히");
            Run(60f, 6f); Assert.AreEqual(2, q.Level, "넉넉함이 15초 이어지면 한 단계 올림");
            Run(40f, 3.1f); Assert.AreEqual(3, q.Level, "올리자마자 느려지면 다시 낮춤");
            Run(60f, 30f); Assert.AreEqual(3, q.Level, "그 단계는 1분 동안 다시 올리지 않는다");
            Run(60f, 40f); Assert.AreEqual(2, q.Level, "1분이 지나면 다시 올려 본다");
        }
    }
}
