using UnityEngine;

namespace CatIsland
{
    public enum PetZone { None, Forehead, Cheek, Chin, Back, Butt, Belly }

    public enum ZonePref { Loved, Liked, Neutral, Disliked }

    public struct PetFrame
    {
        public bool PurrStarted;
        public bool PurrStopped;
        public bool Nipped;
        public bool BellyUp;
        public float AffectionGained;
        /// <summary>이번 프레임에 실제로 기분 좋게 쓰다듬어졌는지 (반응 연출용).</summary>
        public bool Enjoying;
    }

    /// <summary>
    /// 쓰다듬기 규칙. 화면과 무관한 순수 로직이라 테스트로 검증한다.
    /// - 문질러야 한다 (가만히 누르기는 쓰다듬기가 아님)
    /// - 부드럽게 문지를수록 좋다 (너무 빠르면 효과가 떨어짐)
    /// - 고양이마다 좋아하는 곳이 다르다
    /// - 배는 싫어한다. 오래 만지면 살짝 깨문다. 단, 발라당 누운 직후에는 허락한다
    /// </summary>
    public class PetLogic
    {
        readonly ZonePref[] prefs = new ZonePref[7];

        public float Pleasure { get; private set; }
        public bool Purring { get; private set; }
        public float TrustLeft { get; private set; }
        public float NipCooldownLeft { get; private set; }
        public float BellyContact { get; private set; }
        float maxHold;

        public bool Trusting => TrustLeft > 0f;

        public PetLogic(ZonePref forehead, ZonePref cheek, ZonePref chin, ZonePref back, ZonePref butt)
        {
            prefs[(int)PetZone.None] = ZonePref.Neutral;
            prefs[(int)PetZone.Forehead] = forehead;
            prefs[(int)PetZone.Cheek] = cheek;
            prefs[(int)PetZone.Chin] = chin;
            prefs[(int)PetZone.Back] = back;
            prefs[(int)PetZone.Butt] = butt;
            prefs[(int)PetZone.Belly] = ZonePref.Disliked;
        }

        public ZonePref PrefFor(PetZone zone)
        {
            if (zone == PetZone.Belly && Trusting) return ZonePref.Loved;
            return prefs[(int)zone];
        }

        /// <summary>문지르는 속도(화면 높이/초)에 따른 손맛 0~1.</summary>
        public static float StrokeQuality(float speed)
        {
            if (speed < GameConfig.StrokeSpeedMin) return 0f;
            if (speed < GameConfig.StrokeSpeedBest)
                return Mathf.SmoothStep(0.25f, 1f, Mathf.InverseLerp(GameConfig.StrokeSpeedMin, GameConfig.StrokeSpeedBest, speed));
            float rough = Mathf.InverseLerp(GameConfig.StrokeSpeedBest * 2f, GameConfig.StrokeSpeedRough, speed);
            return Mathf.Lerp(1f, 0.3f, rough);
        }

        static float BaseGain(ZonePref p)
        {
            switch (p)
            {
                case ZonePref.Loved: return GameConfig.GainLoved;
                case ZonePref.Liked: return GameConfig.GainLiked;
                case ZonePref.Neutral: return GameConfig.GainNeutral;
                default: return 0f;
            }
        }

        public PetFrame Update(PetZone zone, float strokeSpeed, float dt)
        {
            var f = new PetFrame();
            TrustLeft = Mathf.Max(0f, TrustLeft - dt);
            NipCooldownLeft = Mathf.Max(0f, NipCooldownLeft - dt);

            float quality = zone == PetZone.None ? 0f : StrokeQuality(strokeSpeed);
            bool active = quality > 0f;

            if (!active)
            {
                Pleasure -= GameConfig.PleasureDecayPerSec * dt;
                BellyContact = Mathf.Max(0f, BellyContact - dt * 1.5f);
                maxHold = Mathf.Max(0f, maxHold - dt * 2f);
            }
            else
            {
                ZonePref pref = PrefFor(zone);
                if (pref == ZonePref.Disliked)
                {
                    BellyContact += dt;
                    Pleasure -= GameConfig.PleasureDecayPerSec * 0.3f * dt;
                    maxHold = 0f;
                    if (BellyContact >= GameConfig.BellyTolerance && NipCooldownLeft <= 0f)
                    {
                        f.Nipped = true;
                        Pleasure *= 0.5f;
                        BellyContact = 0f;
                        NipCooldownLeft = GameConfig.NipCooldown;
                    }
                }
                else
                {
                    // 배와 가슴을 오가며 문질러도 배를 만진 시간은 천천히만 줄어든다
                    BellyContact = Mathf.Max(0f, BellyContact - dt * 0.25f);
                    float gain = BaseGain(pref) * quality * (NipCooldownLeft > 0f ? 0.35f : 1f);
                    Pleasure += gain * dt;
                    f.AffectionGained = gain * dt * GameConfig.AffectionPerPleasure;
                    f.Enjoying = gain > 0f;

                    if (Pleasure >= 0.97f) maxHold += dt;
                    if (maxHold >= GameConfig.BellyUpHold && !Trusting)
                    {
                        f.BellyUp = true;
                        TrustLeft = GameConfig.TrustWindow;
                        maxHold = 0f;
                    }
                }
            }

            Pleasure = Mathf.Clamp01(Pleasure);

            if (!Purring && Pleasure >= GameConfig.PurrOnPleasure) { Purring = true; f.PurrStarted = true; }
            else if (Purring && Pleasure <= GameConfig.PurrOffPleasure) { Purring = false; f.PurrStopped = true; }
            return f;
        }
    }

    public class Affection
    {
        public float Points { get; private set; }

        public Affection(float points = 0f) { Points = Mathf.Max(0f, points); }

        public int Level
        {
            get
            {
                var t = GameConfig.AffectionLevelThresholds;
                int lv = 1;
                for (int i = 1; i < t.Length; i++) if (Points >= t[i]) lv = i + 1;
                return lv;
            }
        }

        /// <summary>다음 레벨까지 진행도 0~1 (10레벨이면 1).</summary>
        public float Progress
        {
            get
            {
                var t = GameConfig.AffectionLevelThresholds;
                int lv = Level;
                if (lv >= t.Length) return 1f;
                return Mathf.InverseLerp(t[lv - 1], t[lv], Points);
            }
        }

        /// <returns>레벨이 올랐으면 true</returns>
        public bool Add(float pts)
        {
            if (pts <= 0f) return false;
            int before = Level;
            Points += pts;
            return Level > before;
        }
    }
}
