using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        public CatTower Tower { get; private set; }
        public NavGrid Nav { get; private set; }
        public IslandCamera IslandCam { get; private set; }
        public TouchRouter Router { get; private set; }
        public CatAudio Audio { get; private set; }
        /// <summary>게임 규칙과 저장 (Scripts/Game). 화면(GameUI)과 섬(WorldSync)이 이것을 본다.</summary>
        public CatIsland.Game.Game Logic { get; private set; }
        public CatIsland.UI.GameUI UI { get; private set; }
        public WorldSync WorldLink { get; private set; }
        /// <summary>테스트: 저장소를 바꿔 끼운다 (기본은 기기 저장소). OpenCatMakerIfEmpty: 고양이가 없으면 만들기 창을 연다.</summary>
        public static Func<CatIsland.Game.IFileStore> NewFiles;
        public static bool OpenCatMakerIfEmpty = true;
        float logicTickAt;

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

            // 배치: 그릇은 오른쪽, 방석은 왼쪽 뒤, 캣타워는 오른쪽 뒤 (점프가 옆모습으로 보이게 -X 방향으로 오른다)
            Bowl = FoodBowl.Create(world, new Vector3(1.9f, 0.004f, 0.6f));
            Cushion = Cushion.Create(world, new Vector3(-1.9f, 0.004f, 1.3f));
            Tower = CatTower.Create(world, new Vector3(1.0f, 0.004f, 2.7f), -90f);

            // 길찾기: 고양이가 물건을 뚫고 지나가지 않게 장애물을 등록한다
            Nav = new NavGrid(GameConfig.IslandWalkRadius);
            Nav.Add(new Obstacle { name = "Bowl", item = Bowl.transform, center = Bowl.transform.position, radius = 0.17f });
            Nav.Add(new Obstacle { name = "Cushion", item = Cushion.transform, center = Cushion.transform.position, radius = 0.54f });
            Nav.Add(new Obstacle { name = "Tower", item = Tower.transform, center = Tower.transform.position, half = new Vector2(Tower.DeckSize.x * 0.5f + 0.04f, Tower.DeckSize.y * 0.5f + 0.04f), yaw = Tower.transform.eulerAngles.y });
            foreach (var (name, pos, r) in IslandBuilder.Solids) Nav.Add(new Obstacle { name = name, center = pos, radius = r });

            var fx = new GameObject("Fx").AddComponent<FxPool>();
            fx.transform.SetParent(world, false);

            // 카메라
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = WorldStyle.Sky;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;   // (색 보정: Neutral)
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 80f;
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
            catGo.transform.position = new Vector3(0f, 0f, -0.6f);
            catGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            catGo.AddComponent<CatRig>();
            Audio = catGo.AddComponent<CatAudio>();
            Cat = catGo.AddComponent<CatBrain>();
            Cat.bowl = Bowl;
            Cat.cushion = Cushion;
            Cat.tower = Tower;
            Cat.nav = Nav;
            Cat.audioOut = Audio;
            Cat.cam = camGo.transform;
            IslandCam.follow = catGo.transform;
            IslandCam.SnapNow();

            Router = new GameObject("TouchRouter").AddComponent<TouchRouter>();
            Router.cat = Cat;
            Router.bowl = Bowl;
            Router.cushion = Cushion;
            Router.tower = Tower;
            Router.islandCamera = IslandCam;
            Router.audioOut = Audio;

            // 게임 규칙 · 저장 · 화면
            var files = NewFiles?.Invoke() ?? new CatIsland.Game.DiskFiles(System.IO.Path.Combine(Application.persistentDataPath, "save"));
            Logic = new CatIsland.Game.Game(new CatIsland.Game.RealClock(), files, null, new CatIsland.Game.FakeAds(), new CatIsland.Game.FakeStore(), new CatIsland.Game.FakeGameCenter(), new CatIsland.Game.FakeNotifier());
            Logic.LoadOrNew();
            WorldLink = new WorldSync(this);
            UI = CatIsland.UI.GameUI.Create(Logic, WorldLink);
            WorldLink.Refresh();
            if (Logic.S.zonesUnlocked.Contains(1)) IslandBuilder.OpenYardGate(world.Find("Island"));
            CatIsland.UI.Press.OnPress = () => { if (Logic.S.hapticsOn) Haptics.Impact(ImpactStyle.Soft, .5f); };
            if (Logic.S.cats.Count == 0 && OpenCatMakerIfEmpty) CatIsland.UI.CatMaker.Open(UI);
            Cat.OnPetted = pleasure => { var c = Logic.S.cats.Find(x => x.status == "home"); if (c != null) Logic.Pet(c.uid, pleasure); };

            gameObject.AddComponent<DebugOverlay>();
            gameObject.AddComponent<PerfMonitor>();
            gameObject.AddComponent<BackgroundMusic>();
        }

        void Update()
        {
            if (Logic == null || Time.unscaledTime < logicTickAt) return;
            logicTickAt = Time.unscaledTime + 5f; Logic.Tick();
        }
        void OnApplicationPause(bool paused) { if (Logic == null) return; if (paused) Logic.Pause(); else Logic.Resume(); }
        void OnApplicationQuit() => Logic?.Pause();

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

        /// <summary>빛 (docs/ART_DIRECTION.md 2-1): 맑고 따뜻한 해 + 푸른 하늘빛·풀빛 주변광, Neutral 색 보정, 하늘색 안개.</summary>
        static void SetupLighting()
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Palette.Hex("fff0d0");
            sun.intensity = 1.47f;        // 시안 three.js 3.1 (이전 2.0 → 0.95 와 같은 비율)
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;    // 그늘은 검지 않게 (SoftLit 이 푸르스름하게 칠한다)
            sun.shadowBias = 0.3f;        // docs/ITEMS.md 그림자 설정 (깊이 0.3 / 법선 0.4)
            sun.shadowNormalBias = 0.4f;
            sunGo.transform.rotation = Quaternion.Euler(50f, 36f, 0f);   // 높이 약 50°, 왼쪽 앞에서
            RenderSettings.sun = sun;

            // 하늘빛: 위 #CDEEFF / 아래 #7FB85A (시안 반구광 1.15)
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.Hex("cdeeff") * 0.62f;
            RenderSettings.ambientEquatorColor = Color.Lerp(Palette.Hex("cdeeff"), Palette.Hex("7fb85a"), 0.5f) * 0.62f;
            RenderSettings.ambientGroundColor = Palette.Hex("7fb85a") * 0.62f;
            RenderSettings.ambientIntensity = 1f;

            // 먼 곳: 하늘색 안개 (거리는 카메라에 맞춰 WorldStyle 이 정한다)
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = WorldStyle.Sky;
            WorldStyle.Apply(new Vector3(0f, 0f, 0.4f), Vector3.forward, 8.5f);

            // 색 보정: Neutral, 노출 그대로
            var volGo = new GameObject("Look");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral);
            vol.sharedProfile = profile;
        }
    }

    /// <summary>프레임 시간 기록: 5초마다 평균 fps, 가장 긴 프레임, 25 ms 넘은 프레임 수를 로그로 남긴다 (실기기 버벅임 진단).</summary>
    public class PerfMonitor : MonoBehaviour
    {
        float acc, worst; int frames, slow;
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            acc += dt; frames++;
            worst = Mathf.Max(worst, dt);
            if (dt > 0.025f) slow++;
            if (acc >= 5f)
            {
                Debug.Log($"[CatIsland] perf fps={frames / acc:F1} worst={worst * 1000f:F1}ms slow(>25ms)={slow} target={Application.targetFrameRate} vsync={QualitySettings.vSyncCount}");
                acc = 0f; frames = 0; slow = 0; worst = 0f;
            }
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
