using UnityEngine;
using UnityEngine.Rendering;

namespace CatIsland
{
    /// <summary>
    /// 장면을 코드로 조립한다: 카메라, 해, 섬, 밥그릇, 방석, 고양이, 입력.
    /// 장면 파일에는 이 컴포넌트 하나만 있다.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        public CatBrain Cat { get; private set; }
        public FoodBowl Bowl { get; private set; }
        public Cushion Cushion { get; private set; }
        public IslandCamera IslandCam { get; private set; }
        public TouchRouter Router { get; private set; }
        public CatAudio Audio { get; private set; }

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Haptics.Prepare();
            Debug.developerConsoleEnabled = false; // 시뮬레이터 빌드는 항상 개발판이라 콘솔이 체험을 가린다
            Debug.Log($"[CatIsland] boot device={SystemInfo.deviceModel} gen={DeviceGenerationName()} simulator={IsIOSSimulator()} debug={Debug.isDebugBuild}");

            SetupLighting();

            var world = new GameObject("World").transform;
            IslandBuilder.Build(world);

            Vector3 bowlPos = new Vector3(1.05f, 0.004f, 0.75f);
            Vector3 eatFrom = new Vector3(0.55f, 0f, 0.45f);
            Vector3 away = bowlPos - eatFrom; away.y = 0f;
            Bowl = FoodBowl.Create(world, bowlPos, Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg);
            Cushion = Cushion.Create(world, new Vector3(-1.0f, 0.004f, 0.85f));

            var fx = new GameObject("Fx").AddComponent<FxPool>();
            fx.transform.SetParent(world, false);

            // 카메라
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Sky;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 60f;
            camGo.AddComponent<AudioListener>();
            if (IsIOSSimulator())
            {
                // 시뮬레이터 Metal은 MSAA 렌더 패스를 지원하지 않는다 (실기기는 4x 유지)
                cam.allowMSAA = false;
                if (GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                    urp.msaaSampleCount = 1;
            }
            IslandCam = camGo.AddComponent<IslandCamera>();

            // 고양이
            var catGo = new GameObject("Cat");
            catGo.transform.position = new Vector3(0f, 0f, -0.2f);
            catGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            catGo.AddComponent<CatRig>();
            Audio = catGo.AddComponent<CatAudio>();
            Cat = catGo.AddComponent<CatBrain>();
            Cat.bowl = Bowl;
            Cat.cushion = Cushion;
            Cat.audioOut = Audio;
            Cat.cam = camGo.transform;
            IslandCam.follow = catGo.transform;
            IslandCam.SnapNow();

            Router = new GameObject("TouchRouter").AddComponent<TouchRouter>();
            Router.cat = Cat;
            Router.bowl = Bowl;
            Router.cushion = Cushion;
            Router.islandCamera = IslandCam;
            Router.audioOut = Audio;

            gameObject.AddComponent<DebugOverlay>();
        }

        static string DeviceGenerationName()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return UnityEngine.iOS.Device.generation.ToString();
#else
            return "n/a";
#endif
        }

        static bool IsIOSSimulator()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SIMULATOR_UDID"));
#else
            return false;
#endif
        }

        static void SetupLighting()
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Palette.SunDay;
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sun.shadowBias = 0.03f;
            sun.shadowNormalBias = 0.25f;
            sunGo.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
            RenderSettings.sun = sun;

            // 하늘빛: 위는 푸르스름, 옆은 크림, 아래는 잔디 반사색 (기획서 3부 5장)
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.Hex("cfe6f4");
            RenderSettings.ambientEquatorColor = Palette.Hex("f6ead2");
            RenderSettings.ambientGroundColor = Palette.Hex("c4dfa0");
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
        }
    }

    /// <summary>테스트 진행용 정보 표시. 세 손가락 톡 또는 D 키로 켜고 끈다.</summary>
    public class DebugOverlay : MonoBehaviour
    {
        bool visible;
        bool threeDown;
        GUIStyle style;

        void Update()
        {
            int touches = UnityEngine.InputSystem.Touchscreen.current != null ? CountTouches() : 0;
            if (touches >= 3 && !threeDown) visible = !visible;
            threeDown = touches >= 3;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.dKey.wasPressedThisFrame) visible = !visible;
        }

        static int CountTouches()
        {
            int n = 0;
            foreach (var t in UnityEngine.InputSystem.Touchscreen.current.touches) if (t.press.isPressed) n++;
            return n;
        }

        void OnGUI()
        {
            if (!visible) return;
            var g = GameBootstrap.Instance;
            if (g == null || g.Cat == null) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height / 55f) };
            style.normal.textColor = Palette.Cocoa;
            var c = g.Cat;
            string s =
                $"state {c.State} ({c.StateTime:0.0}s)\n" +
                $"hunger {c.Needs.Hunger:0.00}  energy {c.Needs.Energy:0.00}\n" +
                $"pleasure {c.Pet.Pleasure:0.00}  purr {c.Pet.Purring}  trust {c.Pet.TrustLeft:0.0}\n" +
                $"affection Lv{c.Affection.Level} ({c.Affection.Points:0})\n" +
                $"zone {g.Router.LastZone}  stroke {g.Router.StrokeSpeed:0.00}\n" +
                $"fps {1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):0}";
            GUI.Label(new Rect(20, Screen.height * 0.06f, Screen.width - 40, Screen.height * 0.3f), s, style);
        }
    }
}
