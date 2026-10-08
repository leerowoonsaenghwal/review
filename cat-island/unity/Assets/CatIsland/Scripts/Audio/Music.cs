using System;
using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 배경음악: 잔잔한 오르골·마림바 곡을 코드로 만든다 (기획서 3부 8장. 외부 음원 없음).
    /// 84 BPM, 16마디(약 46초), 이음매 없이 반복. 펜타토닉 멜로디라 어떤 음도 화음과 부딪히지 않는다.
    /// </summary>
    public static class Music
    {
        public const int Rate = 32000;
        const float Bpm = 84f;
        static float Beat => 60f / Bpm;

        // 화음 (MIDI 근음, 구성음). 마지막 C 가 처음 C 로 돌아간다
        static readonly int[][] Chords =
        {
            new[] { 48, 52, 55 }, new[] { 45, 48, 52 }, new[] { 41, 45, 48 }, new[] { 43, 47, 50 },   // C Am F G
            new[] { 48, 52, 55 }, new[] { 45, 48, 52 }, new[] { 41, 45, 48 }, new[] { 43, 47, 50 },
            new[] { 41, 45, 48 }, new[] { 43, 47, 50 }, new[] { 40, 43, 47 }, new[] { 45, 48, 52 },   // F G Em Am
            new[] { 41, 45, 48 }, new[] { 43, 47, 50 }, new[] { 48, 52, 55 }, new[] { 48, 52, 55 },   // F G C C
        };
        static readonly int[] Penta = { 0, 2, 4, 7, 9 };   // 도 레 미 솔 라

        static float Hz(float midi) => 440f * Mathf.Pow(2f, (midi - 69f) / 12f);
        static float Smooth01(float x) => x <= 0f ? 0f : x >= 1f ? 1f : x * x * (3f - 2f * x);

        public static float LoopSeconds => Chords.Length * 4 * Beat;

        public static AudioClip CreateClip()
        {
            var d = Render();
            var c = AudioClip.Create("bgm", d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>곡 전체를 렌더링한다. 끝에서 넘친 울림은 처음에 더해 반복 이음매를 없앤다.</summary>
        public static float[] Render(int seed = 7)
        {
            int loop = Mathf.RoundToInt(LoopSeconds * Rate), tail = 4 * Rate;
            var d = new float[loop + tail];
            var rng = new System.Random(seed);

            for (int bar = 0; bar < Chords.Length; bar++)
            {
                var ch = Chords[bar];
                float t0 = bar * 4 * Beat;
                // 베이스: 1, 3박에 근음 (한 옥타브 아래)
                Note(d, t0, ch[0] - 12, 0.22f, 1.6f, Voice.Bass);
                Note(d, t0 + 2 * Beat, ch[0] - 12, 0.14f, 1.6f, Voice.Bass);
                // 패드: 화음 지속음 (아주 작게)
                foreach (var m in ch) Pad(d, t0, 4 * Beat, m + 12, 0.035f);
                // 마림바: 8분음표 펼친화음
                int[] pat = { 0, 1, 2, 3, 2, 1, 2, 1 };
                for (int k = 0; k < 8; k++)
                {
                    int idx = pat[k];
                    int midi = idx < 3 ? ch[idx] + 12 : ch[0] + 24;
                    Note(d, t0 + k * Beat * 0.5f, midi, k % 2 == 0 ? 0.11f : 0.08f, 4.5f, Voice.Marimba);
                }
            }

            // 멜로디: 4마디 주제(A)를 만들고 A A' B A 로 쓴다
            var motif = MakeMotif(rng);
            var motifB = MakeMotif(rng);
            void PlayMotif((float beat, float len, int deg)[] m, int barOffset, int variation)
            {
                foreach (var (beat, len, deg) in m)
                {
                    int bar = barOffset + Mathf.FloorToInt(beat / 4f);
                    int dg = deg;
                    if (variation == 1 && beat >= 12f) dg = deg - 1;                      // A': 끝을 살짝 낮게
                    int midi = 72 + DegreeToSemis(dg);
                    midi = SnapToChord(midi, Chords[bar], beat % 2f < 0.01f);
                    Note(d, barOffset * 4 * Beat + beat * Beat, midi, 0.16f, 2.0f, Voice.MusicBox);
                }
            }
            PlayMotif(motif, 0, 0);
            PlayMotif(motif, 4, 1);
            PlayMotif(motifB, 8, 0);
            PlayMotif(motif, 12, 0);

            // 높은 소리를 살짝 깎는다 (한 극 저역 통과, 약 5 kHz)
            float a = Mathf.Exp(-2f * Mathf.PI * 5000f / Rate), y = 0f;
            for (int i = 0; i < d.Length; i++) { y = (1f - a) * d[i] + a * y; d[i] = y; }

            // 반복 이음매: 끝에서 넘친 울림을 처음에 더한다
            var outp = new float[loop];
            Array.Copy(d, outp, loop);
            for (int i = 0; i < tail; i++) outp[i] += d[loop + i];
            return CatAudio.Synth.Normalize(outp, 0.35f);
        }

        static int DegreeToSemis(int deg)
        {
            int oct = Mathf.FloorToInt(deg / 5f);
            int i = deg - oct * 5;
            return oct * 12 + Penta[i];
        }

        /// <summary>강박에서는 화음 구성음(같은 음이름)에 가장 가까운 펜타토닉 음으로.</summary>
        static int SnapToChord(int midi, int[] chord, bool strong)
        {
            if (!strong) return midi;
            int best = midi, bd = 99;
            foreach (var c in chord)
                for (int o = -2; o <= 2; o++)
                {
                    int m = (c % 12) + 12 * (midi / 12 + o);
                    int pc = ((m % 12) + 12) % 12;
                    if (Array.IndexOf(Penta, pc) < 0) continue;   // (시 같은 음은 쓰지 않음)
                    int dd = Mathf.Abs(m - midi);
                    if (dd < bd) { bd = dd; best = m; }
                }
            return best;
        }

        static (float, float, int)[] MakeMotif(System.Random rng)
        {
            // 박자 꼴 (박 단위). 마디 끝은 길게 쉬어 숨을 준다
            float[][] rhythms =
            {
                new[] { 1f, 1f, 2f }, new[] { 0.5f, 0.5f, 1f, 2f }, new[] { 1f, 0.5f, 0.5f, 2f },
                new[] { 1.5f, 0.5f, 2f }, new[] { 1f, 1f, 1f, 1f },
            };
            var notes = new System.Collections.Generic.List<(float, float, int)>();
            int deg = 2 + rng.Next(3);   // 미~라 근처에서 시작
            for (int bar = 0; bar < 4; bar++)
            {
                var r = bar == 3 ? new[] { 1f, 3f } : rhythms[rng.Next(rhythms.Length)];
                float beat = bar * 4f;
                foreach (var len in r)
                {
                    int step = new[] { -2, -1, -1, 1, 1, 2, 0 }[rng.Next(7)];
                    deg = Mathf.Clamp(deg + step, 0, 7);
                    if (bar == 3 && len >= 3f) deg = 5;   // 주제 끝은 도(한 옥타브 위)로 쉰다
                    notes.Add((beat, len, deg));
                    beat += len;
                }
            }
            return notes.ToArray();
        }

        enum Voice { MusicBox, Marimba, Bass }

        static void Note(float[] d, float start, float midi, float amp, float decay, Voice v)
        {
            float f = Hz(midi);
            int s0 = Mathf.RoundToInt(start * Rate);
            float dur = Mathf.Min(6f, 6.9f / decay);
            int n = Mathf.Min(d.Length - s0, Mathf.RoundToInt(dur * Rate));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float att = Smooth01(t / (v == Voice.Bass ? 0.02f : 0.005f));
                float env = att * Mathf.Exp(-t * decay);
                float w = 2f * Mathf.PI * f * t, s;
                switch (v)
                {
                    case Voice.MusicBox: s = Mathf.Sin(w) + 0.25f * Mathf.Sin(2f * w) * Mathf.Exp(-t * 3f) + 0.08f * Mathf.Sin(3f * w) * Mathf.Exp(-t * 6f); break;
                    case Voice.Marimba: s = Mathf.Sin(w) + 0.18f * Mathf.Sin(3.93f * w) * Mathf.Exp(-t * 18f); break;   // (마림바의 4배음 근처 배음)
                    default: s = Mathf.Sin(w) + 0.2f * Mathf.Sin(2f * w); break;
                }
                d[s0 + i] += amp * env * s;
            }
        }

        static void Pad(float[] d, float start, float len, float midi, float amp)
        {
            float f = Hz(midi);
            int s0 = Mathf.RoundToInt(start * Rate);
            int n = Mathf.Min(d.Length - s0, Mathf.RoundToInt((len + 1.2f) * Rate));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Smooth01(t / 0.8f) * (t > len ? Mathf.Exp(-(t - len) * 3f) : 1f);
                float w = 2f * Mathf.PI * f * t;
                d[s0 + i] += amp * env * (Mathf.Sin(w * 0.997f) + Mathf.Sin(w * 1.003f)) * 0.5f;
            }
        }
    }

    /// <summary>
    /// 배경음악 재생: 낮 곡과 밤 곡(assets/sounds/music, prototype/3d/music_render.py 로 코드로 만든 곡)을 섬의 시간에 따라
    /// 천천히 바꿔 튼다 (8초 겹쳐 넘어가기). 곡 파일이 없으면 예전 합성 곡. 시작할 때 천천히 커진다.
    /// </summary>
    public class BackgroundMusic : MonoBehaviour
    {
        public float volume = 0.5f;
        /// <summary>설정의 '음악'.</summary>
        public static bool On = true;
        AudioSource day, night;
        float dayW = 1f;

        void Start()
        {
            AudioSource Src(AudioClip c) { var s = gameObject.AddComponent<AudioSource>(); s.clip = c; s.loop = true; s.volume = 0f; s.spatialBlend = 0f; s.priority = 200; s.Play(); return s; }
            var dc = Resources.Load<AudioClip>("Sounds/music/bgm_day"); var nc = Resources.Load<AudioClip>("Sounds/music/bgm_night");
            day = Src(dc ? dc : Music.CreateClip());
            if (nc) night = Src(nc);
            dayW = IsNight ? 0f : 1f;
        }

        static bool IsNight => GameBootstrap.Instance && GameBootstrap.Instance.Day && GameBootstrap.Instance.Day.Night;

        void Update()
        {
            if (!day) return;
            float want = night && IsNight ? 0f : 1f;
            dayW = Mathf.MoveTowards(dayW, want, Time.unscaledDeltaTime / 8f);
            float master = Mathf.MoveTowards(day.volume + (night ? night.volume : 0f), On ? volume : 0f, Time.unscaledDeltaTime * volume / (On ? 2.5f : .4f));
            day.volume = master * dayW; if (night) night.volume = master * (1f - dayW);
        }

        public bool Playing => day && day.isPlaying;
        public string Current => night && dayW < .5f ? "night" : "day";
    }
}
