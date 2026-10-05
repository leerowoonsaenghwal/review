using System;
using System.Collections.Generic;

namespace CatIsland.Game
{
    /// <summary>시간: 실제 시계와 테스트용 가상 시계. 날짜(하루 할 일·출석·계절)는 기기 지역 시간 기준.</summary>
    public interface IClock { DateTime UtcNow { get; } TimeSpan LocalOffset { get; } }
    public class RealClock : IClock { public DateTime UtcNow => DateTime.UtcNow; public TimeSpan LocalOffset => TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow); }
    public class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; }
        public TimeSpan LocalOffset { get; set; } = TimeSpan.FromHours(9);   // (한국)
        public FakeClock(DateTime utc) { UtcNow = utc; }
        public void Advance(TimeSpan d) => UtcNow += d;
    }

    public static class TimeUtil
    {
        public static long ToUnix(DateTime utc) => (long)(utc - DateTime.UnixEpoch).TotalSeconds;
        public static DateTime FromUnix(long s) => DateTime.UnixEpoch.AddSeconds(s);
        public static string DayKey(DateTime utc, TimeSpan offset) => (utc + offset).ToString("yyyy-MM-dd");
        /// <summary>주 번호 (월요일 시작): 주간 목표용.</summary>
        public static string WeekKey(DateTime utc, TimeSpan offset)
        {
            var d = (utc + offset).Date; int dow = ((int)d.DayOfWeek + 6) % 7;
            return d.AddDays(-dow).ToString("yyyy-MM-dd");
        }
    }

    // ---------------------------------------------------------------- 저장 데이터 (JsonUtility 로 저장: 필드만, Dictionary 대신 List)
    [Serializable] public class Count { public string id; public int n; public Count() { } public Count(string id, int n) { this.id = id; this.n = n; } }

    [Serializable]
    public class CatData
    {
        public string uid, name, nickname, breed;
        public string eyeStyle = "", whiskerStyle = "";          // 비어 있으면 품종·털 기본
        public string coatJson = "";                             // 사진으로 만든 고양이의 털 (photo2cat 결과). 비어 있으면 품종 그대로
        public Personality personality;
        public float hunger = .8f, thirst = .8f, play = .8f, clean = .9f;
        public float affection;
        public long adoptedAt, lastPetAt;
        public string lastFirstPetDay = "";
        public string status = "home";                          // home / walk / star
        public long walkEndsAt; public int walkHours;
        public long starredAt;                                   // 별나라로 떠난 날 (이용자가 정함)
        public List<string> firsts = new List<string>();         // 처음 한 일 (성장 기록)
        public float Mood => (hunger + thirst + play + clean) / 4f;
        public int Level => Catalog.AffectionLevel(affection);
        public bool Happy => hunger > .6f && thirst > .6f && play > .6f && clean > .6f;
    }

    [Serializable] public class Placement { public string item; public Zone zone; public int x, z, rot; public long placedAt; }
    [Serializable] public class TaskState { public string id; public int progress; public bool claimed; }
    [Serializable] public class Plot { public string seed = ""; public long plantedAt; }
    [Serializable] public class StarCard { public string catUid, memo, favorite; public long createdAt; public List<string> letters = new List<string>(); public List<string> decor = new List<string>(); }
    [Serializable] public class PhotoEntry { public string file, catUid, caption; public long takenAt; }

    [Serializable]
    public class GameState
    {
        public int version = 1;
        public long createdAt, savedAt, lastSeenAt, lastIdleAt;   // lastSeenAt: 지금까지 본 가장 늦은 시각 (시계 되돌리기 방지)
        public long saveCounter;                                  // (기기 저장과 iCloud 중 더 앞선 것을 고르는 기준)
        public int coins = 300, jelly = 10;
        public List<Count> materials = new List<Count>();
        public List<Count> inventory = new List<Count>();         // 가방 (아직 놓지 않은 용품, 소모품)
        public List<Placement> placed = new List<Placement>();
        public List<CatData> cats = new List<CatData>();
        public int catSlots = Catalog.FirstCatSlots;
        public List<int> zonesUnlocked = new List<int> { 0 };
        public long idleBank;                                     // (받지 않은 방치 보상 코인)
        public double idleHours;                                  // (받지 않은 방치 보상이 쌓인 시간: 8시간까지)

        // 하루·주간·출석
        public string dayKey = "", weekKey = "";
        public List<TaskState> tasks = new List<TaskState>();
        public bool allTasksClaimed;
        public int weeklyDone; public bool weeklyClaimed;
        public List<Count> counters = new List<Count>();          // 오늘의 행동 수 (pet, feed …)
        public string lastAttendDay = ""; public int attendIndex;   // 0..6, 7일째 상자
        // 광고·결제
        public List<Count> adCounts = new List<Count>();          // 오늘 광고 횟수 (자리별)
        public int adGiftJellyToday;
        public long lastInterstitialAt; public int interstitialsToday;
        public bool payer;
        public List<string> purchases = new List<string>();       // 비소모 상품 (계절 세트, 별나라)
        public List<string> pendingTransactions = new List<string>();   // (결제는 됐는데 지급 전에 앱이 꺼진 경우: 다음 실행에 지급)
        // 손님 고양이
        public string guestDay = "", guestBreed = ""; public bool guestTreated; public bool guestGone;
        public List<Count> guestMeetings = new List<Count>();     // 품종별 만난(간식 준) 횟수
        public List<string> dex = new List<string>();             // 도감에 오른 품종
        // 산책·텃밭·만들기
        public List<Plot> plots = new List<Plot>();
        public List<string> recipes = new List<string>();
        public List<string> unlockedItems = new List<string>();   // 만들기·선물로 얻어 상점에 열린 용품
        // 계절 행사
        public string seasonId = ""; public int seasonStamps; public bool seasonClaimed;
        // 처음 7일
        public int onboardingStep; public string onboardingDay = "";
        // 설정·기록
        public bool notifyIdleFull = true, notifyWalkHome = true, notifyAnniversary;
        public bool soundOn = true, musicOn = true, hapticsOn = true;
        public List<PhotoEntry> photos = new List<PhotoEntry>();
        public List<StarCard> stars = new List<StarCard>();
        public List<string> achievements = new List<string>();
        public int totalPets, totalMeals, totalJumps;
        public int clockRollbacks;                               // (감지 횟수: 기록만, 벌은 없다)
    }

    public static class Bag
    {
        public static int Get(List<Count> l, string id) { foreach (var c in l) if (c.id == id) return c.n; return 0; }
        public static void Add(List<Count> l, string id, int n)
        {
            foreach (var c in l) if (c.id == id) { c.n = Math.Max(0, c.n + n); if (c.n == 0) l.Remove(c); return; }
            if (n > 0) l.Add(new Count(id, n));
        }
    }
}
