using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 고양이 소리: 야옹, 반가운 "프릇", 살짝 깨물 때 "먀!", 골골송, 방석 누를 때 "뿅".
    /// 소리 파일 없이 코드로 만든다. 잡음(노이즈)은 쓰지 않는다: 아이폰 스피커에서 "지지직"으로 들린다.
    /// 모든 소리는 최대 크기를 절반 아래로 두어, 겹쳐도 넘쳐서 찌그러지지 않게 한다.
    /// 밥그릇 누를 때는 오르골 같은 "똑똑똑", 먹을 때는 작고 둥근 "냠냠" (둘 다 음정 있는 소리).
    /// </summary>
    public class CatAudio : MonoBehaviour
    {
        public const int Rate = 44100;
        public const float Peak = 0.45f;

        AudioSource purrSrc, sfx;
        AudioClip purr, pop, nip, chirp, kibble;
        AudioClip[] noms;
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

            purr = Clip("purr", Synth.Purr());
            meows = new[] { Clip("meow1", Synth.Meow(640f, 0.5f)), Clip("meow2", Synth.Meow(720f, 0.42f)), Clip("meow3", Synth.Meow(580f, 0.6f)) };
            chirp = Clip("chirp", Synth.Chirp());
            nip = Clip("nip", Synth.Meow(820f, 0.2f));
            pop = Clip("pop", Synth.Pop());
            kibble = Clip("kibble", Synth.Kibble());
            noms = new[] { Clip("nom1", Synth.Nom(520f)), Clip("nom2", Synth.Nom(600f)), Clip("nom3", Synth.Nom(470f)) };
            purrSrc.clip = purr;
        }

        public void SetPurr(float amount) { purrTarget = Mathf.Clamp01(amount); }

        void Update()
        {
            float v = Mathf.MoveTowards(purrSrc.volume, purrTarget * 0.6f, Time.deltaTime * (purrTarget > purrSrc.volume ? 1.0f : 0.5f));
            purrSrc.volume = v;
            if (v > 0.001f && !purrSrc.isPlaying) purrSrc.Play();
            else if (v <= 0.001f && purrSrc.isPlaying) purrSrc.Stop();
        }

        public void Meow() => Play(meows[Random.Range(0, meows.Length)], 0.8f, Random.Range(0.95f, 1.08f));
        public void Pop() => Play(pop, 0.5f, Random.Range(0.95f, 1.1f));
        public void Nip() => Play(nip, 0.7f, 1f);
        public void Kibble() => Play(kibble, 0.55f, 1f);
        public void Crunch() => Play(noms[Random.Range(0, noms.Length)], 0.35f, Random.Range(0.96f, 1.05f));
        public void Chirp() => Play(chirp, 0.7f, Random.Range(0.96f, 1.06f));

        void Play(AudioClip c, float vol, float pitch)
        {
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

            /// <summary>야옹: 음높이가 올라갔다 내려오고, 입모양(미→아→우)에 따라 배음이 바뀐다.</summary>
            public static float[] Meow(float baseF, float dur)
            {
                int n = Mathf.RoundToInt(dur * Rate);
                var d = new float[n];
                float phase = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float k = t / dur;
                    float contour = k < 0.35f ? Mathf.Lerp(0.82f, 1.25f, Smooth01(k / 0.35f)) : Mathf.Lerp(1.25f, 0.88f, Smooth01((k - 0.35f) / 0.65f));
                    float f0 = baseF * contour * (1f + Mathf.Sin(t * 2f * Mathf.PI * 6f) * 0.012f);
                    phase += f0 / Rate;
                    float formant = k < 0.3f ? Mathf.Lerp(900f, 1800f, k / 0.3f) : Mathf.Lerp(1800f, 1050f, (k - 0.3f) / 0.7f);
                    float s = 0f;
                    for (int h = 1; h <= 7; h++)
                    {
                        float fh = f0 * h;
                        float w = Mathf.Exp(-Mathf.Pow((fh - formant) / 650f, 2f)) + 0.25f / h;
                        s += Mathf.Sin(phase * h * Mathf.PI * 2f) * w;
                    }
                    float env = Smooth01(k / 0.1f) * Smooth01((1f - k) / 0.35f);
                    d[i] = s * env;
                }
                return Normalize(d);
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
