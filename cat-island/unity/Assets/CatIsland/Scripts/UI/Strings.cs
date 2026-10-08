namespace CatIsland.UI
{
    /// <summary>
    /// 화면 문구 (BRAND.md 10장 말투: 친구를 부르듯 해요체, 짧게, 재촉·죄책감 없음, 정직하게, 별나라는 조용히).
    /// 한 곳에 모아 두어 나중에 일본어·영어로 바꿔 끼운다.
    /// </summary>
    public static class Str
    {
        public const string AppName = "놀러와요 고양이섬";
        public const string Shop = "상점", Decorate = "꾸미기", Tasks = "할 일", Cats = "고양이", Photo = "사진";
        public const string Close = "닫기", Buy = "사기", Place = "놓기", PutAway = "넣기", Rotate = "돌리기", Done = "다 했어요", Claim = "받기", Ok = "좋아요", Cancel = "괜찮아요";
        public const string Welcome = "왔구나!";
        public static string IdleReady(string cat, int coins) => $"{UI.J.IGa(cat)} 코인 {coins}개를 모아 뒀어요.";
        public const string IdleTitle = "모아 둔 선물", IdleTake = "받을게요", IdleDouble = "광고 보고 두 배로";
        public const string AdHonest = "짧은 광고를 보면 선물이 두 배가 돼요.";
        public const string AdLater = "지금은 광고를 불러오지 못했어요. 조금 있다가 다시 해 볼까요?";
        public const string NotEnoughCoins = "코인이 조금 모자라요. 고양이들이 모아 줄 거예요.";
        public const string NotEnoughJelly = "젤리가 조금 모자라요.";
        public const string Oops = "앗, 잠깐 길을 잃었어요. 다시 해 볼까요?";
        public const string GuestTitle = "손님 고양이가 놀러 왔어요";
        public static string GuestBody(string breed, int met) => met == 0 ? $"{breed} 손님이에요. 간식을 줄까요?" : $"{breed} 손님이 또 왔어요. ({met}번 만났어요)";
        public const string GuestTreat = "간식 주기", GuestAdopt = "우리 섬에서 같이 살래요?";
        public const string TasksToday = "오늘 할 일", Weekly = "이번 주 목표", Attendance = "출석 도장", Season = "계절 도장판";
        public const string AttendStamp = "도장 찍기";
        public const string Walk = "산책", Garden = "텃밭", Craft = "만들기", Dex = "도감";
        public static string WalkHours(int h) => $"{h}시간";
        public static string WalkBack(string cat) => $"산책 간 {UI.J.IGa(cat)} 돌아왔어요. 뭘 물고 왔을까요?";
        public const string Settings = "설정", Sound = "효과음", Music = "음악", Haptics = "진동", Notify = "알림", Cloud = "iCloud 저장", Restore = "구매 복원", Privacy = "개인정보 처리방침", Credits = "만든 사람들";
        /// <summary>고양이 소리 출처 (assets/sounds/cat/SOURCES.md, CC BY 4.0 은 작성자 표기 필요).</summary>
        public const string SoundCredits = "고양이 소리: 위키미디어 공용 녹음을 다시 합성 — PantheraLeo1359531 (CC BY 4.0), Heismark · Insanejeff (퍼블릭 도메인), Tsester (CC0)";
        public const string PhotoPrivacy = "사진은 집사님 휴대폰 안에서만 써요. 밖으로 나가지 않아요.";
        public const string OddsHonest = "이 상자의 확률은 여기서 모두 볼 수 있어요.";
        public const string JellyShop = "젤리 상점";
        public const string StarLand = "별나라";
        public static string StarRest(string cat) => $"{UI.J.IGa(cat)} 별나라에서 편히 쉬고 있어요";
        public const string StarIntro = "별나라는 별이 된 고양이를 기억하는 곳이에요. 원할 때만 찾아와요.";
        public const string Yard = "마당", Indoor = "집 안";
        public static string YardLocked(int coins) => $"마당을 열려면 코인 {coins}개와 재료가 필요해요";
        public const string MakeCat = "고양이 만들기", FromPhoto = "사진으로", FromBreed = "품종 고르기";
        public const string EyeStyle = "눈", Whisker = "수염", Name = "이름";
        public static readonly string[] EyeNames = { "진한 눈", "색 눈", "테두리 눈" };
        public static readonly string[] WhiskerNames = { "짧은 수염", "긴 수염", "수염 점" };
        public static string Price(int n, bool jelly) => jelly ? $"젤리 {n}" : $"{n}";
        public static string Krw(int won) => $"{won:N0}원";

        // 처음 7일 안내 (고양이 말풍선 아래 한 줄)
        public static string Hint(string key) => key switch
        {
            "hint_make_cat" => "첫 고양이를 만나 볼까요?", "hint_feed" => "밥그릇을 눌러 밥을 줘요", "hint_pet" => "고양이를 살살 문질러 쓰다듬어요",
            "hint_cushion" => "방석을 놓아 주면 낮잠을 자요", "hint_guest" => "손님 고양이가 기다려요", "hint_tower" => "캣타워를 놓으면 점프해요",
            "hint_adopt" => "손님 고양이를 세 번 만나면 같이 살 수 있어요", "hint_yard" => "재료를 모으면 마당이 열려요", "hint_photo" => "예쁜 순간을 사진으로 남겨요", _ => "",
        };
    }

    /// <summary>받침 조사 (Game.Josa 와 같다: UI 어셈블리에서 바로 쓰려고).</summary>
    public static class J
    {
        public static string IGa(string w) => CatIsland.Game.Josa.IGa(w);
        public static string EulReul(string w) => CatIsland.Game.Josa.EulReul(w);
    }
}
