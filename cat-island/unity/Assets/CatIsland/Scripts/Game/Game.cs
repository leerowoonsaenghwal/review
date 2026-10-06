using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CatIsland.Game
{
    /// <summary>
    /// 게임 규칙 전체 (기획서 GAME_PLAN_FULL). 화면과 3D 와 떨어진 순수 로직이라 가상 시간으로 30일을 돌려 점검한다
    /// (Tests/EditMode/SimulationTests). 세 가지 약속: 벌주지 않는다 · 고양이가 주인공 · 매일 조금씩 다르다.
    /// 재화는 음수가 되지 않는다. 시계를 되돌리면 그 사이는 없던 시간으로 친다 (보상 없음, 벌도 없음).
    /// </summary>
    public class Game
    {
        public GameState S { get; private set; }
        readonly IClock clock; readonly IFileStore files; readonly ICloudStore cloud;
        public readonly IAds Ads; public readonly IStore Store; public readonly IGameCenter GameCenter; public readonly INotifier Notifier;
        /// <summary>화면에 알릴 일 (손님이 왔다, 산책에서 돌아왔다 …). UI 가 꺼내 간다.</summary>
        public readonly Queue<string> Events = new Queue<string>();
        public const string SaveName = "save.json", BackupName = "save.bak.json";
        const double RollbackTolerance = 300;   // 초. 이보다 크게 시계가 뒤로 가면 되돌림으로 본다

        public Game(IClock clock, IFileStore files, ICloudStore cloud = null, IAds ads = null, IStore store = null, IGameCenter gc = null, INotifier notifier = null)
        {
            this.clock = clock; this.files = files; this.cloud = cloud;
            Ads = ads ?? new FakeAds(); Store = store ?? new FakeStore(); GameCenter = gc ?? new FakeGameCenter(); Notifier = notifier ?? new FakeNotifier();
        }

        // ================================================================ 시간
        long nowCache;
        /// <summary>게임이 쓰는 지금: 기기 시계가 지금까지 본 시각보다 뒤로 갔으면 본 시각에 멈춘다.</summary>
        public long Now
        {
            get
            {
                long real = TimeUtil.ToUnix(clock.UtcNow);
                if (S == null) return real;
                if (real < S.lastSeenAt - RollbackTolerance) { if (nowCache != S.lastSeenAt) { S.clockRollbacks++; nowCache = S.lastSeenAt; } return S.lastSeenAt; }
                return Math.Max(real, S.lastSeenAt);
            }
        }
        DateTime NowUtc => TimeUtil.FromUnix(Now);
        public string Today => TimeUtil.DayKey(NowUtc, clock.LocalOffset);
        int LocalHour => (NowUtc + clock.LocalOffset).Hour;

        // ================================================================ 저장
        public void LoadOrNew()
        {
            GameState local = TryParse(files.Read(SaveName)) ?? TryParse(files.Read(BackupName));
            GameState remote = cloud != null ? TryParse(cloud.Read()) : null;
            S = local == null ? remote : remote == null ? local : (remote.saveCounter > local.saveCounter ? remote : local);
            if (S == null) NewGame();
            Repair();
            Resume();
        }

        static GameState TryParse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { var g = JsonUtility.FromJson<GameState>(json); return g != null && g.createdAt > 0 ? g : null; }
            catch (Exception) { return null; }
        }

        public void Save()
        {
            S.saveCounter++; S.savedAt = Now;
            var json = JsonUtility.ToJson(S);
            var prev = files.Read(SaveName);
            if (prev != null) files.Write(BackupName, prev);   // (한 단계 전 저장: 저장 파일이 깨지면 이것으로)
            files.Write(SaveName, json);
            cloud?.Write(json);
        }

        void NewGame()
        {
            long now = TimeUtil.ToUnix(clock.UtcNow);
            S = new GameState { createdAt = now, lastSeenAt = now, lastIdleAt = now };
            S.recipes.AddRange(Catalog.StarterRecipes);
            for (int i = 0; i < Catalog.GardenPlots; i++) S.plots.Add(new Plot());
            Bag.Add(S.inventory, "food_bowl", 1); Bag.Add(S.inventory, "water_bowl", 1);
            Bag.Add(S.inventory, "kibble_bag", 3);
            // 처음 섬: 그릇과 물그릇은 놓여 있다 (첫 10분: 고양이 만들기 → 밥 → 쓰다듬기 → 방석)
            Place("food_bowl", Zone.Indoor, 9, 6, 0); Place("water_bowl", Zone.Indoor, 9, 4, 0);
        }

        /// <summary>저장 파일 정리: 범위를 벗어난 값, 빠진 목록 (이전 버전이나 손상)을 고친다.</summary>
        void Repair()
        {
            S.coins = Math.Max(0, S.coins); S.jelly = Math.Max(0, S.jelly); S.idleBank = Math.Max(0, S.idleBank);
            S.materials ??= new List<Count>(); S.inventory ??= new List<Count>(); S.placed ??= new List<Placement>(); S.cats ??= new List<CatData>();
            S.plots ??= new List<Plot>(); while (S.plots.Count < Catalog.GardenPlots) S.plots.Add(new Plot());
            S.materials.RemoveAll(c => c.n <= 0); S.inventory.RemoveAll(c => c.n <= 0 || Catalog.Item(c.id) == null);
            S.placed.RemoveAll(p => Catalog.Item(p.item) == null);
            foreach (var c in S.cats) { c.hunger = Mathf.Clamp01(c.hunger); c.thirst = Mathf.Clamp01(c.thirst); c.play = Mathf.Clamp01(c.play); c.clean = Mathf.Clamp01(c.clean); c.affection = Mathf.Max(0, c.affection); }
            S.catSlots = Mathf.Clamp(S.catSlots, Catalog.FirstCatSlots, Catalog.MaxCatSlots);
            if (S.zonesUnlocked == null || S.zonesUnlocked.Count == 0) S.zonesUnlocked = new List<int> { 0 };
        }

        // ================================================================ 접속 · 시간 흐름
        /// <summary>앱을 열거나 다시 돌아올 때. 밀린 시간을 한 번에 처리한다.</summary>
        public void Resume()
        {
            long now = Now;
            Advance(now);
            NewDayIfNeeded();
            S.lastSeenAt = Math.Max(S.lastSeenAt, now);
            ScheduleNotifications();
        }

        /// <summary>앱이 켜져 있는 동안 몇 초마다 (화면 쪽에서 부른다).</summary>
        public void Tick() { long now = Now; Advance(now); NewDayIfNeeded(); S.lastSeenAt = Math.Max(S.lastSeenAt, now); }

        /// <summary>앱을 내릴 때: 저장하고 알림을 다시 예약.</summary>
        public void Pause() { Tick(); ScheduleNotifications(); Save(); }

        void Advance(long now)
        {
            double dt = Math.Max(0, now - S.lastIdleAt);
            if (dt <= 0) return;
            double hours = dt / 3600.0;
            // 방치 보상: 받지 않고 쌓인 시간이 8시간이 될 때까지만 (그동안의 수입 속도: 상태가 줄기 전과 뒤의 평균)
            double r0 = IdleRatePerHour();
            // 상태는 실제 시간으로 줄어든다 (바닥이어도 아프지 않다: 방치 보상만 줄어든다)
            foreach (var c in HomeCats)
            {
                c.hunger = Mathf.Clamp01(c.hunger - (float)(hours / 8)); c.thirst = Mathf.Clamp01(c.thirst - (float)(hours / 10));
                c.play = Mathf.Clamp01(c.play - (float)(hours / 12)); c.clean = Mathf.Clamp01(c.clean - (float)(hours / 24));
            }
            double rate = (r0 + IdleRatePerHour()) / 2;
            if (S.idleBank <= 0) S.idleHours = 0;                    // (받을 것이 없으면 쌓인 시간도 없다: 모두 산책 중이던 동안 등)
            if (rate > 0)
            {
                double add = Math.Min(hours, Math.Max(0, Catalog.IdleCapHours - S.idleHours));
                S.idleBank += (long)Math.Round(rate * add); S.idleHours += add;
            }
            S.lastIdleAt = now;
            // 산책에서 돌아옴
            foreach (var c in S.cats.Where(c => c.status == "walk" && c.walkEndsAt <= now).ToList()) WalkHome(c);
        }

        public IEnumerable<CatData> HomeCats => S.cats.Where(c => c.status == "home");

        /// <summary>코인 / 시간. 고양이마다 기본 40, 기분이 낮으면 최대 −50 %, 모두 행복하면 +20 %, 테마 세트·캣타워 보너스.</summary>
        public double IdleRatePerHour()
        {
            double sum = 0;
            foreach (var c in HomeCats) sum += Catalog.CoinsPerCatHour * (0.5 + 0.5 * c.Mood) * (c.Happy ? 1.2 : 1.0);
            return sum * (1 + ThemeBonus() + TowerBonus());
        }
        public double ThemeBonus()
        {
            var counts = S.placed.Select(p => Catalog.Item(p.item)).Where(d => d != null && d.theme != "기본").GroupBy(d => d.theme).Count(g => g.Count() >= 3);
            return Math.Min(.15, .05 * counts);
        }
        public double TowerBonus() => S.placed.Select(p => Catalog.Item(p.item)).Where(d => d != null).Select(d => (double)d.idleBonus).DefaultIfEmpty(0).Max();
        public bool IdleFull => S.idleHours >= Catalog.IdleCapHours - 1e-6;
        /// <summary>방치 보상이 될 수 있는 가장 큰 값 (모두 행복할 때 8시간): 점검용.</summary>
        public double IdleCap() => HomeCats.Count() * Catalog.CoinsPerCatHour * 1.2 * (1 + ThemeBonus() + TowerBonus()) * Catalog.IdleCapHours;

        public int CollectIdle()
        {
            int c = (int)S.idleBank; S.idleBank = 0; S.idleHours = 0;
            AddCoins(c); Count("collect");
            return c;
        }
        /// <summary>방치 보상 2배 (보상형 광고, 하루 3회). 광고를 끝까지 봤을 때만.</summary>
        public void CollectIdleDoubled(Action<bool, int> done)
        {
            if (S.idleBank <= 0) { done(false, 0); return; }
            WatchAd(Catalog.AdSpot.IdleDouble, ok => { int c = (int)S.idleBank * (ok ? 2 : 1); S.idleBank = 0; S.idleHours = 0; AddCoins(c); Count("collect"); done(ok, c); });
        }

        // ================================================================ 날짜가 바뀔 때
        void NewDayIfNeeded()
        {
            string today = Today;
            if (S.dayKey == today) return;
            S.dayKey = today;
            S.counters.Clear(); S.adCounts.Clear(); S.adGiftJellyToday = 0; S.interstitialsToday = 0; S.allTasksClaimed = false;
            var rng = new System.Random(Hash(today + S.createdAt));
            S.tasks = Catalog.DailyTasks.Where(t => TaskAvailable(t)).OrderBy(_ => rng.Next()).Take(Catalog.DailyTaskCount).Select(t => new TaskState { id = t.id }).ToList();
            string week = TimeUtil.WeekKey(NowUtc, clock.LocalOffset);
            if (S.weekKey != week) { S.weekKey = week; S.weeklyDone = 0; S.weeklyClaimed = false; }
            // 손님 고양이: 하루 1마리 (낮에 온다)
            S.guestDay = today; S.guestTreated = false; S.guestGone = false;
            S.guestBreed = PickGuest(rng);
            // 계절 행사
            var season = Catalog.SeasonAt(NowUtc + clock.LocalOffset);
            if ((season?.id ?? "") != S.seasonId) { S.seasonId = season?.id ?? ""; S.seasonStamps = 0; S.seasonClaimed = false; }
            Events.Enqueue("newday");
        }
        bool TaskAvailable(TaskDef t) => t.counter switch
        {
            "walk" => S.cats.Count > 0, "guestTreat" => true, "plant" => S.zonesUnlocked.Contains(1) || S.onboardingStep >= 2,
            "craft" => S.recipes.Count > 0, "clean" => S.placed.Any(p => p.item == "litter_box"), _ => true,
        };
        static int Hash(string s) { unchecked { int h = 17; foreach (char ch in s) h = h * 31 + ch; return h & 0x7fffffff; } }

        string PickGuest(System.Random rng)
        {
            // 아직 도감에 없는 품종을 조금 더 자주 (같은 날은 같은 손님)
            var pool = Catalog.Breeds.Select(b => (b.id, w: Catalog.BreedWeight(b) * (S.dex.Contains(b.id) ? 1 : 2))).ToList();
            int total = pool.Sum(p => p.w), r = rng.Next(total);
            foreach (var p in pool) { if (r < p.w) return p.id; r -= p.w; }
            return pool[0].id;
        }

        // ================================================================ 재화 (음수 없음)
        public void AddCoins(int n) { if (n > 0) S.coins = checked(S.coins + n); }
        public void AddJelly(int n) { if (n > 0) S.jelly = checked(S.jelly + n); }
        public bool SpendCoins(int n) { if (n < 0 || S.coins < n) return false; S.coins -= n; return true; }
        public bool SpendJelly(int n) { if (n < 0 || S.jelly < n) return false; S.jelly -= n; return true; }
        public bool Spend(Currency c, int n) => c == Currency.Coin ? SpendCoins(n) : SpendJelly(n);
        public int Material(string id) => Bag.Get(S.materials, id);

        void Count(string counter, int n = 1)
        {
            Bag.Add(S.counters, counter, n);
            foreach (var t in S.tasks) { var d = Catalog.DailyTasks.First(x => x.id == t.id); if (d.counter == counter) t.progress = Math.Min(d.goal, t.progress + n); }
            if (!string.IsNullOrEmpty(S.seasonId) && (counter == "pet" || counter == "feed" || counter == "play" || counter == "photo")) S.seasonStamps = Math.Min(Catalog.SeasonStampGoal, S.seasonStamps + (counter == "pet" ? 0 : 1));
        }

        // ================================================================ 고양이
        public CatData Cat(string uid) => S.cats.FirstOrDefault(c => c.uid == uid);
        public bool CanAddCat => S.cats.Count(c => c.status != "star") < S.catSlots;

        /// <summary>첫 고양이 (품종 고르기 또는 사진). 이름 없으면 품종 이름.</summary>
        public CatData AddCat(string breed, string name, Personality p, string eye = "", string whisker = "", string coatJson = "")
        {
            if (!CanAddCat || Catalog.Breeds.All(b => b.id != breed)) return null;
            var c = new CatData { uid = "c" + (S.cats.Count + 1) + "_" + Now, breed = breed, name = string.IsNullOrWhiteSpace(name) ? Catalog.BreedKo(breed) : name.Trim(), personality = p, eyeStyle = eye, whiskerStyle = whisker, coatJson = coatJson, adoptedAt = Now };
            S.cats.Add(c);
            if (!S.dex.Contains(breed)) S.dex.Add(breed);
            AdvanceOnboarding(0);
            Achievement("first_cat"); if (S.cats.Count >= 3) Achievement("three_cats");
            Save();   // (새 고양이는 바로 저장: 직후에 앱이 꺼져도 잃지 않게)
            return c;
        }

        /// <summary>고양이 칸 열기 (첫 칸·첫 입양 칸은 무료, 그다음은 젤리).</summary>
        public bool OpenCatSlot()
        {
            if (S.catSlots >= Catalog.MaxCatSlots) return false;
            int cost = Catalog.CatSlotJelly[S.catSlots];
            if (!SpendJelly(cost)) return false;
            S.catSlots++; return true;
        }

        /// <summary>쓰다듬기 한 번 (기쁨이 오른 만큼 호감). 하루 첫 쓰다듬기는 보너스.</summary>
        public float Pet(string uid, float pleasure = 1f)
        {
            var c = Cat(uid); if (c == null || c.status != "home") return 0;
            float gain = 4f * Mathf.Clamp01(pleasure);
            if (c.lastFirstPetDay != Today) { c.lastFirstPetDay = Today; gain += 10f; }
            int before = c.Level;
            c.affection += gain; c.play = Mathf.Clamp01(c.play + .02f); c.lastPetAt = Now;
            S.totalPets++; Count("pet");
            if (c.Level > before) { Events.Enqueue("levelup:" + uid + ":" + c.Level); Achievement("affection_" + c.Level); }
            if (S.totalPets == 1) AdvanceOnboarding(2);
            return gain;
        }

        /// <summary>밥 주기: 놓인 그릇이 있어야 하고 사료(소모품) 1개를 쓴다. 사료가 없으면 실패 (광고로 1회분 받을 수 있다).</summary>
        public bool Feed(string uid, string food = "kibble_bag")
        {
            var c = Cat(uid); var d = Catalog.Item(food);
            if (c == null || c.status != "home" || d == null || d.fills != NeedKind.Hunger) return false;
            if (!S.placed.Any(p => p.item == "food_bowl" || p.item == "fish_plate") && food == "kibble_bag") return false;
            if (Bag.Get(S.inventory, food) <= 0) return false;
            Bag.Add(S.inventory, food, -1);
            c.hunger = Mathf.Clamp01(c.hunger + (food == "churu" ? .35f : food == "can_food" ? .6f : .7f));
            c.affection += food == "churu" ? 6 : 3; S.totalMeals++; Count("feed");
            if (S.totalMeals == 1) AdvanceOnboarding(1);
            return true;
        }
        public bool GiveWater(string uid)
        {
            var c = Cat(uid); if (c == null || c.status != "home") return false;
            if (!S.placed.Any(p => Catalog.Item(p.item)?.fills == NeedKind.Thirst)) return false;
            c.thirst = 1f; Count("water"); return true;
        }
        public bool PlayWith(string uid, string toy)
        {
            var c = Cat(uid); var d = Catalog.Item(toy);
            if (c == null || c.status != "home" || d == null || (d.category != ItemCategory.Toy && d.category != ItemCategory.Tower && d.fills != NeedKind.Play && d.fills != NeedKind.Rest)) return false;
            if (!S.placed.Any(p => p.item == toy) && Bag.Get(S.inventory, toy) <= 0) return false;
            bool fav = Catalog.FavoriteItems(c.personality).Contains(toy);
            c.play = Mathf.Clamp01(c.play + .5f); c.affection += fav ? 8 : 5; Count("play");
            if (d.category == ItemCategory.Tower) { S.totalJumps++; if (!c.firsts.Contains("first_jump")) { c.firsts.Add("first_jump"); Events.Enqueue("first:" + uid + ":jump"); } }
            return true;
        }
        public bool CleanLitter()
        {
            if (!S.placed.Any(p => p.item == "litter_box" || p.item == "sandbox")) return false;
            foreach (var c in HomeCats) c.clean = 1f;
            Count("clean"); return true;
        }

        // ================================================================ 상점 · 가방 · 꾸미기
        public bool InShop(ItemDef d)
        {
            if (d.limited) return d.season != null && d.season == S.seasonId;
            if (Catalog.Recipes.Any(r => r.item == d.id) && !S.unlockedItems.Contains(d.id)) return false;   // (만들기로 여는 용품)
            if (d.zone == Zone.Yard && !S.zonesUnlocked.Contains(1)) return false;
            return true;
        }
        public bool Buy(string id, int n = 1)
        {
            var d = Catalog.Item(id); if (d == null || n <= 0 || !InShop(d)) return false;
            if (!Spend(d.currency, d.price * n)) return false;
            Bag.Add(S.inventory, id, n); Save(); return true;
        }

        // 격자: 칸 0.6 m. 집 안 12×12, 마당 12×8. 용품 앞 한 칸은 고양이가 쓰는 자리라 비워 둔다.
        public static (int w, int h) GridSize(Zone z) => z == Zone.Indoor ? (12, 12) : (12, 8);
        static (int x0, int z0, int x1, int z1) Footprint(Placement p, bool withUseSpot)
        {
            var d = Catalog.Item(p.item); int w = d.w, h = d.h; if (p.rot % 2 == 1) (w, h) = (h, w);
            int x0 = p.x, z0 = p.z, x1 = p.x + w - 1, z1 = p.z + h - 1;
            if (withUseSpot && (d.category != ItemCategory.Deco && d.category != ItemCategory.Outdoor || d.fills != NeedKind.None))
                switch (p.rot % 4) { case 0: z0--; break; case 1: x1++; break; case 2: z1++; break; default: x0--; break; }   // (앞쪽 한 칸)
            return (x0, z0, x1, z1);
        }
        /// <summary>섬 모양에 맞는 칸인가: 집 안은 둥근 마루(격자 가운데에서 5.6칸 안), 마당은 네모 전부.</summary>
        public static bool CellValid(Zone zone, int x, int z)
        {
            var (w, h) = GridSize(zone); if (x < 0 || z < 0 || x >= w || z >= h) return false;
            if (zone == Zone.Yard) return true;
            float dx = x + .5f - w / 2f, dz = z + .5f - h / 2f; return dx * dx + dz * dz <= 5.6f * 5.6f;
        }
        public bool CanPlace(string item, Zone zone, int x, int z, int rot, Placement ignore = null)
        {
            var d = Catalog.Item(item); if (d == null || !S.zonesUnlocked.Contains((int)zone)) return false;
            if (d.zone != zone && !(d.category == ItemCategory.Deco)) return false;
            var p = new Placement { item = item, zone = zone, x = x, z = z, rot = rot & 3 };
            var (gw, gh) = GridSize(zone); var f = Footprint(p, false);
            if (f.x0 < 0 || f.z0 < 0 || f.x1 >= gw || f.z1 >= gh) return false;
            for (int cx = f.x0; cx <= f.x1; cx++) for (int cz = f.z0; cz <= f.z1; cz++) if (!CellValid(zone, cx, cz)) return false;
            var fu = Footprint(p, true);
            foreach (var o in S.placed)
            {
                if (o == ignore || o.zone != zone) continue;
                var a = Footprint(o, true); var b = Footprint(o, false);
                if (Overlap(f, a) || Overlap(fu, b)) return false;    // (내 몸이 남의 쓰는 자리에, 내 쓰는 자리가 남의 몸에 겹치면 안 됨)
            }
            return true;
        }
        static bool Overlap((int x0, int z0, int x1, int z1) a, (int x0, int z0, int x1, int z1) b) => a.x0 <= b.x1 && b.x0 <= a.x1 && a.z0 <= b.z1 && b.z0 <= a.z1;

        public bool Place(string item, Zone zone, int x, int z, int rot)
        {
            if (Bag.Get(S.inventory, item) <= 0 || Catalog.Item(item).consumable || !CanPlace(item, zone, x, z, rot)) return false;
            Bag.Add(S.inventory, item, -1);
            S.placed.Add(new Placement { item = item, zone = zone, x = x, z = z, rot = rot & 3, placedAt = Now });
            Count("place"); if (item == "cushion") AdvanceOnboarding(3);
            if (Catalog.Item(item).category == ItemCategory.Tower) AdvanceOnboarding(5);
            if (S.placed.Count >= 10) Achievement("decorator_" + Math.Min(30, S.placed.Count / 10 * 10));
            return true;
        }
        public bool Move(int index, int x, int z, int rot)
        {
            if (index < 0 || index >= S.placed.Count) return false;
            var p = S.placed[index]; if (!CanPlace(p.item, p.zone, x, z, rot, p)) return false;
            p.x = x; p.z = z; p.rot = rot & 3; return true;
        }
        public bool PutAway(int index)
        {
            if (index < 0 || index >= S.placed.Count) return false;
            Bag.Add(S.inventory, S.placed[index].item, 1); S.placed.RemoveAt(index); return true;
        }

        /// <summary>마당 열기 (코인 + 재료, 약 1주일 분량).</summary>
        public bool UnlockYard()
        {
            if (S.zonesUnlocked.Contains(1)) return false;
            if (S.coins < Catalog.YardExpansionCoins || Catalog.YardExpansionMaterials.Any(m => Material(m.id) < m.n)) return false;
            SpendCoins(Catalog.YardExpansionCoins); foreach (var m in Catalog.YardExpansionMaterials) Bag.Add(S.materials, m.id, -m.n);
            S.zonesUnlocked.Add(1); Bag.Add(S.inventory, "garden_bed", 1);
            Achievement("yard"); AdvanceOnboarding(7); Events.Enqueue("yard"); Save();
            return true;
        }

        // ================================================================ 할 일 · 주간 · 출석 · 계절
        public bool ClaimTask(int i)
        {
            if (i < 0 || i >= S.tasks.Count) return false;
            var t = S.tasks[i]; var d = Catalog.DailyTasks.First(x => x.id == t.id);
            if (t.claimed || t.progress < d.goal) return false;
            t.claimed = true; AddJelly(d.jelly); AddCoins(d.coins); S.weeklyDone++;
            if (!S.allTasksClaimed && S.tasks.All(x => x.claimed)) { S.allTasksClaimed = true; AddJelly(Catalog.DailyAllDoneJelly); }
            return true;
        }
        public bool ClaimWeekly()
        {
            if (S.weeklyClaimed || S.weeklyDone < Catalog.WeeklyGoal) return false;
            S.weeklyClaimed = true; AddJelly(15); Bag.Add(S.materials, "yarn", 3); Bag.Add(S.materials, "shell", 3); return true;
        }
        public bool CanAttend => S.lastAttendDay != Today;
        /// <summary>출석 도장 (7일 주기, 7일째 상자). 빠진 날이 있어도 이어서 찍는다 (벌 없음).</summary>
        public string Attend()
        {
            if (!CanAttend) return null;
            S.lastAttendDay = Today;
            int i = S.attendIndex; S.attendIndex = (S.attendIndex + 1) % 7;
            if (i < 6) { AddJelly(Catalog.AttendanceJelly[i]); return "jelly:" + Catalog.AttendanceJelly[i]; }
            AddJelly(Catalog.AttendanceDay7Jelly);
            var item = Catalog.AttendanceDay7Items[Hash(Today) % Catalog.AttendanceDay7Items.Length]; Bag.Add(S.inventory, item, 1);
            Achievement("attend_7"); AdvanceOnboarding(8);
            return "box:" + item;
        }
        public bool ClaimSeason()
        {
            if (string.IsNullOrEmpty(S.seasonId) || S.seasonClaimed || S.seasonStamps < Catalog.SeasonStampGoal) return false;
            var s = Catalog.Seasons.First(x => x.id == S.seasonId);
            S.seasonClaimed = true; foreach (var it in s.rewards) Bag.Add(S.inventory, it, 1);
            return true;
        }

        // ================================================================ 손님 고양이
        public bool GuestHere => !string.IsNullOrEmpty(S.guestBreed) && !S.guestGone && LocalHour >= 7 && LocalHour < 21;
        /// <summary>손님에게 간식: 도감 등록, 만난 횟수 +1 (3번이면 입양 가능), 코인·가끔 레시피.</summary>
        public bool TreatGuest()
        {
            if (!GuestHere || S.guestTreated) return false;
            S.guestTreated = true; Bag.Add(S.guestMeetings, S.guestBreed, 1);
            if (!S.dex.Contains(S.guestBreed)) { S.dex.Add(S.guestBreed); Events.Enqueue("dex:" + S.guestBreed); GameCenter.Score("dex", S.dex.Count); if (S.dex.Count >= Catalog.Breeds.Length) Achievement("dex_all"); }
            AddCoins(Catalog.GuestTreatCoins); Count("guestTreat"); Bag.Add(S.materials, "feather", 1);
            var rng = new System.Random(Hash(Today + "recipe"));
            var unknown = Catalog.Recipes.Where(r => !S.recipes.Contains(r.id)).ToList();
            if (unknown.Count > 0 && rng.NextDouble() < .35) { var r = unknown[rng.Next(unknown.Count)]; S.recipes.Add(r.id); Events.Enqueue("recipe:" + r.id); }
            if (S.onboardingStep <= 4) AdvanceOnboarding(4);
            return true;
        }
        public int Meetings(string breed) => Bag.Get(S.guestMeetings, breed);
        public bool CanAdoptGuest => GuestHere && Meetings(S.guestBreed) >= Catalog.AdoptAfterMeetings && CanAddCat;
        public CatData AdoptGuest(string name)
        {
            if (!CanAdoptGuest) return null;
            var p = (Personality)(Hash(S.guestBreed + Today) % 5);
            var c = AddCat(S.guestBreed, name, p); if (c != null) { S.guestGone = true; Events.Enqueue("adopt:" + c.uid); AdvanceOnboarding(6); }
            return c;
        }

        // ================================================================ 산책 · 텃밭 · 만들기
        public bool SendWalk(string uid, int hours)
        {
            var c = Cat(uid); if (c == null || c.status != "home" || !Catalog.WalkHours.Contains(hours)) return false;
            c.status = "walk"; c.walkHours = hours; c.walkEndsAt = Now + hours * 3600L; Count("walk");
            if (S.onboardingStep <= 3) AdvanceOnboarding(3);
            ScheduleNotifications();
            return true;
        }
        /// <summary>산책 빨리 부르기 (보상형 광고, 하루 2회).</summary>
        public void CallWalkHome(string uid, Action<bool> done)
        {
            var c = Cat(uid); if (c == null || c.status != "walk") { done(false); return; }
            WatchAd(Catalog.AdSpot.WalkSkip, ok => { if (ok) { c.walkEndsAt = Now; WalkHome(c); } done(ok); });
        }
        void WalkHome(CatData c)
        {
            var (coins, mats, rare) = Catalog.WalkReward(c.walkHours);
            var rng = new System.Random(Hash(c.uid + c.walkEndsAt));
            AddCoins(coins);
            for (int i = 0; i < mats; i++) Bag.Add(S.materials, Catalog.PickWalkMaterial(rng), 1);
            if (!c.firsts.Contains("first_walk")) { Bag.Add(S.materials, "wood", 2); Bag.Add(S.materials, "stone", 2); }   // (첫 산책 선물: 마당으로 가는 첫걸음)
            string gift = "";
            if (rng.NextDouble() < rare) { gift = Catalog.WalkRareItems[rng.Next(Catalog.WalkRareItems.Length)]; Bag.Add(S.inventory, gift, 1); if (!S.unlockedItems.Contains(gift)) S.unlockedItems.Add(gift); }
            c.status = "home"; c.walkEndsAt = 0; c.play = Mathf.Clamp01(c.play + .3f); c.affection += 5;
            if (!c.firsts.Contains("first_walk")) c.firsts.Add("first_walk");
            Events.Enqueue("walkhome:" + c.uid + ":" + gift);
        }
        public bool Plant(int plot, string seed)
        {
            if (plot < 0 || plot >= S.plots.Count || !string.IsNullOrEmpty(S.plots[plot].seed)) return false;
            var d = Catalog.Seeds.FirstOrDefault(x => x.id == seed); if (d == null || !SpendCoins(d.price)) return false;
            S.plots[plot].seed = seed; S.plots[plot].plantedAt = Now; Count("plant");
            if (S.onboardingStep <= 3) AdvanceOnboarding(3);
            return true;
        }
        public bool Ripe(int plot) { var p = S.plots[plot]; var d = Catalog.Seeds.FirstOrDefault(x => x.id == p.seed); return d != null && Now - p.plantedAt >= d.hours * 3600L; }
        public bool Harvest(int plot)
        {
            if (plot < 0 || plot >= S.plots.Count || !Ripe(plot)) return false;
            var d = Catalog.Seeds.First(x => x.id == S.plots[plot].seed);
            Bag.Add(S.materials, d.yields, d.amount); S.plots[plot] = new Plot(); return true;
        }
        public bool Craft(string recipeId)
        {
            var r = Catalog.Recipes.FirstOrDefault(x => x.id == recipeId);
            if (r == null || !S.recipes.Contains(recipeId)) return false;
            for (int i = 0; i < r.needs.Length; i++) if (Material(r.needs[i]) < r.counts[i]) return false;
            for (int i = 0; i < r.needs.Length; i++) Bag.Add(S.materials, r.needs[i], -r.counts[i]);
            Bag.Add(S.inventory, r.item, 1); if (!S.unlockedItems.Contains(r.item)) S.unlockedItems.Add(r.item);
            Count("craft"); AdvanceOnboarding(5); Achievement("crafter"); Save();
            return true;
        }

        // ================================================================ 광고 (보상형 중심) · 결제
        public int AdsToday(Catalog.AdSpot s) => Bag.Get(S.adCounts, s.ToString());
        public bool AdAvailable(Catalog.AdSpot s) => AdsToday(s) < Catalog.AdDailyCap(s) && Ads.RewardedReady;
        /// <summary>보상형 광고: 끝까지 봤을 때만 보상, 실패·취소는 그대로 (횟수도 안 씀).</summary>
        public void WatchAd(Catalog.AdSpot s, Action<bool> done)
        {
            if (!AdAvailable(s)) { done(false); return; }
            Ads.ShowRewarded(s.ToString(), r => { bool ok = r == AdResult.Rewarded; if (ok) Bag.Add(S.adCounts, s.ToString(), 1); done(ok); });
        }
        public void FreeFoodAd(Action<bool> done) => WatchAd(Catalog.AdSpot.FreeFood, ok => { if (ok) Bag.Add(S.inventory, "kibble_bag", 1); done(ok); });
        /// <summary>무료 뽑기 상자 (확률 공개: Catalog.FreeBoxOdds).</summary>
        public void FreeBoxAd(Action<string> done) => WatchAd(Catalog.AdSpot.FreeBox, ok =>
        {
            if (!ok) { done(null); return; }
            double r = new System.Random(Hash(Today + AdsToday(Catalog.AdSpot.FreeBox) + S.saveCounter)).NextDouble(), acc = 0;
            foreach (var (item, p) in Catalog.FreeBoxOdds) { acc += p; if (r < acc) { Bag.Add(S.inventory, item, 1); done(item); return; } }
            Bag.Add(S.inventory, Catalog.FreeBoxOdds[0].item, 1); done(Catalog.FreeBoxOdds[0].item);
        });
        /// <summary>고양이가 물어 온 선물 상자 (광고, 하루 2회): 코인, 하루 1번만 젤리 소량.</summary>
        public void GiftAd(Action<int, int> done) => WatchAd(Catalog.AdSpot.Gift, ok =>
        {
            if (!ok) { done(0, 0); return; }
            int coins = 80, jelly = 0; AddCoins(coins);
            if (S.adGiftJellyToday < Catalog.AdGiftJellyPerDay) { S.adGiftJellyToday++; jelly = 3; AddJelly(jelly); }
            done(coins, jelly);
        });
        /// <summary>전면 광고는 화면이 넘어갈 때만: 설치 3일 이내·결제자·4분 이내·하루 6회 넘으면 안 보인다.</summary>
        public bool ShouldShowInterstitial(bool inStarLand)
        {
            if (inStarLand || S.payer) return false;
            if (Now - S.createdAt < Catalog.InterstitialFreeDays * 86400L) return false;
            if (S.interstitialsToday >= Catalog.InterstitialDailyCap || Now - S.lastInterstitialAt < Catalog.InterstitialMinSeconds) return false;
            return true;
        }
        public void MaybeInterstitial(bool inStarLand)
        {
            if (!ShouldShowInterstitial(inStarLand)) return;
            S.interstitialsToday++; S.lastInterstitialAt = Now; Ads.ShowInterstitial(() => { });
        }

        public void Purchase(string productId, Action<PurchaseResult> done)
        {
            var p = Catalog.Products.FirstOrDefault(x => x.id == productId);
            if (p == null) { done(PurchaseResult.Failed); return; }
            if (p.kind != "jelly" && S.purchases.Contains(productId)) { done(PurchaseResult.Failed); return; }   // (이미 산 세트)
            Store.Purchase(productId, (r, tx) => { if (r == PurchaseResult.Success) Grant(productId, tx); done(r); });
        }
        /// <summary>지급 → 저장 → 확인(Finish). 지급 전에 꺼진 결제는 다음 실행에 RestorePending 으로 지급한다 (두 번 지급하지 않음).</summary>
        void Grant(string productId, string tx)
        {
            if (S.pendingTransactions.Contains(tx)) { Store.Finish(tx); return; }
            var p = Catalog.Products.First(x => x.id == productId);
            AddJelly(p.jelly); foreach (var it in p.items) { if (Catalog.Item(it) != null) Bag.Add(S.inventory, it, 1); }
            if (p.kind != "jelly" && !S.purchases.Contains(productId)) S.purchases.Add(productId);
            S.payer = true; S.pendingTransactions.Add(tx);
            Save(); Store.Finish(tx);
        }
        /// <summary>구매 복원 (설정): 앱스토어에 지난 결제를 다시 묻고, 끝나지 않은 결제를 지급한다.</summary>
        public void RestorePurchases() { Store.Restore(); RestorePending(); }
        public void RestorePending() { foreach (var (product, tx) in Store.Unfinished()) Grant(product, tx); }

        // ================================================================ 사진 · 별나라 · 업적
        public void TakePhoto(string file, string catUid, string caption = "")
        {
            S.photos.Add(new PhotoEntry { file = file, catUid = catUid, caption = caption, takenAt = Now });   // (사진은 기기 안에만)
            Count("photo"); Achievement("photographer"); if (S.onboardingStep <= 8) AdvanceOnboarding(8);
        }
        /// <summary>별나라로 (이용자가 고른 고양이만. 게임이 고양이를 떠나보내는 일은 없다).</summary>
        public StarCard SendToStars(string uid, string memo, string favorite)
        {
            var c = Cat(uid); if (c == null || c.status == "star") return null;
            c.status = "star"; c.starredAt = Now;
            var card = new StarCard { catUid = uid, memo = memo ?? "", favorite = favorite ?? "", createdAt = Now };
            card.decor.AddRange(Catalog.StarItems.Where(i => i.free).Select(i => i.id));
            S.stars.Add(card); Save(); return card;
        }
        public bool WriteLetter(string uid, string text)
        {
            var card = S.stars.FirstOrDefault(s => s.catUid == uid); if (card == null || string.IsNullOrWhiteSpace(text)) return false;
            card.letters.Add(text.Trim()); return true;
        }
        void Achievement(string id) { if (S.achievements.Contains(id)) return; S.achievements.Add(id); GameCenter.Report(id); }

        // ================================================================ 처음 7일
        // 0 첫 고양이 → 1 밥 → 2 쓰다듬기 → 3 방석 놓기 / 산책·텃밭 → 4 손님 → 5 캣타워·만들기 → 6 입양 → 7 마당 → 8 출석 7일·사진
        void AdvanceOnboarding(int step) { if (S.onboardingStep == step) { S.onboardingStep = step + 1; Events.Enqueue("onboarding:" + S.onboardingStep); } }
        /// <summary>지금 고양이 말풍선으로 보여 줄 안내 (긴 설명 없음).</summary>
        public string OnboardingHint() => S.onboardingStep switch
        {
            0 => "hint_make_cat", 1 => "hint_feed", 2 => "hint_pet", 3 => "hint_cushion", 4 => "hint_guest", 5 => "hint_tower", 6 => "hint_adopt", 7 => "hint_yard", 8 => "hint_photo", _ => "",
        };

        // ================================================================ 알림 (하루 2번까지, 밤 10시~아침 8시 금지)
        public void ScheduleNotifications()
        {
            Notifier.ClearAll();
            var list = new List<(DateTime at, string id, string title, string body)>();
            var name = S.cats.FirstOrDefault(c => c.status != "star")?.name ?? "고양이";
            if (S.notifyIdleFull && S.cats.Any(c => c.status == "home"))
            {
                double rate = IdleRatePerHour();
                if (rate > 0) { double hoursLeft = Math.Max(0, Catalog.IdleCapHours - S.idleHours); list.Add((NowUtc.AddHours(Math.Max(hoursLeft, .5)), "idle", "놀러와요 고양이섬", $"{Josa.IGa(name)} 모아 둔 코인이 가득 찼어요")); }
            }
            if (S.notifyWalkHome)
                foreach (var c in S.cats.Where(c => c.status == "walk")) list.Add((TimeUtil.FromUnix(c.walkEndsAt), "walk_" + c.uid, "놀러와요 고양이섬", $"{Josa.IGa(c.name)} 선물을 물고 왔어요"));
            var perDay = new Dictionary<string, int>();
            foreach (var n in list.OrderBy(n => n.at))
            {
                var at = QuietShift(n.at); var day = TimeUtil.DayKey(at, clock.LocalOffset);
                perDay.TryGetValue(day, out int k); if (k >= 2) continue;
                perDay[day] = k + 1; Notifier.Schedule(n.id, at, n.title, n.body);
            }
        }
        /// <summary>밤 10시 ~ 아침 8시 사이면 아침 8시로 미룬다.</summary>
        DateTime QuietShift(DateTime utc)
        {
            var local = utc + clock.LocalOffset;
            if (local.Hour >= 22) local = local.Date.AddDays(1).AddHours(8);
            else if (local.Hour < 8) local = local.Date.AddHours(8);
            return local - clock.LocalOffset;
        }
    }

    /// <summary>받침에 따라 이/가, 을/를 (알림·말풍선 문구).</summary>
    public static class Josa
    {
        static bool HasFinal(string w) { if (string.IsNullOrEmpty(w)) return false; char c = w[w.Length - 1]; return c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 != 0; }
        public static string IGa(string w) => w + (HasFinal(w) ? "이" : "가");
        public static string EulReul(string w) => w + (HasFinal(w) ? "을" : "를");
        public static string WaGwa(string w) => w + (HasFinal(w) ? "과" : "와");
    }
}
