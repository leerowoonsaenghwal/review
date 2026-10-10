using System;
using System.Collections.Generic;
using System.Linq;

namespace CatIsland.Game
{
    public enum ItemCategory { Food, Toy, Furniture, Tower, Outdoor, Deco }
    public enum Zone { Indoor = 0, Yard = 1 }
    public enum Personality { Easygoing, Playful, Aloof, Foodie, Shy }
    public enum Currency { Coin, Jelly }
    /// <summary>용품이 채워 주는 상태 (고양이가 그 용품을 쓰면).</summary>
    public enum NeedKind { None, Hunger, Thirst, Play, Clean, Rest }

    /// <summary>용품 하나. 가격 기준: 기본 용품 1개 = 하루 접속 1번 분량의 코인 (기획서 10장).</summary>
    [Serializable]
    public class ItemDef
    {
        public string id, ko, theme;
        public ItemCategory category;
        public Zone zone;
        public Currency currency;
        public int price;
        public int w = 1, h = 1;              // 격자 칸 (0.6 m 한 칸)
        public NeedKind fills;
        public float idleBonus;               // 방치 보상 배율 더하기 (캣타워 0.1~0.3)
        public bool consumable;               // 사료·캔·츄르: 쓰면 없어진다
        public bool limited;                  // 계절 한정 / 별나라 / 결제 세트
        public string model;                  // assets/items/<model>; 없으면 아직 모델 없음 (가까운 모델로 대신 보임)
        public string season;                 // 계절 한정이면 행사 id
        public string ToString2() => $"{id}({price}{(currency == Currency.Coin ? "c" : "j")})";
    }

    /// <summary>데크 타일: 섬 풀밭 격자 한 칸(0.6 m)에 까는 바닥. 9장 묶음으로 산다. surface: 고양이 발소리 (0 나무, 1 러그, 3 모래 - CatAudio.Surface).</summary>
    [Serializable] public class TileDef { public string id, ko; public int price, pack = 9, surface; }
    [Serializable] public class BreedDef { public string id, ko; public int rarity; }   // rarity 1 흔함 .. 3 드묾 (손님으로 오는 확률)
    [Serializable] public class SeedDef { public string id, ko, yields; public int hours, price, amount; }
    [Serializable] public class RecipeDef { public string id, item; public string[] needs; public int[] counts; }
    [Serializable] public class SeasonDef { public string id, ko; public int fromMonth, fromDay, toMonth, toDay; public string[] rewards; }
    [Serializable] public class ProductDef { public string id, ko; public int priceKrw, jelly; public string[] items; public string kind; }   // kind: jelly / season / star
    [Serializable] public class TaskDef { public string id, ko, counter; public int goal, jelly, coins; }

    /// <summary>
    /// 게임 데이터 전체 (기획서 GAME_PLAN_FULL 1부·2부). 코드로 정의해 테스트로 검증한다: id 중복 없음, 60종, 가격 범위 등.
    /// </summary>
    public static class Catalog
    {
        // ---------------------------------------------------------------- 밸런스 기준값
        public const int CoinsPerCatHour = 40;          // 고양이 1마리가 1시간에 모으는 코인 (기본)
        public const double IdleCapHours = 8;           // 방치 보상 최대 8시간
        public const int BasicItemPrice = 300;          // ≈ 하루 접속 1번 분량 (2.5~3시간 간격 × 고양이 1~2마리)
        public const int YardExpansionCoins = 3500;     // 섬 확장 1단계 ≈ 1주일 분량 (시뮬레이션: 하루 약 500 코인)
        public static readonly (string id, int n)[] YardExpansionMaterials = { ("wood", 6), ("stone", 4), ("flower", 3) };
        public const int FirstCatSlots = 1, MaxCatSlots = 6;
        public static readonly int[] CatSlotJelly = { 0, 0, 120, 200, 300, 400 };   // n번째 칸(0부터)을 여는 젤리. 0·1번 칸은 무료 (첫 고양이 + 첫 입양)

        // ---------------------------------------------------------------- 품종 33
        public static readonly BreedDef[] Breeds =
        {
            B("korean_shorthair", "코리안 숏헤어", 1), B("russian_blue", "러시안 블루", 2), B("persian", "페르시안", 2), B("himalayan", "히말라얀", 3),
            B("chinchilla_persian", "친칠라 페르시안", 3), B("siamese", "샴", 2), B("scottish_fold", "스코티시 폴드", 2), B("british_shorthair", "브리티시 숏헤어", 1),
            B("munchkin", "먼치킨", 2), B("ragdoll", "랙돌", 2), B("birman", "버먼", 3), B("american_shorthair", "아메리칸 숏헤어", 1),
            B("norwegian_forest", "노르웨이 숲", 2), B("maine_coon", "메인쿤", 2), B("siberian", "시베리안", 2), B("bengal", "벵갈", 2),
            B("egyptian_mau", "이집션 마우", 3), B("abyssinian", "아비시니안", 2), B("somali", "소말리", 3), B("savannah", "사바나", 3),
            B("oriental", "오리엔탈", 3), B("sphynx", "스핑크스", 3), B("turkish_angora", "터키시 앙고라", 2), B("turkish_van", "터키시 반", 3),
            B("exotic_shorthair", "엑조틱 숏헤어", 2), B("american_curl", "아메리칸 컬", 3), B("devon_rex", "데본 렉스", 3), B("cornish_rex", "코니시 렉스", 3),
            B("selkirk_rex", "셀커크 렉스", 3), B("laperm", "라팜", 3), B("manx", "맹크스", 3), B("japanese_bobtail", "재패니즈 밥테일", 3),
            B("bombay", "봄베이", 2),
        };
        static BreedDef B(string id, string ko, int r) => new BreedDef { id = id, ko = ko, rarity = r };

        // ---------------------------------------------------------------- 용품 60
        // (fills: 고양이가 쓰면 채워지는 상태. 테마 같은 용품 3개 이상 → 방치 보상 +5%, 최대 +15%)
        public static readonly ItemDef[] Items = BuildItems();

        /// <summary>데크 타일 8가지 (무늬는 IslandArt.PaintedTextures.Tile, 그리기는 FloorTiles).</summary>
        public static readonly TileDef[] Tiles =
        {
            new TileDef { id = "deck_honey", ko = "원목 마루", price = 90, surface = 0 },
            new TileDef { id = "deck_white", ko = "화이트 우드", price = 110, surface = 0 },
            new TileDef { id = "deck_walnut", ko = "월넛 마루", price = 130, surface = 0 },
            new TileDef { id = "deck_basket", ko = "바둑판 마루", price = 160, surface = 0 },
            new TileDef { id = "deck_sakura", ko = "벚꽃 마루", price = 150, surface = 0 },
            new TileDef { id = "tile_mint", ko = "민트 체크 타일", price = 120, surface = 0 },
            new TileDef { id = "tile_terracotta", ko = "테라코타 타일", price = 140, surface = 0 },
            new TileDef { id = "stone_path", ko = "징검돌 판", price = 100, surface = 3 },
        };
        public static TileDef Tile(string id) { foreach (var t in Tiles) if (t.id == id) return t; return null; }
        public const string StarterTile = "deck_honey"; public const int StarterTiles = 9;

        static ItemDef[] BuildItems()
        {
            var L = new List<ItemDef>();
            void I(string id, string ko, ItemCategory c, Zone z, int price, NeedKind fills = NeedKind.None, string theme = "기본", int w = 1, int h = 1,
                   string model = null, Currency cur = Currency.Coin, float idle = 0f, bool consumable = false, bool limited = false, string season = null)
                => L.Add(new ItemDef { id = id, ko = ko, category = c, zone = z, price = price, fills = fills, theme = theme, w = w, h = h, model = model ?? id, currency = cur, idleBonus = idle, consumable = consumable, limited = limited, season = season });
            // 먹을 것 (그릇은 다시 채워 쓰는 용품, 사료·캔·츄르는 소모품)
            I("food_bowl", "사료 그릇", ItemCategory.Food, Zone.Indoor, 120, NeedKind.Hunger, "기본");
            I("water_bowl", "물그릇", ItemCategory.Food, Zone.Indoor, 120, NeedKind.Thirst, "기본");
            I("milk_bowl", "우유 그릇", ItemCategory.Food, Zone.Indoor, 160, NeedKind.Thirst, "딸기");
            I("can_food", "고양이 캔", ItemCategory.Food, Zone.Indoor, 40, NeedKind.Hunger, consumable: true);
            I("churu", "츄르", ItemCategory.Food, Zone.Indoor, 30, NeedKind.Hunger, consumable: true);
            I("kibble_bag", "사료 한 봉지", ItemCategory.Food, Zone.Indoor, 60, NeedKind.Hunger, consumable: true, model: "food_bowl");
            I("fountain", "고양이 정수기", ItemCategory.Food, Zone.Indoor, 480, NeedKind.Thirst, "바다");
            I("treat_jar", "생선 쿠키 병", ItemCategory.Food, Zone.Indoor, 260, NeedKind.Hunger, "바다");
            I("fish_plate", "생선 접시", ItemCategory.Food, Zone.Indoor, 220, NeedKind.Hunger, "바다");
            // 장난감
            I("ball", "털실 공", ItemCategory.Toy, Zone.Indoor, 150, NeedKind.Play, "기본");
            I("mouse_toy", "쥐돌이", ItemCategory.Toy, Zone.Indoor, 150, NeedKind.Play, "기본");
            I("wand_toy", "낚싯대 장난감", ItemCategory.Toy, Zone.Indoor, 220, NeedKind.Play, "기본");
            I("yarn_basket", "털실 바구니", ItemCategory.Toy, Zone.Indoor, 340, NeedKind.Play, "숲");
            I("paper_box", "종이 상자", ItemCategory.Toy, Zone.Indoor, 120, NeedKind.Rest, "기본");
            I("tunnel", "놀이 터널", ItemCategory.Toy, Zone.Indoor, 420, NeedKind.Play, "딸기", w: 2);
            I("bell_ball", "방울 공", ItemCategory.Toy, Zone.Indoor, 180, NeedKind.Play, "딸기");
            I("catnip_fish", "캣닢 생선 인형", ItemCategory.Toy, Zone.Indoor, 240, NeedKind.Play, "바다");
            I("feather_stand", "깃털 오뚝이", ItemCategory.Toy, Zone.Indoor, 300, NeedKind.Play, "숲");
            // 가구
            I("cushion", "방석", ItemCategory.Furniture, Zone.Indoor, 300, NeedKind.Rest, "딸기", 2, 2);
            I("hideout", "숨숨집", ItemCategory.Furniture, Zone.Indoor, 520, NeedKind.Rest, "숲", 2, 2);
            I("litter_box", "화장실", ItemCategory.Furniture, Zone.Indoor, 360, NeedKind.Clean, "기본", 2, 2);
            I("scratcher", "스크래처", ItemCategory.Furniture, Zone.Indoor, 240, NeedKind.Play, "기본", 1, 2);
            I("basket_bed", "바구니 침대", ItemCategory.Furniture, Zone.Indoor, 460, NeedKind.Rest, "숲", 2, 2);
            I("hammock_stand", "해먹 의자", ItemCategory.Furniture, Zone.Indoor, 620, NeedKind.Rest, "바다", 2, 2);
            I("window_box", "창가 상자", ItemCategory.Furniture, Zone.Indoor, 380, NeedKind.Rest, "기본", 2, 1);
            I("rug_round", "둥근 러그", ItemCategory.Deco, Zone.Indoor, 280, theme: "딸기", w: 3, h: 3);
            I("rug_wave", "물결 러그", ItemCategory.Deco, Zone.Indoor, 280, theme: "바다", w: 3, h: 3);
            I("floor_lamp", "버섯 스탠드", ItemCategory.Deco, Zone.Indoor, 320, theme: "숲");
            I("bookshelf_low", "낮은 책장", ItemCategory.Deco, Zone.Indoor, 400, theme: "기본", w: 2);
            I("plant_pot", "몬스테라 화분", ItemCategory.Deco, Zone.Indoor, 260, theme: "숲");
            I("cat_clock", "고양이 시계", ItemCategory.Deco, Zone.Indoor, 300, theme: "기본");
            I("shell_lamp", "조개 등", ItemCategory.Deco, Zone.Indoor, 340, theme: "바다");
            I("fish_mobile", "물고기 모빌", ItemCategory.Deco, Zone.Indoor, 260, theme: "바다");
            I("teacup_bed", "찻잔 침대", ItemCategory.Furniture, Zone.Indoor, 540, NeedKind.Rest, "딸기", 2, 2);
            // 캣타워 (방치 보상 +10~30%)
            I("cat_tower_1", "캣타워 1단", ItemCategory.Tower, Zone.Indoor, 600, NeedKind.Play, "기본", 2, 2, idle: .1f);
            I("tower_stool", "캣타워 스툴형", ItemCategory.Tower, Zone.Indoor, 700, NeedKind.Rest, "딸기", 2, 2, idle: .1f);
            I("tower_stairs", "캣타워 계단형", ItemCategory.Tower, Zone.Indoor, 1200, NeedKind.Play, "기본", 3, 2, idle: .15f);
            I("tower_house", "캣타워 하우스형", ItemCategory.Tower, Zone.Indoor, 1800, NeedKind.Rest, "숲", 3, 2, idle: .2f);
            I("tower_tree", "캣타워 나무형", ItemCategory.Tower, Zone.Indoor, 1800, NeedKind.Play, "숲", 3, 2, idle: .2f);
            I("tower_tall", "캣타워 높은 탑", ItemCategory.Tower, Zone.Indoor, 3000, NeedKind.Play, "기본", 3, 2, idle: .3f);
            // 마당
            I("garden_bed", "텃밭 상자", ItemCategory.Outdoor, Zone.Yard, 400, theme: "숲", w: 2);
            I("bench", "나무 벤치", ItemCategory.Outdoor, Zone.Yard, 450, NeedKind.Rest, "숲", 2);
            I("mailbox", "우체통", ItemCategory.Outdoor, Zone.Yard, 300, theme: "기본");
            I("garden_lantern", "정원 등불", ItemCategory.Outdoor, Zone.Yard, 280, theme: "숲");
            I("bird_feeder", "새 모이통", ItemCategory.Outdoor, Zone.Yard, 350, NeedKind.Play, "숲");
            I("pond", "작은 연못", ItemCategory.Outdoor, Zone.Yard, 900, NeedKind.Thirst, "바다", 2, 2);
            I("stepping_stones", "징검돌", ItemCategory.Outdoor, Zone.Yard, 200, theme: "숲", w: 2);
            I("picket_fence", "나무 울타리", ItemCategory.Outdoor, Zone.Yard, 160, theme: "기본", w: 2);
            I("parasol", "파라솔", ItemCategory.Outdoor, Zone.Yard, 520, NeedKind.Rest, "바다", 2, 2);
            I("picnic_mat", "소풍 돗자리", ItemCategory.Outdoor, Zone.Yard, 360, NeedKind.Rest, "딸기", 2, 2);
            I("flower_pot_row", "꽃 화분 줄", ItemCategory.Outdoor, Zone.Yard, 240, theme: "딸기", w: 2);
            I("sandbox", "모래 놀이터", ItemCategory.Outdoor, Zone.Yard, 600, NeedKind.Clean, "바다", 2, 2);
            I("swing_basket", "그네 바구니", ItemCategory.Outdoor, Zone.Yard, 780, NeedKind.Rest, "숲", 2, 2);
            I("watering_can", "물뿌리개", ItemCategory.Outdoor, Zone.Yard, 120, theme: "기본");
            // 계절 한정 (젤리, 계절 행사 기간에만 상점에)
            I("sakura_tower", "벚꽃 캣타워", ItemCategory.Tower, Zone.Indoor, 300, NeedKind.Play, "벚꽃", 2, 2, cur: Currency.Jelly, idle: .2f, limited: true, season: "sakura");
            I("petal_cushion", "꽃잎 방석", ItemCategory.Furniture, Zone.Indoor, 150, NeedKind.Rest, "벚꽃", 2, 2, cur: Currency.Jelly, limited: true, season: "sakura");
            I("melon_cushion", "수박 방석", ItemCategory.Furniture, Zone.Indoor, 150, NeedKind.Rest, "여름", 2, 2, cur: Currency.Jelly, limited: true, season: "summer");
            I("pumpkin_house", "호박 숨숨집", ItemCategory.Furniture, Zone.Indoor, 250, NeedKind.Rest, "할로윈", 2, 2, cur: Currency.Jelly, limited: true, season: "halloween");
            I("snow_tree", "눈 트리", ItemCategory.Deco, Zone.Yard, 250, theme: "겨울", w: 2, h: 2, cur: Currency.Jelly, limited: true, season: "winter");
            I("hanok_hideout", "한옥 숨숨집", ItemCategory.Furniture, Zone.Indoor, 250, NeedKind.Rest, "가을", 2, 2, cur: Currency.Jelly, limited: true, season: "catday_kr");
            return L.ToArray();
        }

        /// <summary>별나라 꾸미기 (별나라에만 놓인다. 무료로도 충분히 꾸밀 수 있게 기본 4종은 무료).</summary>
        public static readonly (string id, string ko, bool free)[] StarItems =
        {
            ("star_cloud_bed", "구름 침대", true), ("star_lamp", "별빛 등", true), ("star_flowers", "달맞이꽃", true), ("star_bench", "구름 벤치", true),
            ("star_lantern", "별나라 등불", false), ("star_cloud_garden", "구름 정원", false),
        };

        public static readonly Dictionary<string, ItemDef> ItemById = Items.ToDictionary(i => i.id);
        public static ItemDef Item(string id) => ItemById.TryGetValue(id, out var d) ? d : null;

        // ---------------------------------------------------------------- 재료·텃밭·만들기
        public static readonly string[] Materials = { "wood", "stone", "flower", "yarn", "shell", "catnip", "wheatgrass", "feather" };
        public static readonly Dictionary<string, string> MaterialKo = new Dictionary<string, string>
        { ["wood"] = "나뭇가지", ["stone"] = "조약돌", ["flower"] = "꽃", ["yarn"] = "털실", ["shell"] = "조개껍데기", ["catnip"] = "캣닢", ["wheatgrass"] = "밀싹", ["feather"] = "깃털" };

        public static readonly SeedDef[] Seeds =
        {
            new SeedDef { id = "catnip_seed", ko = "캣닢 씨앗", yields = "catnip", hours = 4, price = 40, amount = 3 },
            new SeedDef { id = "wheat_seed", ko = "밀싹 씨앗", yields = "wheatgrass", hours = 6, price = 50, amount = 3 },
            new SeedDef { id = "flower_seed", ko = "꽃씨", yields = "flower", hours = 12, price = 60, amount = 4 },
        };
        public const int GardenPlots = 3;

        public static readonly RecipeDef[] Recipes =
        {
            R("r_yarn_basket", "yarn_basket", ("yarn", 3), ("wood", 2)),
            R("r_catnip_fish", "catnip_fish", ("catnip", 3), ("yarn", 1)),
            R("r_feather_stand", "feather_stand", ("feather", 3), ("wood", 2)),
            R("r_shell_lamp", "shell_lamp", ("shell", 4), ("stone", 1)),
            R("r_flower_pot_row", "flower_pot_row", ("flower", 4), ("stone", 2)),
            R("r_bird_feeder", "bird_feeder", ("wood", 4), ("wheatgrass", 2)),
            R("r_stepping_stones", "stepping_stones", ("stone", 5)),
            R("r_basket_bed", "basket_bed", ("wood", 4), ("yarn", 3)),
        };
        static RecipeDef R(string id, string item, params (string m, int n)[] needs) => new RecipeDef { id = id, item = item, needs = needs.Select(x => x.m).ToArray(), counts = needs.Select(x => x.n).ToArray() };
        public static readonly string[] StarterRecipes = { "r_stepping_stones" };

        // ---------------------------------------------------------------- 산책
        public static readonly int[] WalkHours = { 1, 2, 4, 8 };
        /// <summary>산책에서 물어 오는 재료 (바깥 재료가 잦다).</summary>
        public static readonly (string id, int w)[] WalkMaterials = { ("wood", 3), ("stone", 3), ("flower", 2), ("shell", 2), ("feather", 1), ("yarn", 1) };
        public static string PickWalkMaterial(System.Random r) { int t = WalkMaterials.Sum(m => m.w), x = r.Next(t); foreach (var m in WalkMaterials) { if (x < m.w) return m.id; x -= m.w; } return "wood"; }
        /// <summary>산책 선물: 시간이 길수록 많다. (코인, 재료 개수, 희귀 용품 확률)</summary>
        public static (int coins, int materials, double rareChance) WalkReward(int hours) => (30 * hours, 1 + hours / 2, hours >= 8 ? .12 : hours >= 4 ? .05 : .0);
        public static readonly string[] WalkRareItems = { "catnip_fish", "shell_lamp", "feather_stand", "bell_ball" };

        // ---------------------------------------------------------------- 하루·주간
        public static readonly TaskDef[] DailyTasks =
        {
            T("pet3", "고양이 3번 쓰다듬기", "pet", 3), T("feed2", "밥 2번 주기", "feed", 2), T("play2", "장난감으로 2번 놀기", "play", 2),
            T("photo1", "사진 1장 찍기", "photo", 1), T("guest1", "손님 고양이에게 간식 주기", "guestTreat", 1), T("walk1", "산책 보내기", "walk", 1),
            T("plant1", "텃밭에 씨앗 심기", "plant", 1), T("place1", "용품 하나 놓기", "place", 1), T("collect1", "방치 보상 받기", "collect", 1),
            T("water1", "물 채워 주기", "water", 1), T("craft1", "만들기 1번", "craft", 1), T("clean1", "화장실 치우기", "clean", 1),
            T("tile3", "데크 타일 3장 깔기", "tile", 3),
        };
        static TaskDef T(string id, string ko, string counter, int goal) => new TaskDef { id = id, ko = ko, counter = counter, goal = goal, jelly = 2, coins = 60 };
        public const int DailyTaskCount = 3, DailyAllDoneJelly = 4;   // 하루 3개 다 하면 +4 젤리 (하나에 2 + 모두 4 = 10/일)
        public const int WeeklyGoal = 15;
        public static readonly int[] AttendanceJelly = { 2, 2, 3, 3, 4, 4, 0 };     // 7일째는 상자
        public const int AttendanceDay7Jelly = 20;
        public static readonly string[] AttendanceDay7Items = { "bell_ball", "catnip_fish", "plant_pot", "cat_clock" };

        // ---------------------------------------------------------------- 손님 고양이
        public const int GuestTreatCoins = 50, AdoptAfterMeetings = 3;
        public static int BreedWeight(BreedDef b) => b.rarity == 1 ? 6 : b.rarity == 2 ? 3 : 1;

        // ---------------------------------------------------------------- 계절 행사
        public static readonly SeasonDef[] Seasons =
        {
            S("seollal", "설날", 1, 20, 2, 5, "snow_tree"), S("catday_jp", "고양이의 날 (2월 22일)", 2, 18, 2, 28, "petal_cushion"),
            S("sakura", "벚꽃", 3, 25, 4, 20, "sakura_tower", "petal_cushion"), S("summer", "여름", 7, 1, 8, 15, "melon_cushion"),
            S("catday_kr", "한국 고양이의 날 (9월 9일)", 9, 3, 9, 15, "hanok_hideout"), S("halloween", "할로윈", 10, 18, 10, 31, "pumpkin_house"),
            S("winter", "겨울", 12, 10, 12, 31, "snow_tree"),
        };
        static SeasonDef S(string id, string ko, int fm, int fd, int tm, int td, params string[] rewards) => new SeasonDef { id = id, ko = ko, fromMonth = fm, fromDay = fd, toMonth = tm, toDay = td, rewards = rewards };
        public const int SeasonStampGoal = 10;
        public static SeasonDef SeasonAt(DateTime local)
        {
            int key = local.Month * 100 + local.Day;
            return Seasons.FirstOrDefault(s => key >= s.fromMonth * 100 + s.fromDay && key <= s.toMonth * 100 + s.toDay);
        }

        // ---------------------------------------------------------------- 결제 상품 (젤리 묶음, 계절 세트, 별나라 꾸미기뿐)
        public static readonly ProductDef[] Products =
        {
            P("jelly_60", "젤리 60개", 1200, 60, "jelly"), P("jelly_330", "젤리 330개", 5900, 330, "jelly"), P("jelly_700", "젤리 700개", 11000, 700, "jelly"),
            P("jelly_1500", "젤리 1500개", 22000, 1500, "jelly"), P("jelly_4200", "젤리 4200개", 59000, 4200, "jelly"),
            P("season_sakura", "벚꽃 세트", 9900, 0, "season", "sakura_tower", "petal_cushion"), P("season_summer", "여름 세트", 9900, 0, "season", "melon_cushion"),
            P("season_halloween", "할로윈 세트", 9900, 0, "season", "pumpkin_house"), P("season_winter", "겨울 세트", 9900, 0, "season", "snow_tree"),
            P("star_lanterns", "별나라 등불 꾸미기", 3900, 0, "star", "star_lantern"), P("star_garden", "별나라 구름 정원", 5900, 0, "star", "star_cloud_garden"),
        };
        static ProductDef P(string id, string ko, int krw, int jelly, string kind, params string[] items) => new ProductDef { id = "com.nolgoseom." + id, ko = ko, priceKrw = krw, jelly = jelly, kind = kind, items = items };

        // ---------------------------------------------------------------- 광고 (보상형 중심, 배너 없음, 별나라 없음)
        public enum AdSpot { IdleDouble, FreeFood, FreeBox, Gift, WalkSkip }
        public static int AdDailyCap(AdSpot s) => s switch { AdSpot.IdleDouble => 3, AdSpot.FreeFood => 2, AdSpot.FreeBox => 3, AdSpot.Gift => 2, AdSpot.WalkSkip => 2, _ => 0 };
        public const int AdGiftJellyPerDay = 1;           // 광고 선물 중 하루 1회만 젤리
        public const int InterstitialMinSeconds = 240, InterstitialDailyCap = 6, InterstitialFreeDays = 3;

        // 무료 뽑기 상자 확률 (상점에 모두 공개: 게임산업진흥법)
        public static readonly (string item, double p)[] FreeBoxOdds =
        {
            ("can_food", .30), ("churu", .25), ("kibble_bag", .20), ("bell_ball", .10), ("catnip_fish", .07), ("plant_pot", .05), ("cat_clock", .03),
        };

        // ---------------------------------------------------------------- 호감도 10단계
        public static readonly int[] AffectionLevels = { 0, 30, 80, 150, 240, 350, 480, 640, 830, 1050 };
        public static int AffectionLevel(float points) { int lv = 1; for (int i = 0; i < AffectionLevels.Length; i++) if (points >= AffectionLevels[i]) lv = i + 1; return lv; }

        // ---------------------------------------------------------------- 성격
        public static string[] FavoriteItems(Personality p) => p switch
        {
            Personality.Easygoing => new[] { "cushion", "hideout", "basket_bed" },
            Personality.Playful => new[] { "mouse_toy", "wand_toy", "ball", "tunnel" },
            Personality.Aloof => new[] { "cat_tower_1", "tower_tall", "tower_tree", "window_box" },
            Personality.Foodie => new[] { "churu", "can_food", "treat_jar", "fish_plate" },
            Personality.Shy => new[] { "hideout", "paper_box", "tunnel" },
            _ => new string[0],
        };
        public static string Ko(Personality p) => p switch { Personality.Easygoing => "느긋함", Personality.Playful => "장난꾸러기", Personality.Aloof => "새침함", Personality.Foodie => "먹보", _ => "겁쟁이" };

        public static string BreedKo(string id) => Breeds.FirstOrDefault(b => b.id == id)?.ko ?? id;
    }
}
