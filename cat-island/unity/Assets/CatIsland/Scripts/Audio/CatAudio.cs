using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 고양이 소리: 야옹, 반가운 "프릇", 살짝 깨물 때 "먀!", 골골송, 방석 누를 때 "뿅".
    /// 소리 파일 없이 코드로 만든다. 잡음(노이즈)은 쓰지 않는다: 아이폰 스피커에서 "지지직"으로 들린다.
    /// 모든 소리는 최대 크기를 절반 아래로 두어, 겹쳐도 넘쳐서 찌그러지지 않게 한다.
    /// 밥그릇 누를 때는 오르골 같은 "똑똑똑", 먹을 때는 작고 둥근 "냠냠" (둘 다 음정 있는 소리).
    /// </summary>
    public enum Surface { Wood = 0, Rug = 1, Grass = 2, Sand = 3 }

    public class CatAudio : MonoBehaviour
    {
        public const int Rate = 44100;
        public const float Peak = 0.45f;
        /// <summary>설정의 '효과음'을 끄면 고양이 소리(야옹·골골·발소리)도 멈춘다.</summary>
        public static bool Muted;

        AudioSource purrSrc, sfx, stepSrc;
        AudioClip purr, pop, kibble;
        AudioClip[] chirps, nips; AudioClip yawn;
        public bool UsingRecordings { get; private set; }
        public int MeowCount => meows.Length;
        public string LastMeow { get; private set; }
        AudioClip[] noms;
        AudioClip[][] steps;
        int lastMeow = -1, lastStep = -1;
        readonly System.Collections.Generic.List<int> meowBag = new System.Collections.Generic.List<int>();
        public int StepCount { get; private set; }
        public Surface LastStepSurface { get; private set; }
        AudioClip[] meows;
        float purrTarget;
        public float PurrVolume => purrSrc ? purrSrc.volume : 0f;
        public int PlayedCount { get; private set; }

        void Awake()
        {
            purrSrc = gameObject.AddComponent<AudioSource>();
            purrSrc.loop = true;
            purrSrc.playOnAwake = false;
            purrSrc.volume = 0f;
            purrSrc.spatialBlend = 0f;
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
            stepSrc = gameObject.AddComponent<AudioSource>();
            stepSrc.playOnAwake = false;
            stepSrc.spatialBlend = 0f;

            purr = Clip("purr", Synth.Purr());
            // 실제 고양이 녹음이 있으면 그것을 쓴다 (assets/sounds/cat, 출처: SOURCES.md). 없으면 합성 소리
            var real = Resources.LoadAll<AudioClip>("Sounds/cat");
            AudioClip[] Pick(string prefix) => System.Array.FindAll(real, c => c.name.StartsWith(prefix));
            var mv = Synth.MeowVariants();
            meows = new AudioClip[mv.Length];
            for (int i = 0; i < mv.Length; i++) meows[i] = Clip("meow" + i, mv[i]);
            steps = new AudioClip[4][];
            for (int s = 0; s < 4; s++)
            {
                steps[s] = new AudioClip[4];
                for (int v = 0; v < 4; v++) steps[s][v] = Clip($"step{s}_{v}", Synth.Step((Surface)s, v));
            }
            chirps = new[] { Clip("chirp", Synth.Chirp()) };
            yawn = Clip("yawn", Synth.Cry(560f, 820f, 1180f, 0.34f, 0.72f, new[] { Synth.VA, Synth.VI }, 0.006f, 11));   // (하품 끝의 작은 '아이~')
            nips = new[] { Clip("nip", Synth.Meow(820f, 0.2f)) };
            if (Pick("meow_").Length > 0) { meows = Pick("meow_"); UsingRecordings = true; }
            if (Pick("chirp_").Length > 0) chirps = Pick("chirp_");
            if (Pick("nip_").Length > 0) nips = Pick("nip_");
            var realPurr = Pick("purr_");
            if (realPurr.Length > 0) purr = realPurr[0];
            pop = Clip("pop", Synth.Pop());
            kibble = Clip("kibble", Synth.Kibble());
            noms = new[] { Clip("nom1", Synth.Nom(520f)), Clip("nom2", Synth.Nom(600f)), Clip("nom3", Synth.Nom(470f)) };
            purrSrc.clip = purr;
        }

        public void SetPurr(float amount) { purrTarget = Mathf.Clamp01(amount); }

        void Update()
        {
            float v = Mathf.MoveTowards(purrSrc.volume, (Muted ? 0f : purrTarget) * 0.6f, Time.deltaTime * (purrTarget > purrSrc.volume ? 1.0f : 0.5f));
            purrSrc.volume = v;
            if (v > 0.001f && !purrSrc.isPlaying) purrSrc.Play();
            else if (v <= 0.001f && purrSrc.isPlaying) purrSrc.Stop();
        }

        /// <summary>누를 때마다 다른 야옹: 모든 야옹을 섞어 한 바퀴 다 들려준 뒤 다시 섞는다 (몇 개만 돌지 않게, 바퀴가 바뀔 때도 연달아 같은 것 없음).</summary>
        public void Meow()
        {
            if (meowBag.Count == 0)
            {
                for (int k = 0; k < meows.Length; k++) meowBag.Add(k);
                for (int k = meowBag.Count - 1; k > 0; k--) { int j = Random.Range(0, k + 1); (meowBag[k], meowBag[j]) = (meowBag[j], meowBag[k]); }
                if (meowBag.Count > 1 && meowBag[meowBag.Count - 1] == lastMeow) (meowBag[0], meowBag[meowBag.Count - 1]) = (meowBag[meowBag.Count - 1], meowBag[0]);
            }
            int i = meowBag[meowBag.Count - 1]; meowBag.RemoveAt(meowBag.Count - 1);
            lastMeow = i;
            LastMeow = meows[i].name;
            Play(meows[i], 0.85f, Random.Range(0.97f, 1.04f));
        }

        /// <summary>발소리: 바닥 재질별로 네 가지 중 하나 (같은 소리가 연달아 나지 않게).</summary>
        public void Step(Surface surface, float loudness)
        {
            if (Muted) return;
            var set = steps[(int)surface];
            int i = Random.Range(0, set.Length - 1);
            if (i >= lastStep && lastStep >= 0) i++;
            lastStep = i;
            stepSrc.pitch = Random.Range(0.93f, 1.07f);
            stepSrc.PlayOneShot(set[i], Mathf.Clamp01(loudness));
            StepCount++;
            LastStepSurface = surface;
        }
        public void Pop() => Play(pop, 0.5f, Random.Range(0.95f, 1.1f));
        public void Nip() => Play(nips[Random.Range(0, nips.Length)], 0.75f, Random.Range(1.0f, 1.08f));
        public void Kibble() => Play(kibble, 0.55f, 1f);
        public void Crunch() => Play(noms[Random.Range(0, noms.Length)], 0.35f, Random.Range(0.96f, 1.05f));
        public void Yawn() => Play(yawn, 0.28f, Random.Range(.94f, 1.08f));
        public void Chirp() => Play(chirps[Random.Range(0, chirps.Length)], 0.6f, Random.Range(1.02f, 1.12f));

        void Play(AudioClip c, float vol, float pitch)
        {
            if (Muted) return;
            sfx.pitch = pitch;
            sfx.PlayOneShot(c, vol);
            PlayedCount++;
        }

        static AudioClip Clip(string name, float[] d)
        {
            var c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>합성 (테스트에서 직접 검사한다).</summary>
        public static class Synth
        {
            static float Smooth01(float x) => x <= 0f ? 0f : x >= 1f ? 1f : x * x * (3f - 2f * x);

            public static float[] Normalize(float[] d, float peak = Peak)
            {
                float m = 0f;
                foreach (var x in d) m = Mathf.Max(m, Mathf.Abs(x));
                if (m > 0f) for (int i = 0; i < d.Length; i++) d[i] *= peak / m;
                return d;
            }

            /// <summary>
            /// 골골송: 낮은 음(약 95 Hz와 배음)을 초당 24번 부드럽게 오르내리게 하고, 들숨·날숨으로 숨결을 준다.
            /// 펄스를 둥근 모양(코사인)으로 해서 웅웅거리지 않는다. 한 주기(2.4초)가 끝과 처음이 이어진다.
            /// </summary>
            public static float[] Purr()
            {
                float inhale = 1.1f, exhale = 1.3f, total = inhale + exhale;
                int n = Mathf.RoundToInt(total * Rate);
                var d = new float[n];
                // 정수 배가 되도록 주파수를 맞춰 반복 이음매에서 위상이 끊기지 않게
                float f0 = Mathf.Round(95f * total) / total, am = Mathf.Round(24f * total) / total;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    bool inh = t < inhale;
                    float local = inh ? t / inhale : (t - inhale) / exhale;
                    float env = Smooth01(local / 0.25f) * Smooth01((1f - local) / 0.25f) * (inh ? 0.6f : 1f);
                    float pulse = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * am * t);         // 둥근 펄스
                    pulse = 0.35f + 0.65f * pulse * pulse;
                    float w = 2f * Mathf.PI * f0 * t;
                    float tone = Mathf.Sin(w) + 0.55f * Mathf.Sin(2f * w) + 0.25f * Mathf.Sin(3f * w) + 0.08f * Mathf.Sin(4f * w);
                    d[i] = tone * pulse * env;
                }
                return Normalize(d, 0.4f);
            }

            // 모음 공명 (F1, F2). 작은 고양이의 목이라 사람보다 높다
            internal static readonly Vector2 VI = new Vector2(420f, 2500f), VA = new Vector2(1050f, 1750f), VO = new Vector2(700f, 1150f), VU = new Vector2(480f, 1000f), VM = new Vector2(320f, 1400f);

            /// <summary>
            /// 고양이 울음 한 번: "ㅁ"(입 다문 콧소리)로 시작해 모음을 지나며(이→아→오 등) 음높이가 올라갔다 내려온다.
            /// 짧고 낮으면 강아지처럼 들려서, 고양이답게 높고 길게, 모음 변화를 뚜렷하게 만든다.
            /// f: 시작·최고·끝 음높이, vowels: 지나가는 모음들
            /// </summary>
            public static float[] Cry(float fStart, float fPeak, float fEnd, float dur, float peakAt, Vector2[] vowels, float vibrato = 0.015f, int seed = 1)
            {
                int n = Mathf.RoundToInt(dur * Rate);
                var d = new float[n];
                var rng = new System.Random(seed);
                float phase = 0f, drift = 0f, driftT = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate, k = t / dur;
                    float f0 = k < peakAt ? Mathf.Lerp(fStart, fPeak, Smooth01(k / peakAt)) : Mathf.Lerp(fPeak, fEnd, Smooth01((k - peakAt) / (1f - peakAt)));
                    if (t > driftT) { driftT = t + 0.05f; drift = ((float)rng.NextDouble() - 0.5f) * 0.012f; }
                    f0 *= 1f + vibrato * Mathf.Sin(2f * Mathf.PI * 5.5f * t) * Smooth01((k - 0.2f) / 0.3f) + drift;
                    phase += f0 / Rate;
                    // 모음: 처음 10%는 콧소리(ㅁ), 이어서 vowels 를 고르게 지나간다
                    Vector2 fm;
                    if (k < 0.1f) fm = Vector2.Lerp(VM, vowels[0], Smooth01(k / 0.1f));
                    else
                    {
                        float u = (k - 0.1f) / 0.9f * (vowels.Length - 1);
                        int a = Mathf.Min(vowels.Length - 2, Mathf.FloorToInt(u));
                        fm = vowels.Length == 1 ? vowels[0] : Vector2.Lerp(vowels[a], vowels[a + 1], Smooth01(u - a));
                    }
                    float s = 0f;
                    for (int h = 1; h <= 10; h++)
                    {
                        float fh = f0 * h;
                        if (fh > 5200f) break;
                        float w = 1.0f / Mathf.Pow(h, 0.8f) * (0.25f + Mathf.Exp(-Mathf.Pow((fh - fm.x) / 260f, 2f)) + 0.7f * Mathf.Exp(-Mathf.Pow((fh - fm.y) / 380f, 2f)));
                        s += Mathf.Sin(phase * h * Mathf.PI * 2f) * w;
                    }
                    float env = Smooth01(k / 0.08f) * Smooth01((1f - k) / 0.28f) * (k < 0.1f ? 0.55f + 4.5f * k : 1f);
                    d[i] = s * env;
                }
                return Normalize(d);
            }

            /// <summary>짧은 울음 (깨물 때 "먀!" 등에 쓴다).</summary>
            public static float[] Meow(float baseF, float dur) => Cry(baseF * 0.92f, baseF * 1.12f, baseF * 0.95f, dur, 0.35f, new[] { VI, VA, VO }, 0.01f, 3);

            /// <summary>야옹 여러 가지: 누를 때마다 다른 소리가 나도록.</summary>
            public static float[][] MeowVariants() => new[]
            {
                Cry(720f, 1000f, 640f, 0.78f, 0.38f, new[] { VI, VA, VO }, 0.015f, 1),          // 미야옹
                Cry(680f, 760f, 1080f, 0.52f, 0.25f, new[] { VI, VU }, 0.01f, 2),               // 미유? (끝이 올라감)
                Cry(860f, 940f, 780f, 0.24f, 0.4f, new[] { VA }, 0.0f, 3),                       // 먀
                Concat(Cry(880f, 960f, 820f, 0.18f, 0.4f, new[] { VA }, 0f, 4), 0.06f, Cry(920f, 1000f, 840f, 0.2f, 0.4f, new[] { VA, VO }, 0f, 5)),   // 먀먀
                Cry(980f, 1220f, 900f, 0.9f, 0.45f, new[] { VI, VA, VA, VO }, 0.02f, 6),         // 냐아앙 (새끼 고양이처럼 길게)
                Cry(1120f, 1260f, 1050f, 0.34f, 0.4f, new[] { VI, VI }, 0.01f, 7),               // 미이
                Cry(600f, 840f, 560f, 0.86f, 0.42f, new[] { VI, VA, VO, VU }, 0.018f, 8),        // 야아옹 (낮고 느긋하게)
                Cry(760f, 900f, 980f, 0.4f, 0.5f, new[] { VM, VA }, 0.008f, 9),                  // 응냐? (입 다물었다 열며)
            };

            public static float[] Concat(float[] a, float gapSec, float[] b)
            {
                int gap = Mathf.RoundToInt(gapSec * Rate);
                var d = new float[a.Length + gap + b.Length];
                a.CopyTo(d, 0);
                b.CopyTo(d, a.Length + gap);
                return Normalize(d);
            }

            /// <summary>
            /// 발소리. 마루 = 작은 나무 "톡", 러그 = 폭신한 "툭", 풀 = 부드러운 사각, 모래 = 고운 알갱이 서걱.
            /// 풀·모래는 잡음 성분이 필요하지만 높은 소리를 깎고 아주 작게 해서 "지지직"으로 들리지 않게 한다.
            /// </summary>
            public static float[] Step(Surface surface, int variant)
            {
                var rng = new System.Random(100 + (int)surface * 10 + variant);
                float dur = surface == Surface.Grass || surface == Surface.Sand ? 0.16f : 0.08f;
                var d = new float[Mathf.RoundToInt(dur * Rate)];
                float r = 1f + (variant - 1.5f) * 0.05f;
                switch (surface)
                {
                    case Surface.Wood:
                        for (int i = 0; i < d.Length; i++)
                        {
                            float t = i / (float)Rate;
                            d[i] = Smooth01(t / 0.002f) * (Mathf.Sin(2f * Mathf.PI * 300f * r * t) * Mathf.Exp(-t * 70f) + 0.4f * Mathf.Sin(2f * Mathf.PI * 820f * r * t) * Mathf.Exp(-t * 120f));
                        }
                        return Normalize(d, 0.16f);
                    case Surface.Rug:
                        for (int i = 0; i < d.Length; i++)
                        {
                            float t = i / (float)Rate;
                            d[i] = Smooth01(t / 0.006f) * (Mathf.Sin(2f * Mathf.PI * 120f * r * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 250f * r * t)) * Mathf.Exp(-t * 45f);
                        }
                        return Normalize(d, 0.12f);
                    case Surface.Grass:
                    {
                        // 걸러낸 부드러운 사각임 (약 600~2500 Hz) + 아주 작은 낮은 툭
                        float lp1 = 0f, lp2 = 0f, hp = 0f;
                        for (int i = 0; i < d.Length; i++)
                        {
                            float t = i / (float)Rate, k = t / dur;
                            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
                            lp1 += (x - lp1) * 0.18f; lp2 += (lp1 - lp2) * 0.18f;   // 저역 통과 (~1.8 kHz): 높은 소리를 깎아 "지지직" 없이
                            hp += (lp2 - hp) * 0.08f;                                 // 아주 낮은 것은 뺀다
                            float rustle = (lp2 - hp) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k * 1.3f)) * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 23f * t));
                            d[i] = rustle * 2.2f + 0.25f * Mathf.Sin(2f * Mathf.PI * 140f * t) * Mathf.Exp(-t * 60f);
                        }
                        return Normalize(d, 0.11f);
                    }
                    default: // Sand: 고운 알갱이가 짧게 여러 번 (음정 있는 작은 알갱이 소리 + 낮은 눌림)
                    {
                        for (int g = 0; g < 26; g++)
                        {
                            float st = (float)rng.NextDouble() * dur * 0.7f, f = 1300f + (float)rng.NextDouble() * 1500f, a = 0.3f + 0.7f * (float)rng.NextDouble();
                            int s0 = Mathf.RoundToInt(st * Rate);
                            for (int i = 0; i < Rate * 0.006f && s0 + i < d.Length; i++)
                            {
                                float t = i / (float)Rate;
                                d[s0 + i] += a * Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 700f) * Smooth01(t / 0.0006f);
                            }
                        }
                        for (int i = 0; i < d.Length; i++)
                        {
                            float t = i / (float)Rate;
                            d[i] = d[i] * 0.35f + 0.5f * Mathf.Sin(2f * Mathf.PI * 110f * t) * Mathf.Exp(-t * 35f) * Smooth01(t / 0.008f);
                        }
                        return Normalize(d, 0.13f);
                    }
                }
            }

            /// <summary>반가운 "프릇": 짧게 위로 올라가는 두 음 (야옹과 같은 목소리).</summary>
            public static float[] Chirp()
            {
                var a = Meow(560f, 0.12f);
                var b = Meow(760f, 0.16f);
                int gap = Mathf.RoundToInt(0.03f * Rate);
                var d = new float[a.Length + gap + b.Length];
                a.CopyTo(d, 0);
                b.CopyTo(d, a.Length + gap);
                return Normalize(d);
            }

            /// <summary>오르골 한 음: 사인 + 맑은 배음, 짧은 시작과 긴 울림.</summary>
            public static void Bell(float[] d, int start, float f, float amp, float decay)
            {
                for (int i = start; i < d.Length; i++)
                {
                    float t = (i - start) / (float)Rate;
                    float env = Smooth01(t / 0.004f) * Mathf.Exp(-t * decay);
                    if (env < 1e-4f && t > 0.05f) break;
                    float w = 2f * Mathf.PI * f * t;
                    d[i] += amp * env * (Mathf.Sin(w) + 0.3f * Mathf.Sin(2f * w) * Mathf.Exp(-t * decay * 1.5f) + 0.1f * Mathf.Sin(3f * w) * Mathf.Exp(-t * decay * 3f));
                }
            }

            /// <summary>사료 붓기: 높은 오르골 음 다섯 개가 또르르 (C장조 펜타토닉으로 내려옴).</summary>
            public static float[] Kibble()
            {
                var d = new float[Mathf.RoundToInt(0.9f * Rate)];
                float[] notes = { 2093f, 1760f, 1568f, 1319f, 1175f };   // C7 A6 G6 E6 D6
                for (int k = 0; k < notes.Length; k++) Bell(d, Mathf.RoundToInt((0.02f + k * 0.075f) * Rate), notes[k], 1f - k * 0.1f, 9f);
                return Normalize(d, 0.32f);
            }

            /// <summary>먹는 소리 "냠": 낮고 짧은 둥근 톡 (나무 블록 같은 소리).</summary>
            public static float[] Nom(float f)
            {
                float dur = 0.12f;
                var d = new float[Mathf.RoundToInt(dur * Rate)];
                for (int i = 0; i < d.Length; i++)
                {
                    float t = i / (float)Rate;
                    float pitch = f * (1f - 0.25f * t / dur);                  // 살짝 내려가는 음
                    float env = Smooth01(t / 0.006f) * Mathf.Exp(-t * 38f);
                    d[i] = env * (Mathf.Sin(2f * Mathf.PI * pitch * t) + 0.2f * Mathf.Sin(4f * Mathf.PI * pitch * t));
                }
                return Normalize(d, 0.3f);
            }

            /// <summary>방석 "뿅": 부드러운 사인 상승음.</summary>
            public static float[] Pop()
            {
                float dur = 0.14f;
                int n = Mathf.RoundToInt(dur * Rate);
                var d = new float[n];
                float phase = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float f = Mathf.Lerp(380f, 760f, Mathf.Sqrt(t / dur));
                    phase += f / Rate;
                    d[i] = Mathf.Sin(phase * Mathf.PI * 2f) * Mathf.Exp(-t * 26f) * Smooth01(t / 0.006f);
                }
                return Normalize(d, 0.35f);
            }
        }
    }
}
