using System;
using System.Collections.Generic;
using System.Linq;
using CatIsland.Game;
using NUnit.Framework;
using UnityEngine;

namespace CatIsland.Tests
{
    /// <summary>
    /// 30일 플레이 시뮬레이션 (가상 시간). 하루 3번 접속(아침·점심·저녁, 가끔 빠짐), 방치 보상, 돌보기, 손님, 산책, 텃밭,
    /// 만들기, 출석, 할 일, 계절 행사(할로윈) 날짜 넘김, 광고·결제(성공·실패·취소·중간 종료), 앱 강제 종료 뒤 복구,
    /// 시계 되돌리기. 매 행동마다 불변식(재화 음수 없음, 상태 0~1, 방치 보상 8시간 이하, 저장 왕복 일치)을 확인하고
    /// 밸런스(기획서 10장)를 잰다. 결과는 Logs/catisland/sim_report.txt 에도 남긴다.
    /// </summary>
    public class SimulationTests
    {
        [Test]
        public void ThirtyDays_NoErrors_NoBadMoney_BalanceInRange()
        {
            var report = Run(seed: 1, days: 30);
            System.IO.Directory.CreateDirectory("Logs/catisland");
            System.IO.File.WriteAllText("Logs/catisland/sim_report.txt", report);
            Debug.Log(report);
        }

        [Test]
        public void ThirtyDays_OtherPlayers()
        {
            for (int seed = 2; seed <= 6; seed++) Debug.Log(Run(seed, 30));
        }

        static string Run(int seed, int days)
        {
            CatIsland.Game.Game g;
            var rng = new System.Random(seed);
            var clock = new FakeClock(new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc));   // 한국 10월 6일 0시
            var files = new MemoryFiles(); var cloud = new MemoryCloud(); var ads = new FakeAds(); var store = new FakeStore(); var notes = new FakeNotifier(); var gc = new FakeGameCenter();
            g = new CatIsland.Game.Game(clock, files, cloud, ads, store, gc, notes); g.LoadOrNew();
            var log = new List<string>();
            int coinsEarnedIdle = 0, jellyStart = g.S.jelly, jellySpent = 0, purchases = 0, restarts = 0, rollbacks = 0;
            int yardDay = -1, limitedOwned = 0;
            var first = g.AddCat("korean_shorthair", "나비", Personality.Playful);
            Assert.NotNull(first);

            void Check(string where)
            {
                var s = g.S;
                Assert.GreaterOrEqual(s.coins, 0, where); Assert.GreaterOrEqual(s.jelly, 0, where); Assert.GreaterOrEqual(s.idleBank, 0, where);
                Assert.IsTrue(s.materials.All(m => m.n > 0) && s.inventory.All(m => m.n > 0), where + " bag");
                foreach (var c in s.cats) foreach (var v in new[] { c.hunger, c.thirst, c.play, c.clean }) Assert.IsTrue(v >= 0 && v <= 1, where + " needs");
                Assert.LessOrEqual(s.idleHours, Catalog.IdleCapHours + 1e-9, where + " idle hours"); Assert.LessOrEqual(s.idleBank, g.IdleCap() + 2, where + " idle cap");
                Assert.IsTrue(s.cats.All(c => c.status == "home" || c.status == "walk" || c.status == "star"), where + " status");
                Assert.IsFalse(s.cats.Any(c => c.status == "star"), where + " (the game never sends a cat away)");
                var (gw, gh) = CatIsland.Game.Game.GridSize(Zone.Indoor);
                Assert.IsTrue(s.placed.All(p => p.x >= 0 && p.z >= 0 && p.x < gw && p.z < gh), where + " grid");
            }

            for (int day = 0; day < days; day++)
            {
                var morning = new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc).AddDays(day);
                foreach (var hourLocal in new[] { 8.0 + rng.NextDouble(), 12.5 + rng.NextDouble(), 20.0 + rng.NextDouble() })
                {
                    if (rng.NextDouble() < .12) continue;   // (가끔 접속을 거른다)
                    clock.UtcNow = morning.AddHours(hourLocal);
                    // 가끔 앱이 강제로 꺼졌다 다시 켜진다 (마지막 저장에서 이어서)
                    if (rng.NextDouble() < .2) { g = new CatIsland.Game.Game(clock, files, cloud, ads, store, gc, notes); g.LoadOrNew(); g.RestorePending(); restarts++; }
                    else g.Resume();
                    Check($"d{day} open");

                    if (g.CanAttend) g.Attend();
                    // 방치 보상: 가끔 광고로 두 배 (광고는 성공·실패·취소 섞어서)
                    ads.next = new[] { AdResult.Rewarded, AdResult.Rewarded, AdResult.Failed, AdResult.Cancelled }[rng.Next(4)];
                    if (g.S.idleBank > 0)
                    {
                        if (rng.NextDouble() < .5) g.CollectIdleDoubled((ok, c) => coinsEarnedIdle += c);
                        else coinsEarnedIdle += g.CollectIdle();
                    }
                    Check($"d{day} collect");
                    // 돌보기
                    foreach (var c in g.HomeCats.ToList())
                    {
                        if (c.hunger < .6f) { if (!g.Feed(c.uid)) { if (g.S.coins >= 60) { g.Buy("kibble_bag", 2); g.Feed(c.uid); } else g.FreeFoodAd(_ => { }); } }
                        if (c.thirst < .6f) g.GiveWater(c.uid);
                        for (int k = 0; k < 3; k++) g.Pet(c.uid, (float)rng.NextDouble());
                        var toy = g.S.placed.Select(p => p.item).FirstOrDefault(i => Catalog.Item(i).category == ItemCategory.Toy || Catalog.Item(i).category == ItemCategory.Tower);
                        if (toy != null) g.PlayWith(c.uid, toy);
                    }
                    g.CleanLitter();
                    // 손님 고양이 · 입양
                    if (g.GuestHere) { g.TreatGuest(); if (g.CanAdoptGuest) g.AdoptGuest(null); else if (!g.CanAddCat && g.S.catSlots < 3 && g.S.jelly >= Catalog.CatSlotJelly[g.S.catSlots] + 50) { jellySpent += Catalog.CatSlotJelly[g.S.catSlots]; g.OpenCatSlot(); } }
                    // 산책 · 텃밭 · 만들기
                    var walker = g.HomeCats.FirstOrDefault(c => c.uid != g.S.cats[0].uid) ?? (hourLocal > 19 ? g.HomeCats.FirstOrDefault() : null);
                    if (walker != null && rng.NextDouble() < .6) g.SendWalk(walker.uid, Catalog.WalkHours[rng.Next(4)]);
                    for (int p = 0; p < g.S.plots.Count; p++) { if (g.Ripe(p)) g.Harvest(p); else if (string.IsNullOrEmpty(g.S.plots[p].seed)) g.Plant(p, Catalog.Seeds[rng.Next(Catalog.Seeds.Length)].id); }
                    foreach (var r in g.S.recipes.ToList()) { var rd = Catalog.Recipes.First(x => x.id == r); if (g.S.zonesUnlocked.Contains(1) || !rd.needs.Any(m => m == "wood" || m == "stone" || m == "flower")) g.Craft(r); }
                    // 사진
                    if (rng.NextDouble() < .3) g.TakePhoto($"photo_{day}_{hourLocal:0}.png", g.S.cats[0].uid);
                    // 꾸미기: 마당을 먼저 모으고(1주일 분량), 그다음 용품
                    if (!g.S.zonesUnlocked.Contains(1)) { if (g.UnlockYard()) yardDay = day; }
                    else
                    {
                        var want = Catalog.Items.Where(i => g.InShop(i) && i.currency == Currency.Coin && !i.consumable && i.price <= g.S.coins - 200 && !g.S.placed.Any(p => p.item == i.id)).OrderBy(i => rng.Next()).FirstOrDefault();
                        if (want != null && g.Buy(want.id)) PlaceAnywhere(g, want.id);
                    }
                    if (g.S.zonesUnlocked.Contains(0) && !g.S.placed.Any(p => p.item == "cushion") && g.S.coins > 400 && g.Buy("cushion")) PlaceAnywhere(g, "cushion");
                    // 한정 용품을 젤리로 (결제 없이 한 달에 1~2개: 기획서 10장)
                    var lim = Catalog.Items.FirstOrDefault(i => i.limited && g.InShop(i) && g.S.jelly >= i.price && Bag.Get(g.S.inventory, i.id) == 0 && !g.S.placed.Any(p => p.item == i.id));
                    if (lim != null && g.Buy(lim.id)) { jellySpent += lim.price; limitedOwned++; }
                    // 할 일 · 주간 · 계절
                    for (int t = 0; t < g.S.tasks.Count; t++) g.ClaimTask(t);
                    g.ClaimWeekly(); g.ClaimSeason();
                    // 광고 선물 · 무료 뽑기
                    if (rng.NextDouble() < .5) g.GiftAd((c2, j2) => { });
                    if (rng.NextDouble() < .3) g.FreeBoxAd(_ => { });
                    // 결제 (일부 플레이어만): 성공·실패·취소·결제 중 종료
                    if (seed % 2 == 0 && rng.NextDouble() < .05)
                    {
                        var res = new[] { PurchaseResult.Success, PurchaseResult.Failed, PurchaseResult.Cancelled, PurchaseResult.Success }[rng.Next(4)];
                        store.next = res; store.crashBeforeFinish = rng.NextDouble() < .3;
                        g.Purchase("com.nolgoseom.jelly_60", _ => { }); purchases++;
                        store.crashBeforeFinish = false;
                    }
                    g.MaybeInterstitial(false);
                    Check($"d{day} actions");
                    // 앱 내림: 저장, 알림 다시 예약 (하루 2번, 밤 금지)
                    g.Pause();
                    Assert.IsTrue(notes.scheduled.GroupBy(n => TimeUtil.DayKey(n.utc, clock.LocalOffset)).All(x => x.Count() <= 2), "알림 하루 2번");
                    Assert.IsTrue(notes.scheduled.All(n => { var h = (n.utc + clock.LocalOffset).Hour; return h >= 8 && h < 22; }), "밤 알림 없음");
                    // 저장 왕복: 저장한 그대로 불러와진다
                    var back = JsonUtility.FromJson<GameState>(files.Read(CatIsland.Game.Game.SaveName));
                    Assert.AreEqual(g.S.coins, back.coins); Assert.AreEqual(g.S.jelly, back.jelly); Assert.AreEqual(g.S.cats.Count, back.cats.Count);
                }
                // 한 번은 기기 시계를 하루 되돌린다 (보상 없이 그대로 이어져야)
                if (day == 12)
                {
                    long bank = g.S.idleBank; clock.UtcNow = clock.UtcNow.AddDays(-1); g.Resume(); rollbacks++;
                    Assert.AreEqual(bank, g.S.idleBank, "되돌린 시간은 보상 없음"); Check("rollback");
                }
            }
            g.Tick();
            var s2 = g.S;
            int jellyFree = s2.jelly + jellySpent - jellyStart - (s2.purchases.Count > 0 || s2.payer ? 0 : 0);
            string report = $"seed {seed}: cats {s2.cats.Count}, dex {s2.dex.Count}, coins {s2.coins}, jelly {s2.jelly} (spent {jellySpent}), idle coins collected {coinsEarnedIdle}, " +
                            $"yard day {yardDay}, placed {s2.placed.Count}, limited {limitedOwned}, recipes {s2.recipes.Count}, photos {s2.photos.Count}, restarts {restarts}, purchases tried {purchases}, payer {s2.payer}, achievements {s2.achievements.Count}";
            // 밸런스 (기획서 1부 10장)
            Assert.GreaterOrEqual(s2.cats.Count, 2, report + " (손님 고양이를 입양할 수 있어야)");
            Assert.IsTrue(yardDay >= 4 && yardDay <= 14, report + " (섬 확장 1단계 ≈ 1주일)");
            if (!s2.payer) Assert.GreaterOrEqual(s2.jelly + jellySpent - jellyStart, 300, report + " (결제 없이 한 달에 한정 용품 1~2개 분량의 젤리)");
            Assert.GreaterOrEqual(s2.dex.Count, 10, report + " (손님으로 도감이 찬다)");
            return report;
        }

        static bool PlaceAnywhere(CatIsland.Game.Game g, string item)
        {
            var d = Catalog.Item(item); var zone = d.zone;
            var (w, h) = CatIsland.Game.Game.GridSize(zone);
            for (int z = 1; z < h; z++) for (int x = 0; x < w; x++) for (int r = 0; r < 4; r += 2) if (g.Place(item, zone, x, z, r)) return true;
            return false;
        }
    }
}
