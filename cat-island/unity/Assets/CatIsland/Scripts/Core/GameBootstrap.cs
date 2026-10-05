using System;
using System.Collections.Generic;
using System.Linq;
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
        public DayCycle Day { get; private set; }
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
            Day = gameObject.AddComponent<DayCycle>(); Day.sun = RenderSettings.sun; Day.cam = cam; Day.Apply(DayCycle.HourOverride >= 0 ? DayCycle.HourOverride : (float)DateTime.Now.TimeOfDay.TotalHours);

            // 고양이 (저장에 고양이가 있으면 SyncCats 가 저장대로 다시 세운다)
            Cat = SpawnCat("korean_shorthair", new Vector3(0f, 0f, -0.6f), 180f, null, camGo.transform);
            IslandCam.follow = Cat.transform;
            IslandCam.SnapNow();

            Router = new GameObject("TouchRouter").AddComponent<TouchRouter>();
            Router.cat = Cat; Router.cats.Add(Cat);
            Router.bowl = Bowl;
            Router.cushion = Cushion;
            Router.tower = Tower;
            Router.islandCamera = IslandCam;
            Router.audioOut = Audio;

            // 게임 규칙 · 저장 · 화면
            var files = NewFiles?.Invoke() ?? new CatIsland.Game.DiskFiles(System.IO.Path.Combine(Application.persistentDataPath, "save"));
#if UNITY_IOS && !UNITY_EDITOR
            // 아이폰: 기기 서비스 (알림·iCloud·Game Center·결제). 광고 회사 모듈은 앱 ID 를 받은 뒤 (docs/RELEASE_TODO.md)
            var ios = CatIsland.Game.IosServices.Create();
            Logic = new CatIsland.Game.Game(new CatIsland.Game.RealClock(), files, ios, new CatIsland.Game.NoAds(), ios, ios, ios);
            ios.OnUnsolicited = () => { Logic.RestorePending(); UI?.Refresh(); };
#else
            Logic = new CatIsland.Game.Game(new CatIsland.Game.RealClock(), files, null, new CatIsland.Game.FakeAds(), new CatIsland.Game.FakeStore(), new CatIsland.Game.FakeGameCenter(), new CatIsland.Game.FakeNotifier());
#endif
            Logic.LoadOrNew();
            WorldLink = new WorldSync(this);
            UI = CatIsland.UI.GameUI.Create(Logic, WorldLink);
            WorldLink.Refresh();
            if (Logic.S.zonesUnlocked.Contains(1)) IslandBuilder.OpenYardGate(world.Find("Island"));
            CatIsland.UI.Press.OnPress = () => { if (Logic.S.hapticsOn) Haptics.Impact(ImpactStyle.Soft, .5f); };
            if (Logic.S.cats.Count == 0 && OpenCatMakerIfEmpty) CatIsland.UI.CatMaker.Open(UI);
            SyncCats(); SyncGuest();
            Router.BeforeBowlFill = () =>
            {
                if (Logic.S.cats.Count == 0) return true;      // (첫 고양이를 만들기 전 미리보기)
                var hungry = Logic.HomeCats.OrderBy(c => c.hunger).FirstOrDefault(); if (hungry == null) return false;
                if (Logic.Feed(hungry.uid)) { UI.Refresh(); return true; }
                if (Logic.AdAvailable(CatIsland.Game.Catalog.AdSpot.FreeFood)) UI.Toast("사료가 떨어졌어요. 고양이 메뉴에서 광고로 한 봉지 받을 수 있어요");
                else UI.Toast("사료가 떨어졌어요. 상점에서 사 와요");
                return false;
            };

            gameObject.AddComponent<DebugOverlay>();
            gameObject.AddComponent<PerfMonitor>();
            gameObject.AddComponent<BackgroundMusic>();
        }

        CatBrain SpawnCat(string breed, Vector3 pos, float yaw, CatIsland.Game.CatData data, Transform camT)
        {
            var go = new GameObject(data != null ? "Cat_" + data.name : "Cat"); go.SetActive(false);
            go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var rig = go.AddComponent<CatRig>(); rig.breed = breed;
            var audio = go.AddComponent<CatAudio>(); var brain = go.AddComponent<CatBrain>();
            brain.bowl = Bowl; brain.cushion = Cushion; brain.tower = Tower; brain.nav = Nav; brain.audioOut = audio; brain.cam = camT; brain.Data = data;
            if (Audio == null) Audio = audio;
            go.SetActive(true);
            return brain;
        }

        readonly Dictionary<string, CatBrain> catViews = new Dictionary<string, CatBrain>();
        /// <summary>섬의 3D 고양이를 저장과 맞춘다: 집에 있는 고양이마다 하나 (산책 중·별나라는 섬에 없다).</summary>
        public void SyncCats()
        {
            if (Logic == null || Logic.S.cats.Count == 0) return;
            if (catViews.Count == 0 && Cat && Cat.Data == null) { Router.cats.Remove(Cat); Destroy(Cat.gameObject); Cat = null; }   // (미리보기 고양이 치우기)
            var cam = IslandCam.transform; int i = 0;
            foreach (var c in Logic.S.cats)
            {
                bool home = c.status == "home";
                if (!catViews.TryGetValue(c.uid, out var view) || !view)
                {
                    if (!home) continue;
                    var spot = Nav.NearestFree(new Vector3(-0.8f + 0.9f * i, 0f, -0.6f - 0.3f * (i % 2)));
                    view = SpawnCat(HasArt(c.breed) ? c.breed : "korean_shorthair", spot, 180f, c, cam);
                    view.OnPetted = p => Logic.Pet(c.uid, p);
                    view.OnUsedItem = id => { Logic.PlayWith(c.uid, id); UI?.Refresh(); };
                    catViews[c.uid] = view; Router.cats.Add(view);
                }
                // 산책: 걸어 나가서 사라지고, 돌아오면 선물을 물고 걸어 들어온다
                if (!home && view.gameObject.activeSelf && view.State != CatState.LeaveForWalk) { var v = view; v.LeaveForWalk(() => v.gameObject.SetActive(false)); }
                else if (!home && view.State != CatState.LeaveForWalk) view.gameObject.SetActive(false);
                else if (home && !view.gameObject.activeSelf)
                {
                    view.gameObject.SetActive(true);
                    var gift = ItemLoader.Spawn("ball", null, Vector3.zero, 0); if (gift) foreach (var col in gift.GetComponentsInChildren<Collider>()) Destroy(col);
                    view.ReturnFromWalk(gift);
                }
                i++;
            }
            foreach (var kv in catViews.Where(kv => kv.Value && Logic.Cat(kv.Key) == null).ToList()) { Router.cats.Remove(kv.Value); Destroy(kv.Value.gameObject); catViews.Remove(kv.Key); }
            var first = Logic.HomeCats.Select(c => catViews.TryGetValue(c.uid, out var v) ? v : null).FirstOrDefault(v => v);
            if (first) { Cat = first; Router.cat = first; IslandCam.follow = first.transform; if (Cat.Data != null) Audio = Cat.GetComponent<CatAudio>(); }
        }
        CatRig guestView; string guestBreedShown;
        /// <summary>손님 고양이: 낮에 섬 앞 오른쪽에 앉아 있다 (간식을 주기 전, 또는 같이 살 수 있을 때). 누르면 손님 창.</summary>
        public void SyncGuest()
        {
            bool show = Logic != null && Logic.S.cats.Count > 0 && Logic.GuestHere && (!Logic.S.guestTreated || Logic.CanAdoptGuest) && HasArt(Logic.S.guestBreed);
            if (!show || guestBreedShown != Logic.S.guestBreed) { if (guestView) Destroy(guestView.gameObject); guestView = null; guestBreedShown = null; }
            if (!show || guestView) return;
            var go = new GameObject("GuestCat"); go.SetActive(false);
            var spot = Nav.NearestFree(new Vector3(2.9f, 0f, -2.1f)); go.transform.position = spot; go.transform.rotation = Quaternion.Euler(0, 200f, 0);
            guestView = go.AddComponent<CatRig>(); guestView.breed = Logic.S.guestBreed; go.SetActive(true); guestView.Request(Posture.Sit);
            guestBreedShown = Logic.S.guestBreed;
            Router.TapOther = c => { if (guestView && guestView.IsCatCollider(c)) { UI.OpenGuest(); return true; } return false; };
        }

        /// <summary>츄르 주기: 사료처럼 저장의 츄르를 쓰고, 고양이가 앞으로 와서 핥는다 (츄르만 입 앞에 떠 있다).</summary>
        public bool GiveChuru(string uid)
        {
            if (!catViews.TryGetValue(uid, out var v) || !v || !v.isActiveAndEnabled) return false;
            if (!Logic.Feed(uid, "churu")) return false;
            GameObject prop = null;
            return v.GiveTreat((pos, rot) => { prop = ItemLoader.Spawn("churu", null, pos, 0); if (prop) { prop.transform.rotation = rot * Quaternion.Euler(90, 0, 0); foreach (var col in prop.GetComponentsInChildren<Collider>()) Destroy(col); } }, () => { if (prop) Destroy(prop); UI?.Refresh(); });
        }

        /// <summary>새 용품: 가장 가까운 한가한 고양이가 바로 써 본다.</summary>
        public void OnNewItem(ItemTag tag)
        {
            var cats = catViews.Values.Where(v => v && v.isActiveAndEnabled).OrderBy(v => Vector3.Distance(v.transform.position, tag.transform.position));
            foreach (var v in cats) if (v.TryNewItem(tag)) return;
        }

        static bool HasArt(string breed) => Resources.Load<TextAsset>("Art/Cats/" + breed + "_info") != null;

        void Update()
        {
            if (Logic == null || Time.unscaledTime < logicTickAt) return;
            logicTickAt = Time.unscaledTime + 5f; Logic.Tick(); SyncGuest();
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
