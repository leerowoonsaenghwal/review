using System.Collections;
using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 게임 반응 (ART_DIRECTION 11장): 버튼 '톡', 코인 방울, 용품을 놓을 때 '통' 튀며 반짝, 방석 눌림. 소리는 코드로 만든 짧은 소리
    /// (외부 음원 없음), 설정의 효과음 끄기를 따른다.
    /// </summary>
    public static class GameFeel
    {
        static AudioSource src; static AudioClip tok, bell, place;
        public static bool SoundOn = true;
        static void Ensure()
        {
            if (src) return;
            var go = new GameObject("UISound"); Object.DontDestroyOnLoad(go); src = go.AddComponent<AudioSource>(); src.spatialBlend = 0f; src.playOnAwake = false;
            tok = Make("tok", Tok()); bell = Make("bell", Bell()); place = Make("place", Place());
        }
        static AudioClip Make(string n, float[] d) { var c = AudioClip.Create(n, d.Length, 1, CatAudio.Rate, false); c.SetData(d, 0); return c; }
        public static void Tap() { if (!SoundOn) return; Ensure(); src.pitch = Random.Range(.96f, 1.06f); src.PlayOneShot(tok, .35f); }
        public static void Coins() { if (!SoundOn) return; Ensure(); src.pitch = 1f; src.PlayOneShot(bell, .45f); }
        public static void Placed() { if (!SoundOn) return; Ensure(); src.pitch = 1f; src.PlayOneShot(place, .5f); }

        /// <summary>놓은 용품이 '통' 하고 커졌다 자리 잡는다 + 반짝.</summary>
        public static void PopIn(GameObject go)
        {
            if (!go) return; var r = go.AddComponent<PopInFx>(); Placed();
            FxPool.Instance?.Burst(Icon.Sparkle, go.transform.position + Vector3.up * .3f, 6, .35f, .22f);
        }

        // ---- 소리 (부드럽게: 지지직거리는 잡음 없이 사인파 위주)
        static float[] Tok()   // 나무 블록 '톡'
        {
            int n = (int)(.07f * CatAudio.Rate); var d = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)CatAudio.Rate; d[i] = Mathf.Sin(2 * Mathf.PI * 880 * t) * Mathf.Exp(-t * 70) * .8f + Mathf.Sin(2 * Mathf.PI * 1760 * t) * Mathf.Exp(-t * 120) * .2f; }
            return CatAudio.Synth.Normalize(d, .3f);
        }
        static float[] Bell()  // 작은 방울 두 번
        {
            int n = (int)(.45f * CatAudio.Rate); var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)CatAudio.Rate, t2 = t - .09f;
                float a = Mathf.Sin(2 * Mathf.PI * 1568 * t) * Mathf.Exp(-t * 14) + .3f * Mathf.Sin(2 * Mathf.PI * 3920 * t) * Mathf.Exp(-t * 30);
                float b = t2 > 0 ? Mathf.Sin(2 * Mathf.PI * 2093 * t2) * Mathf.Exp(-t2 * 14) + .3f * Mathf.Sin(2 * Mathf.PI * 5230 * t2) * Mathf.Exp(-t2 * 30) : 0;
                d[i] = a + b;
            }
            return CatAudio.Synth.Normalize(d, .3f);
        }
        static float[] Place() // '통' (낮은 둥근 소리) + 작은 반짝
        {
            int n = (int)(.3f * CatAudio.Rate); var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)CatAudio.Rate, f = Mathf.Lerp(220, 150, t / .3f);
                d[i] = Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * 16) + .25f * Mathf.Sin(2 * Mathf.PI * 1318 * (t - .05f)) * (t > .05f ? Mathf.Exp(-(t - .05f) * 20) : 0);
            }
            return CatAudio.Synth.Normalize(d, .35f);
        }
    }

    public class PopInFx : MonoBehaviour
    {
        Vector3 baseScale;
        IEnumerator Start()
        {
            baseScale = transform.localScale;
            for (float t = 0; t < .35f; t += Time.deltaTime)
            {
                float u = t / .35f, s = u < .55f ? Mathf.Lerp(.2f, 1.12f, u / .55f) : Mathf.Lerp(1.12f, 1f, (u - .55f) / .45f);
                transform.localScale = baseScale * s; yield return null;
            }
            transform.localScale = baseScale; Destroy(this);
        }
    }
}
