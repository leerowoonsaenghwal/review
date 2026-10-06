using CatIsland.Game;
using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 날씨 보이기 (Weather.At): 카메라 둘레에 비(가는 줄)·눈(동그란 송이) 파티클, 흐림·비·눈은 해를 조금 약하게.
    /// 비도 가볍고 맑은 색으로 (탁한 회색 화면 금지: ART_DIRECTION 2-1). 비·눈 알갱이는 코드로 그린 작은 그림.
    /// </summary>
    public class WeatherFx : MonoBehaviour
    {
        /// <summary>테스트·스크린샷: 날씨 고정. null 이면 기기 날짜.</summary>
        public static WeatherKind? Override;
        public WeatherKind Now { get; private set; } = WeatherKind.Clear;
        public Transform follow;
        ParticleSystem ps; ParticleSystemRenderer pr; Material rainMat, snowMat; float nextAt;

        /// <summary>해 밝기 배율 (DayCycle 이 곱한다).</summary>
        public float SunScale => Now switch { WeatherKind.Cloudy => .82f, WeatherKind.Rain => .72f, WeatherKind.Snow => .88f, _ => 1f };

        void Awake()
        {
            var go = new GameObject("WeatherParticles"); go.transform.SetParent(transform, false);
            ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            pr = go.GetComponent<ParticleSystemRenderer>();
            var sh = Resources.Load<Shader>("Shaders/Weather");
            rainMat = new Material(sh) { name = "Rain", mainTexture = Dot(8, 32, true) };
            snowMat = new Material(sh) { name = "Snow", mainTexture = Dot(32, 32, false) };
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 2600; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(24f, .1f, 16f);
            go.transform.localPosition = new Vector3(0, 7f, 0);
        }

        static Texture2D Dot(int w, int h, bool streak)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = streak ? "rain" : "snow" };
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float u = (x + .5f) / w * 2 - 1, v = (y + .5f) / h * 2 - 1;
                    float a = streak ? Mathf.Clamp01(1 - Mathf.Abs(u) * 1.6f) * Mathf.Clamp01(1 - v * v) : Mathf.Clamp01((1 - Mathf.Sqrt(u * u + v * v)) * 2.2f);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply(); return t;
        }

        void Update()
        {
            if (follow) transform.position = new Vector3(follow.position.x, 0, follow.position.z);
            if (Time.unscaledTime < nextAt) return; nextAt = Time.unscaledTime + 30f;
            Set(Override ?? Weather.At(System.DateTime.Now));
        }

        public void Set(WeatherKind w)
        {
            nextAt = Time.unscaledTime + 30f;
            if (w == Now && (ps.isPlaying || w == WeatherKind.Clear || w == WeatherKind.Cloudy)) return;
            Now = w;
            var main = ps.main; var em = ps.emission; var vel = ps.velocityOverLifetime; var noise = ps.noise;
            if (w == WeatherKind.Rain)
            {
                pr.sharedMaterial = rainMat; pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = .06f; pr.lengthScale = .9f;
                main.startSpeed = 9f; main.startLifetime = .9f; main.startSize = .035f; main.startColor = new Color(.86f, .94f, 1f, .5f); main.gravityModifier = 0f;
                em.rateOverTime = 1100; vel.enabled = false; noise.enabled = false;
                var sh = ps.shape; sh.rotation = new Vector3(180f, 0, 0);   // (아래로)
                ps.Play();
            }
            else if (w == WeatherKind.Snow)
            {
                pr.sharedMaterial = snowMat; pr.renderMode = ParticleSystemRenderMode.Billboard;
                main.startSpeed = .9f; main.startLifetime = 9f; main.startSize = new ParticleSystem.MinMaxCurve(.05f, .11f); main.startColor = new Color(1f, 1f, 1f, .9f); main.gravityModifier = 0f;
                em.rateOverTime = 260; noise.enabled = true; noise.strength = .35f; noise.frequency = .4f;
                var sh = ps.shape; sh.rotation = new Vector3(180f, 0, 0);
                ps.Play();
            }
            else ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
