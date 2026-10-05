using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace CatIsland.Tests
{
    /// <summary>
    /// 소리 품질: 잡음("지지직")이 없고, 넘치지 않고, 배경음악 반복이 매끄러운가.
    /// 들어 볼 수 있게 WAV 로도 저장한다 (unity/Shots/audio/).
    /// </summary>
    public class AudioTests
    {
        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Shots/audio"));

        /// <summary>cutoff 위 주파수의 에너지 비율 (4096점 FFT 를 여러 구간 평균).</summary>
        static float HighBandFraction(float[] x, int rate, float cutoff)
        {
            const int N = 4096;
            double hi = 0, all = 0;
            for (int start = 0; start + N <= x.Length; start += N / 2)
            {
                var re = new double[N]; var im = new double[N];
                for (int i = 0; i < N; i++) re[i] = x[start + i] * (0.5 - 0.5 * System.Math.Cos(2 * System.Math.PI * i / (N - 1)));
                FFT(re, im);
                for (int k = 1; k < N / 2; k++)
                {
                    double e = re[k] * re[k] + im[k] * im[k];
                    all += e;
                    if (k * (double)rate / N > cutoff) hi += e;
                }
            }
            return all > 0 ? (float)(hi / all) : 0f;
        }

        static void FFT(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * System.Math.PI / len;
                for (int i = 0; i < n; i += len)
                    for (int k = 0; k < len / 2; k++)
                    {
                        double wr = System.Math.Cos(ang * k), wi = System.Math.Sin(ang * k);
                        double ur = re[i + k], ui = im[i + k];
                        double vr = re[i + k + len / 2] * wr - im[i + k + len / 2] * wi, vi = re[i + k + len / 2] * wi + im[i + k + len / 2] * wr;
                        re[i + k] = ur + vr; im[i + k] = ui + vi; re[i + k + len / 2] = ur - vr; im[i + k + len / 2] = ui - vi;
                    }
            }
        }

        static float PeakOf(float[] x) { float m = 0; foreach (var v in x) m = Mathf.Max(m, Mathf.Abs(v)); return m; }

        static void SaveWav(string name, float[] x, int rate)
        {
            Directory.CreateDirectory(Dir);
            using var w = new BinaryWriter(File.Create(Path.Combine(Dir, name + ".wav")));
            int bytes = x.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (var v in x) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
        }

        static float[] Repeat(float[] x, int times) { var o = new float[x.Length * times]; for (int i = 0; i < times; i++) x.CopyTo(o, i * x.Length); return o; }

        [Test]
        public void CatSounds_AreTonal_NotHiss_AndNeverClip()
        {
            var sounds = new (string name, float[] data)[]
            {
                ("purr", Repeat(CatAudio.Synth.Purr(), 3)), ("meow", CatAudio.Synth.Meow(640f, 0.5f)), ("chirp", CatAudio.Synth.Chirp()),
                ("nip", CatAudio.Synth.Meow(820f, 0.2f)), ("pop", CatAudio.Synth.Pop()), ("kibble", CatAudio.Synth.Kibble()), ("nom", CatAudio.Synth.Nom(520f)),
            };
            var list = new System.Collections.Generic.List<(string, float[])>(sounds);
            var mv = CatAudio.Synth.MeowVariants();
            for (int i = 0; i < mv.Length; i++) list.Add(("meow_v" + i, mv[i]));
            foreach (Surface sf in System.Enum.GetValues(typeof(Surface)))
                for (int v = 0; v < 4; v++) list.Add(($"step_{sf}_{v}".ToLower(), CatAudio.Synth.Step(sf, v)));
            foreach (var (name, data) in list)
            {
                SaveWav(name, data, CatAudio.Rate);
                var padded = data.Length >= 4096 ? data : Repeat(data, 4096 / data.Length + 2);
                float hf = HighBandFraction(padded, CatAudio.Rate, 6000f);
                Debug.Log($"[Audio] {name}: peak={PeakOf(data):F2} energy>6kHz={hf:P2}");
                Assert.LessOrEqual(PeakOf(data), 0.5f, name + " leaves headroom so overlapping sounds never clip");
                Assert.Less(hf, 0.03f, name + " has no hiss");
            }
        }

        [Test]
        public void Meows_AreAllDifferent_AndCatLike()
        {
            var mv = CatAudio.Synth.MeowVariants();
            Assert.GreaterOrEqual(mv.Length, 6, "many meows so taps do not repeat");
            for (int i = 0; i < mv.Length; i++)
                for (int j = i + 1; j < mv.Length; j++)
                {
                    bool sameLen = Mathf.Abs(mv[i].Length - mv[j].Length) < CatAudio.Rate * 0.02f;
                    float diff = 0f; int n = Mathf.Min(mv[i].Length, mv[j].Length);
                    for (int k = 0; k < n; k++) diff += Mathf.Abs(mv[i][k] - mv[j][k]);
                    Assert.IsFalse(sameLen && diff / n < 0.01f, $"meow {i} and {j} sound the same");
                }
            // 고양이 울음은 높다: 가장 센 음 성분이 500 Hz 위 (짧고 낮으면 강아지처럼 들린다)
            foreach (var m in mv) Assert.Greater(DominantHz(m, CatAudio.Rate), 500f);
        }

        static float DominantHz(float[] x, int rate)
        {
            const int N = 4096;
            int start = Mathf.Max(0, x.Length / 2 - N / 2);
            var re = new double[N]; var im = new double[N];
            for (int i = 0; i < N && start + i < x.Length; i++) re[i] = x[start + i] * (0.5 - 0.5 * System.Math.Cos(2 * System.Math.PI * i / (N - 1)));
            FFT(re, im);
            int best = 1; double be = 0;
            for (int k = 1; k < N / 2; k++) { double e = re[k] * re[k] + im[k] * im[k]; if (e > be) { be = e; best = k; } }
            return best * rate / (float)N;
        }

        [Test]
        public void Purr_LoopsWithoutAClick()
        {
            var p = CatAudio.Synth.Purr();
            Assert.Less(Mathf.Abs(p[p.Length - 1] - p[0]), 0.02f);
        }

        [Test]
        public void Music_IsGentle_AndLoopsSeamlessly()
        {
            var m = Music.Render();
            SaveWav("bgm", Repeat(m, 2), Music.Rate);
            float hf = HighBandFraction(m, Music.Rate, 6000f);
            Debug.Log($"[Audio] bgm: {m.Length / (float)Music.Rate:F1}s peak={PeakOf(m):F2} energy>6kHz={hf:P2} seam={Mathf.Abs(m[m.Length - 1] - m[0]):F4}");
            Assert.AreEqual(Music.LoopSeconds, m.Length / (float)Music.Rate, 0.01f);
            Assert.LessOrEqual(PeakOf(m), 0.36f, "quieter than the cat");
            Assert.Less(hf, 0.01f);
            // 이음매: 끝과 처음 샘플의 차이가 이웃 샘플 차이 수준
            float maxStep = 0f;
            for (int i = 1; i < m.Length; i++) maxStep = Mathf.Max(maxStep, Mathf.Abs(m[i] - m[i - 1]));
            Assert.LessOrEqual(Mathf.Abs(m[m.Length - 1] - m[0]), maxStep * 1.05f);
        }
    }
}
