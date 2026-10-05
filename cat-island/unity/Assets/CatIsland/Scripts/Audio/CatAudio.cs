using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 소리 파일 없이 코드로 합성한 효과음: 골골송, 야옹, 오도독, 사료 붓기, 뿅.
    /// 시제품 단계용이다. 정식 소리는 4부 출시 범위에서 녹음, 제작으로 바꾼다.
    /// </summary>
    public class CatAudio : MonoBehaviour
    {
        const int Rate = 44100;
        AudioSource purrSrc, sfx;
        AudioClip purr, crunch, kibble, pop, nip, chirp;
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

            purr = MakePurr();
            meows = new[] { MakeMeow(640f, 0.5f, 1), MakeMeow(720f, 0.42f, 2), MakeMeow(580f, 0.6f, 3) };
            crunch = MakeCrunch();
            kibble = MakeKibble();
            pop = MakePop();
            nip = MakeTrill(430f, 0.26f, 34f, -0.15f, "nip");
            chirp = MakeTrill(520f, 0.3f, 26f, 0.5f, "chirp");
            purrSrc.clip = purr;
        }

        /// <summary>0이면 꺼짐. 손을 떼면 바로 끊기지 않고 잦아든다.</summary>
        public void SetPurr(float amount) { purrTarget = Mathf.Clamp01(amount); }

        void Update()
        {
            float v = Mathf.MoveTowards(purrSrc.volume, purrTarget * 0.85f, Time.deltaTime * (purrTarget > purrSrc.volume ? 1.2f : 0.5f));
            purrSrc.volume = v;
            if (v > 0.001f && !purrSrc.isPlaying) purrSrc.Play();
            else if (v <= 0.001f && purrSrc.isPlaying) purrSrc.Stop();
        }

        public void Meow() => Play(meows[Random.Range(0, meows.Length)], 0.7f, Random.Range(0.95f, 1.08f));
        public void Crunch() => Play(crunch, 0.5f, Random.Range(0.9f, 1.15f));
        public void Kibble() => Play(kibble, 0.6f, 1f);
        public void Pop() => Play(pop, 0.45f, Random.Range(0.95f, 1.1f));
        public void Nip() => Play(nip, 0.65f, 1f);
        public void Chirp() => Play(chirp, 0.6f, Random.Range(0.95f, 1.08f));

        void Play(AudioClip c, float vol, float pitch)
        {
            sfx.pitch = pitch;
            sfx.PlayOneShot(c, vol);
            PlayedCount++;
        }

        // ---------------- 합성 ----------------

        static AudioClip Clip(string name, float[] d)
        {
            float peak = 0f;
            foreach (var x in d) peak = Mathf.Max(peak, Mathf.Abs(x));
            if (peak > 0f) for (int i = 0; i < d.Length; i++) d[i] /= peak;
            var c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static float Smooth01(float x) => x <= 0f ? 0f : x >= 1f ? 1f : x * x * (3f - 2f * x);

        static AudioClip MakePurr()
        {
            // 들숨 1.1초 + 날숨 1.3초를 한 주기로, 끝과 처음이 이어지게
            float inhale = 1.1f, exhale = 1.3f, total = inhale + exhale;
            int n = Mathf.RoundToInt(total * Rate);
            var d = new float[n];
            var rng = new System.Random(7);
            float lp = 0f, lp2 = 0f, phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                bool inh = t < inhale;
                float local = inh ? t / inhale : (t - inhale) / exhale;
                float env = Smooth01(local / 0.18f) * Smooth01((1f - local) / 0.22f);
                env *= inh ? 0.65f : 1f;
                float f = inh ? 23f : 26f;
                phase += f / Rate;
                float ph = phase - Mathf.Floor(phase);
                float pulse = Mathf.Exp(-ph * 5.5f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.045f;
                lp2 += (lp - lp2) * 0.08f;
                float body = Mathf.Sin(phase * Mathf.PI * 2f * 2f) * 0.25f;
                d[i] = (lp2 * 6f + body * 0.4f) * pulse * env;
            }
            return Clip("purr", d);
        }

        static AudioClip MakeMeow(float baseF, float dur, int seed)
        {
            int n = Mathf.RoundToInt(dur * Rate);
            var d = new float[n];
            var rng = new System.Random(seed);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                // 음높이: 올라갔다가 내려옴
                float contour = k < 0.35f ? Mathf.Lerp(0.82f, 1.25f, Smooth01(k / 0.35f)) : Mathf.Lerp(1.25f, 0.88f, Smooth01((k - 0.35f) / 0.65f));
                float f0 = baseF * contour * (1f + Mathf.Sin(t * 2f * Mathf.PI * 6f) * 0.012f);
                phase += f0 / Rate;
                // 입모양: 미 → 아 → 우
                float formant = k < 0.3f ? Mathf.Lerp(900f, 1800f, k / 0.3f) : Mathf.Lerp(1800f, 1050f, (k - 0.3f) / 0.7f);
                float s = 0f;
                for (int h = 1; h <= 9; h++)
                {
                    float fh = f0 * h;
                    float w = Mathf.Exp(-Mathf.Pow((fh - formant) / 650f, 2f)) + 0.25f / h;
                    s += Mathf.Sin(phase * h * Mathf.PI * 2f) * w;
                }
                float breath = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.04f;
                float env = Smooth01(k / 0.08f) * Smooth01((1f - k) / 0.3f);
                d[i] = (s + breath) * env;
            }
            return Clip("meow" + seed, d);
        }

        static AudioClip MakeTrill(float baseF, float dur, float trillHz, float rise, string name)
        {
            int n = Mathf.RoundToInt(dur * Rate);
            var d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float f0 = baseF * (1f + rise * k);
                phase += f0 / Rate;
                float am = 0.55f + 0.45f * Mathf.Sin(t * trillHz * Mathf.PI * 2f);
                float s = Mathf.Sin(phase * Mathf.PI * 2f) + Mathf.Sin(phase * 2f * Mathf.PI * 2f) * 0.45f + Mathf.Sin(phase * 3f * Mathf.PI * 2f) * 0.2f;
                float env = Smooth01(k / 0.1f) * Smooth01((1f - k) / 0.35f);
                d[i] = s * am * env;
            }
            return Clip(name, d);
        }

        static AudioClip MakeCrunch()
        {
            float dur = 0.16f;
            int n = Mathf.RoundToInt(dur * Rate);
            var d = new float[n];
            var rng = new System.Random(3);
            float[] hits = { 0f, 0.035f, 0.07f, 0.1f };
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                foreach (var h in hits)
                {
                    float lt = t - h;
                    if (lt < 0f) continue;
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    s += noise * Mathf.Exp(-lt * 90f);
                }
                float hp = s - prev; // 고음 강조
                prev = s;
                d[i] = hp * 0.7f + s * 0.3f;
            }
            return Clip("crunch", d);
        }

        static AudioClip MakeKibble()
        {
            float dur = 0.75f;
            int n = Mathf.RoundToInt(dur * Rate);
            var d = new float[n];
            var rng = new System.Random(11);
            int clicks = 46;
            for (int c = 0; c < clicks; c++)
            {
                float start = Mathf.Pow((float)rng.NextDouble(), 1.4f) * (dur - 0.06f);
                float freq = 2200f + (float)rng.NextDouble() * 2600f;
                float amp = 0.4f + (float)rng.NextDouble() * 0.6f;
                int s0 = Mathf.RoundToInt(start * Rate);
                int len = Mathf.RoundToInt(0.035f * Rate);
                for (int i = 0; i < len && s0 + i < n; i++)
                {
                    float lt = i / (float)Rate;
                    d[s0 + i] += Mathf.Sin(lt * freq * Mathf.PI * 2f) * Mathf.Exp(-lt * 160f) * amp;
                }
            }
            return Clip("kibble", d);
        }

        static AudioClip MakePop()
        {
            float dur = 0.12f;
            int n = Mathf.RoundToInt(dur * Rate);
            var d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(380f, 980f, Mathf.Sqrt(t / dur));
                phase += f / Rate;
                d[i] = Mathf.Sin(phase * Mathf.PI * 2f) * Mathf.Exp(-t * 32f) * Smooth01(t / 0.004f);
            }
            return Clip("pop", d);
        }
    }
}
