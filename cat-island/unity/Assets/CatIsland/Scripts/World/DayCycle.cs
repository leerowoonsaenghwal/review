using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 하루 시간대 (기획서 3부 4-2, 실제 지역 시간): 아침 6~10 연한 복숭아 / 낮 10~17 따뜻한 흰빛(시안 값) / 저녁 17~20 주황 노을 /
    /// 밤 20~6 푸른 달빛과 남색 하늘. 맑음은 유지한다 (저녁도 탁하지 않게: ART_DIRECTION 2-1). 경계는 30분에 걸쳐 섞는다.
    /// </summary>
    public class DayCycle : MonoBehaviour
    {
        public Light sun; public Camera cam; public WeatherFx weather;
        /// <summary>테스트·스크린샷: 시각을 고정 (0~24). 음수면 기기 시계.</summary>
        public static float HourOverride = -1f;
        public float Hour { get; private set; }
        float nextAt;

        struct Look { public Color sun, sky, ground, fog; public float intensity, elevation; }
        static Look L(string sun, float i, float el, string sky, string ground, string fog) => new Look { sun = Palette.Hex(sun), intensity = i, elevation = el, sky = Palette.Hex(sky), ground = Palette.Hex(ground), fog = Palette.Hex(fog) };
        static readonly (float hour, Look look)[] Keys =
        {
            (0f,  L("9fb6ff", .45f, 40, "2e3a6e", "33456a", "3a4a7c")),   // 밤
            (5.5f, L("9fb6ff", .45f, 40, "2e3a6e", "33456a", "3a4a7c")),
            (6.5f, L("ffd2b8", 1.1f, 22, "f6d6e2", "86b867", "f3cbd8")),   // 아침
            (9.5f, L("ffe6c8", 1.35f, 42, "d8eefc", "7fb85a", "a8dcf2")),
            (10.5f, L("fff0d0", 1.47f, 50, "cdeeff", "7fb85a", "8fd6f2")), // 낮 (시안)
            (16.5f, L("fff0d0", 1.47f, 50, "cdeeff", "7fb85a", "8fd6f2")),
            (18f, L("ffbe82", 1.3f, 22, "ffe2c6", "8cbf66", "f8c9a2")),   // 저녁 (노을빛이되 탁하지 않게: 풀은 초록으로)
            (19.5f, L("ffa88a", 1.0f, 14, "d8b8e6", "7a9a78", "c8a8d6")),
            (20.5f, L("9fb6ff", .45f, 40, "2e3a6e", "33456a", "3a4a7c")),  // 밤
            (24f, L("9fb6ff", .45f, 40, "2e3a6e", "33456a", "3a4a7c")),
        };

        void Update()
        {
            if (Time.unscaledTime < nextAt) return; nextAt = Time.unscaledTime + 20f;
            Apply(HourOverride >= 0 ? HourOverride : (float)System.DateTime.Now.TimeOfDay.TotalHours);
        }

        public void Apply(float hour)
        {
            Hour = hour; nextAt = Time.unscaledTime + 20f;
            int i = 0; while (i < Keys.Length - 2 && Keys[i + 1].hour <= hour) i++;
            var a = Keys[i]; var b = Keys[i + 1]; float u = Mathf.InverseLerp(a.hour, b.hour, hour); u = u * u * (3 - 2 * u);
            var A = a.look; var B = b.look;
            if (sun)
            {
                sun.color = Color.Lerp(A.sun, B.sun, u); sun.intensity = Mathf.Lerp(A.intensity, B.intensity, u) * (weather ? weather.SunScale : 1f);
                sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(A.elevation, B.elevation, u), 36f, 0f);
            }
            var sky = Color.Lerp(A.sky, B.sky, u); var ground = Color.Lerp(A.ground, B.ground, u);
            RenderSettings.ambientSkyColor = sky * .62f; RenderSettings.ambientEquatorColor = Color.Lerp(sky, ground, .5f) * .62f; RenderSettings.ambientGroundColor = ground * .62f;
            var fog = Color.Lerp(A.fog, B.fog, u); RenderSettings.fogColor = fog; if (cam) cam.backgroundColor = fog;
        }

        public bool Night => Hour >= 20.5f || Hour < 5.5f;
    }
}
