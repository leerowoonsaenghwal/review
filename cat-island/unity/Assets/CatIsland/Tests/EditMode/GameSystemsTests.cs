using System;
using System.Linq;
using CatIsland.Game;
using NUnit.Framework;

namespace CatIsland.Tests
{
    /// <summary>게임 규칙 하나하나 (Scripts/Game). 가상 시계로 시간을 넘기며 확인한다.</summary>
    public class GameSystemsTests
    {
        [Test]
        public void Weather_ByDateAndSeason_SameForEveryone()
        {
            var d0 = new DateTime(2027, 1, 1, 9, 0, 0);
            int snow = 0, rainSummer = 0, rainOther = 0, clear = 0, n = 0, snowOutOfWinter = 0;
            for (int day = 0; day < 365 * 2; day++) for (int half = 0; half < 2; half++)
                {
                    var t = d0.AddDays(day).AddHours(half * 6); var w = Weather.At(t);
                    Assert.AreEqual(w, Weather.At(t.AddMinutes(90)), "같은 오전·오후 안에서는 바뀌지 않는다");
                    bool winter = t.Month == 12 || t.Month <= 2; n++;
                    if (w == WeatherKind.Snow) { if (winter) snow++; else snowOutOfWinter++; }
                    if (w == WeatherKind.Rain) { if (t.Month == 6 || t.Month == 7) rainSummer++; else rainOther++; }
                    if (w == WeatherKind.Clear) clear++;
                }
            Assert.AreEqual(0, snowOutOfWinter, "눈은 겨울에만");
            Assert.Greater(snow, 40, "겨울엔 눈 오는 때가 있다");
            Assert.Greater(rainSummer / 244f, rainOther / (n - 244f - 360f), "장마철에 비가 더 잦다");
            Assert.Greater(clear, n / 2, "맑은 때가 가장 많다");
        }

        static readonly DateTime Start = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);   // 한국 아침 9시
        FakeClock clock; MemoryFiles files; MemoryCloud cloud; FakeAds ads; FakeStore store; FakeNotifier notes; FakeGameCenter gc; CatIsland.Game.Game g;

        [SetUp]
        public void SetUp()
        {
            clock = new FakeClock(Start); files = new MemoryFiles(); cloud = new MemoryCloud(); ads = new FakeAds(); store = new FakeStore(); notes = new FakeNotifier(); gc = new FakeGameCenter();
            g = New(); g.LoadOrNew();
        }
        CatIsland.Game.Game New() => new CatIsland.Game.Game(clock, files, cloud, ads, store, gc, notes);
        CatData FirstCat() => g.AddCat("korean_shorthair", "나비", Personality.Playful);

        [Test]
        public void Catalog_HasSixtyUniqueItems_33Breeds_AndSaneData()
        {
            Assert.AreEqual(60, Catalog.Items.Length);
            Assert.AreEqual(Catalog.Items.Length, Catalog.Items.Select(i => i.id).Distinct().Count());
            Assert.AreEqual(33, Catalog.Breeds.Length);
            Assert.IsTrue(Catalog.Items.All(i => i.price > 0 && !string.IsNullOrEmpty(i.ko)));
            foreach (var r in Catalog.Recipes) { Assert.NotNull(Catalog.Item(r.item), r.id); Assert.IsTrue(r.needs.All(m => Catalog.Materials.Contains(m)), r.id); }
            foreach (var p in Catalog.Products) foreach (var it in p.items) Assert.IsTrue(Catalog.Item(it) != null || Catalog.StarItems.Any(s => s.id == it), it);
            Assert.AreEqual(1.0, Catalog.FreeBoxOdds.Sum(o => o.p), 1e-9, "뽑기 확률 합 100 %");
            // 결제 상품은 젤리 묶음, 계절 세트, 별나라 꾸미기뿐 (광고 제거·월간 패스 없음)
            Assert.IsTrue(Catalog.Products.All(p => p.kind == "jelly" || p.kind == "season" || p.kind == "star"));
            Assert.IsFalse(Catalog.Products.Any(p => p.id.Contains("noads") || p.id.Contains("pass")));
        }

        [Test]
        public void NewGame_StartsWithBowlsPlaced_AndNoNegativeMoney()
        {
            Assert.IsTrue(g.S.placed.Any(p => p.item == "food_bowl"));
            Assert.IsTrue(g.S.placed.Any(p => p.item == "water_bowl"));
            Assert.GreaterOrEqual(g.S.coins, 0); Assert.GreaterOrEqual(g.S.jelly, 0);
            Assert.IsFalse(g.SpendCoins(g.S.coins + 1)); Assert.IsFalse(g.SpendJelly(-5));
        }

        [Test]
        public void Idle_AccruesUpToEightHours_AndCatsNeverSuffer()
        {
            var c = FirstCat();
            clock.Advance(TimeSpan.FromHours(3)); g.Resume();
            long three = g.S.idleBank; Assert.Greater(three, 0);
            clock.Advance(TimeSpan.FromDays(5)); g.Resume();
            Assert.LessOrEqual(g.S.idleHours, Catalog.IdleCapHours + 1e-9, "8시간 분량까지만");
            Assert.LessOrEqual(g.S.idleBank, g.IdleCap() + 1); Assert.IsTrue(g.IdleFull);
            Assert.AreEqual(0f, c.hunger, 1e-6); Assert.AreEqual("home", c.status, "며칠 안 와도 떠나지 않는다");
            int got = g.CollectIdle(); Assert.Greater(got, 0); Assert.AreEqual(0, g.S.idleBank);
        }

        [Test]
        public void ClockRollback_GivesNothing_AndDoesNotBreak()
        {
            FirstCat(); clock.Advance(TimeSpan.FromHours(2)); g.Resume(); g.CollectIdle();
            clock.Advance(TimeSpan.FromDays(-2)); g.Resume();            // (기기 시계를 이틀 전으로)
            Assert.AreEqual(0, g.S.idleBank);
            clock.Advance(TimeSpan.FromDays(1)); g.Resume();             // (아직 마지막 본 시각보다 이전)
            Assert.AreEqual(0, g.S.idleBank, "되돌린 시간은 보상이 없다");
            Assert.GreaterOrEqual(g.S.clockRollbacks, 1);
            clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromHours(2)); g.Resume();   // (다시 앞으로: 정상)
            Assert.Greater(g.S.idleBank, 0);
        }

        [Test]
        public void Save_Load_Corrupt_Recovers_FromBackup_AndCloudNewerWins()
        {
            FirstCat(); g.AddCoins(1234); g.Save(); g.AddCoins(1); g.Save();
            int coins = g.S.coins;
            files.files[CatIsland.Game.Game.SaveName] = "{ broken json";                // (쓰다 꺼져 깨진 저장)
            cloud.data = null;
            var h = New(); h.LoadOrNew();
            Assert.AreEqual(coins - 1, h.S.coins, "한 단계 전 저장(백업)으로");
            Assert.AreEqual(1, h.S.cats.Count);
            // 다른 기기에서 더 많이 진행한 iCloud 저장이 있으면 그쪽
            var other = New(); other.LoadOrNew(); other.AddCoins(99999); other.Save(); other.Save(); other.Save(); other.Save();
            string cloudJson = cloud.data;
            files.files.Clear(); files.files[CatIsland.Game.Game.SaveName] = UnityEngine.JsonUtility.ToJson(h.S);
            cloud.data = cloudJson;
            var k = New(); k.LoadOrNew();
            Assert.GreaterOrEqual(k.S.coins, 99999);
        }

        [Test]
        public void Grid_KeepsUseSpotsFree_AndBounds()
        {
            g.AddCoins(5000); Assert.IsTrue(g.Buy("cushion")); Assert.IsTrue(g.Buy("cat_tower_1"));
            Assert.IsFalse(g.CanPlace("cushion", Zone.Indoor, 11, 11, 0), "격자 밖");
            Assert.IsTrue(g.Place("cushion", Zone.Indoor, 2, 2, 0));
            Assert.IsFalse(g.CanPlace("cat_tower_1", Zone.Indoor, 2, 2, 0), "겹침");
            Assert.IsFalse(g.CanPlace("cat_tower_1", Zone.Indoor, 2, 0, 0), "방석 앞(쓰는 자리)을 막음");
            Assert.IsTrue(g.Place("cat_tower_1", Zone.Indoor, 5, 2, 0));
            Assert.IsFalse(g.CanPlace("bench", Zone.Yard, 1, 1, 0), "마당은 아직 닫힘");
        }

        [Test]
        public void Ads_Rewarded_Success_Fail_Cancel_AndDailyCap()
        {
            FirstCat(); clock.Advance(TimeSpan.FromHours(4)); g.Resume();
            ads.next = AdResult.Cancelled; long bank = g.S.idleBank; bool? ok = null; int got = 0;
            g.CollectIdleDoubled((o, c) => { ok = o; got = c; });
            Assert.IsFalse(ok.Value); Assert.AreEqual(bank, got, "취소해도 기본 보상은 받는다");
            Assert.AreEqual(0, g.AdsToday(Catalog.AdSpot.IdleDouble), "본 횟수로 치지 않음");
            ads.next = AdResult.Rewarded;
            for (int i = 0; i < 5; i++) g.FreeFoodAd(_ => { });
            Assert.AreEqual(Catalog.AdDailyCap(Catalog.AdSpot.FreeFood), g.AdsToday(Catalog.AdSpot.FreeFood));
            ads.next = AdResult.Failed; int coins = g.S.coins; g.GiftAd((c, j) => { }); Assert.AreEqual(coins, g.S.coins);
            ads.ready = false; Assert.IsFalse(g.AdAvailable(Catalog.AdSpot.Gift));
        }

        [Test]
        public void Interstitial_Rules()
        {
            Assert.IsFalse(g.ShouldShowInterstitial(false), "설치 3일 동안 없음");
            clock.Advance(TimeSpan.FromDays(4)); g.Resume();
            Assert.IsTrue(g.ShouldShowInterstitial(false));
            Assert.IsFalse(g.ShouldShowInterstitial(true), "별나라에는 광고 없음");
            g.MaybeInterstitial(false); Assert.IsFalse(g.ShouldShowInterstitial(false), "4분 간격");
            for (int i = 0; i < 10; i++) { clock.Advance(TimeSpan.FromMinutes(5)); g.Tick(); g.MaybeInterstitial(false); }
            Assert.LessOrEqual(g.S.interstitialsToday, Catalog.InterstitialDailyCap);
            g.S.payer = true; clock.Advance(TimeSpan.FromDays(1)); g.Resume(); Assert.IsFalse(g.ShouldShowInterstitial(false), "결제자에게 없음");
        }

        [Test]
        public void Purchase_Success_Fail_Cancel_AndCrashRestore_NoDoubleGrant()
        {
            int j = g.S.jelly; PurchaseResult? r = null;
            g.Purchase("com.nolgoseom.jelly_60", x => r = x); Assert.AreEqual(PurchaseResult.Success, r); Assert.AreEqual(j + 60, g.S.jelly);
            store.next = PurchaseResult.Failed; g.Purchase("com.nolgoseom.jelly_60", x => r = x); Assert.AreEqual(j + 60, g.S.jelly);
            store.next = PurchaseResult.Cancelled; g.Purchase("com.nolgoseom.jelly_60", x => r = x); Assert.AreEqual(PurchaseResult.Cancelled, r); Assert.AreEqual(j + 60, g.S.jelly);
            // 결제 직후 앱이 꺼짐 → 다음 실행에 한 번만 지급
            store.next = PurchaseResult.Success; store.crashBeforeFinish = true; g.Purchase("com.nolgoseom.jelly_330", x => r = x);
            Assert.AreEqual(j + 60, g.S.jelly);
            store.crashBeforeFinish = false;
            var h = New(); h.LoadOrNew(); h.RestorePending(); h.RestorePending();
            Assert.AreEqual(j + 60 + 330, h.S.jelly); Assert.AreEqual(0, store.Unfinished().Count());
            // 세트는 한 번만
            h.Purchase("com.nolgoseom.season_winter", x => r = x); h.Purchase("com.nolgoseom.season_winter", x => r = x);
            Assert.AreEqual(PurchaseResult.Failed, r);
        }

        [Test]
        public void Notifications_AtMostTwoPerDay_NotAtNight_KindWords()
        {
            g.S.catSlots = 4;
            var a = FirstCat(); var b = g.AddCat("persian", "보리", Personality.Easygoing); var c = g.AddCat("bengal", "콩", Personality.Foodie);
            clock.UtcNow = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);   // 한국 밤 9시
            g.Resume();
            g.SendWalk(a.uid, 1); g.SendWalk(b.uid, 2); g.SendWalk(c.uid, 4);
            g.ScheduleNotifications();
            foreach (var n in notes.scheduled)
            {
                var local = n.utc + clock.LocalOffset; Assert.IsTrue(local.Hour >= 8 && local.Hour < 22, n.id + " " + local);
                Assert.IsFalse(n.body.Contains("!") || n.body.Contains("빨리") || n.body.Contains("안 하면"), n.body);
            }
            Assert.IsTrue(notes.scheduled.GroupBy(n => TimeUtil.DayKey(n.utc, clock.LocalOffset)).All(x => x.Count() <= 2));
            Assert.IsTrue(notes.scheduled.Any(n => n.body.Contains("나비가 선물을")), "받침 없는 이름: 가");
        }

        [Test]
        public void Guest_TreatThreeTimes_ThenAdopt()
        {
            FirstCat(); g.S.catSlots = 2;
            clock.UtcNow = new DateTime(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc); g.Resume();   // 낮 12시
            string breed = g.S.guestBreed;
            for (int day = 0; day < 3; day++)
            {
                g.S.guestBreed = breed;                 // (같은 품종이 세 번 왔다고 가정)
                Assert.IsTrue(g.TreatGuest());
                Assert.IsFalse(g.TreatGuest(), "하루 한 번");
                if (day < 2) { clock.Advance(TimeSpan.FromDays(1)); g.Resume(); }
            }
            Assert.IsTrue(g.S.dex.Contains(breed));
            Assert.IsTrue(g.CanAdoptGuest); Assert.NotNull(g.AdoptGuest("손님이"));
            Assert.AreEqual(2, g.S.cats.Count);
        }

        [Test]
        public void Walk_Garden_Craft()
        {
            var c = FirstCat(); g.AddCoins(1000);
            Assert.IsTrue(g.SendWalk(c.uid, 2)); Assert.IsFalse(g.Feed(c.uid), "산책 중에는 집에 없음");
            Assert.IsTrue(g.Plant(0, "catnip_seed")); Assert.IsFalse(g.Harvest(0));
            clock.Advance(TimeSpan.FromHours(4.1)); g.Resume();
            Assert.AreEqual("home", c.status); Assert.IsTrue(g.Harvest(0)); Assert.AreEqual(3, g.Material("catnip"));
            int stone0 = g.Material("stone"); Bag.Add(g.S.materials, "stone", 5);
            Assert.IsTrue(g.Craft("r_stepping_stones")); Assert.AreEqual(stone0, g.Material("stone"));
            while (g.Material("stone") >= 5) Assert.IsTrue(g.Craft("r_stepping_stones"));
            Assert.IsFalse(g.Craft("r_stepping_stones"), "재료 없음");
        }

        [Test]
        public void Attendance_SevenDayCycle_NoPenaltyForGaps()
        {
            int j0 = g.S.jelly; string last = null;
            for (int d = 0; d < 7; d++) { last = g.Attend(); Assert.NotNull(last); Assert.IsNull(g.Attend(), "하루 한 번"); clock.Advance(TimeSpan.FromDays(d == 3 ? 3 : 1)); g.Resume(); }
            StringAssert.StartsWith("box:", last);
            Assert.Greater(g.S.jelly, j0);
        }

        [Test]
        public void StarLand_OnlyByChoice_NoAds_LettersStayLocal()
        {
            var c = FirstCat();
            clock.Advance(TimeSpan.FromDays(60)); g.Resume();
            Assert.AreEqual("home", c.status, "게임이 고양이를 떠나보내지 않는다");
            Assert.NotNull(g.SendToStars(c.uid, "햇볕을 좋아했어", "방석"));
            Assert.IsTrue(g.WriteLetter(c.uid, "보고 싶어"));
            Assert.IsFalse(g.ShouldShowInterstitial(true));
            Assert.AreEqual(4, g.S.stars[0].decor.Count, "무료 꾸미기 4개");
        }

        [Test]
        public void Season_StartsAndEnds_WithStampReward()
        {
            g.AddCat("korean_shorthair", "나비", Personality.Playful);
            clock.UtcNow = new DateTime(2026, 10, 18, 1, 0, 0, DateTimeKind.Utc); g.Resume();
            Assert.AreEqual("halloween", g.S.seasonId);
            Assert.IsTrue(g.InShop(Catalog.Item("pumpkin_house"))); Assert.IsFalse(g.InShop(Catalog.Item("sakura_tower")));
            var cat = g.S.cats[0]; g.AddCoins(2000);
            for (int i = 0; i < 10; i++) { g.Buy("kibble_bag"); g.Feed(cat.uid); }
            Assert.IsTrue(g.ClaimSeason()); Assert.AreEqual(1, Bag.Get(g.S.inventory, "pumpkin_house"));
            clock.UtcNow = new DateTime(2026, 11, 1, 1, 0, 0, DateTimeKind.Utc); g.Resume();
            Assert.AreEqual("", g.S.seasonId); Assert.IsFalse(g.InShop(Catalog.Item("pumpkin_house")));
        }

        [Test]
        public void Josa_Particles()
        {
            Assert.AreEqual("콩이", Josa.IGa("콩")); Assert.AreEqual("나비가", Josa.IGa("나비")); Assert.AreEqual("Coco가", Josa.IGa("Coco"));
        }
    }
}
