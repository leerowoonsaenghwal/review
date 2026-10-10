namespace CatIsland
{
    /// <summary>
    /// 손맛 시제품 튜닝 값. 실제 게임은 상태가 8~24시간에 걸쳐 줄지만,
    /// 시제품은 2분 체험 안에 밥 → 낮잠 → 다시 배고픔 한 바퀴가 보이도록 압축했다.
    /// </summary>
    public static class GameConfig
    {
        // ---- 상태 (0 = 바닥, 1 = 가득) ----
        public const float StartHunger = 0.32f;        // 시작하자마자 곧 배고파지게
        public const float StartEnergy = 0.75f;
        public const float HungerDrainPerSec = 1f / 150f;   // 가득 → 바닥 150초
        public const float EnergyDrainPerSec = 1f / 110f;
        public const float HungryThreshold = 0.25f;    // 이 아래면 생선 말풍선
        public const float SleepyThreshold = 0.35f;
        public const float EatGainPerSec = 0.22f;
        public const float SleepGainPerSec = 0.045f;   // 약 20초 낮잠
        public const float WakeEnergy = 0.95f;

        // ---- 쓰다듬기 ----
        public const float PleasureDecayPerSec = 0.22f;
        public const float GainLoved = 0.55f;
        public const float GainLiked = 0.42f;
        public const float GainNeutral = 0.24f;
        public const float StrokeSpeedMin = 0.04f;     // 화면 높이 기준 /초. 이보다 느리면 문지르는 게 아님
        public const float StrokeSpeedBest = 0.55f;    // 이 근처가 가장 기분 좋음
        public const float StrokeSpeedRough = 2.2f;    // 이보다 빠르면 거칠다
        public const float PurrOnPleasure = 0.15f;
        public const float PurrOffPleasure = 0.08f;
        public const float BellyTolerance = 1.1f;      // 신뢰 전 배를 이만큼 문지르면 살짝 깨묾
        public const float NipCooldown = 2.5f;
        public const float BellyUpHold = 1.6f;         // 기쁨 최대치를 이만큼 유지하면 발라당
        public const float TrustWindow = 7f;           // 발라당 뒤 배를 만져도 되는 시간
        public const float FirstPetOfDayBonus = 25f;

        // ---- 호감도 ----
        public const float AffectionPerPleasure = 9f;  // 기쁨 1 상승당 호감 포인트
        public const float AffectionPerMeal = 6f;
        public static readonly float[] AffectionLevelThresholds =
            { 0f, 20f, 50f, 95f, 155f, 230f, 320f, 430f, 560f, 720f };

        // ---- 이동 (클립의 실제 속도: Walk 0.40, Trot 1.06, Gallop 2.27 m/s → 발이 미끄러지지 않는다) ----
        public const float WalkSpeed = 0.4f;
        public const float TrotSpeed = 1.06f;
        public const float RunSpeed = 2.27f;
        public const float TurnSpeedDeg = 160f;
        public const float IslandWalkRadius = 8.3f;   // (섬 반지름 9.6 m, 모래톱 9.2 m 앞까지. 뒤쪽 언덕은 길찾기 장애물이 막는다)
        public const float WanderRadius = 4.2f;   // (가끔 풀밭과 모래밭까지)

        // ---- 입력 ----
        public const float TapMaxDuration = 0.28f;
        public const float TapMaxMove = 0.025f;        // 화면 높이 비율
        public const float IdleInviteDelay = 6f;       // 아무것도 안 하면 고양이가 다가와 쓰다듬어 달라고 함
    }
}
