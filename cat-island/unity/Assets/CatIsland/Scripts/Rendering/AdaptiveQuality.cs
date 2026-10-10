using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CatIsland
{
    /// <summary>
    /// 화질 단계 고르기 (순수 규칙, 테스트 가능): 3초마다 평균 fps 를 받아, 모자라면 한 단계 낮추고(빨리),
    /// 넉넉함이 오래(15초) 이어지면 한 단계 올린다(천천히). 올렸다가 바로 다시 떨어지면 그 단계는 1분 동안 다시 올리지 않는다.
    /// </summary>
    public class QualityGovernor
    {
        public const int MaxLevel = 3;
        public const float Window = 3f;
        public int Level { get; private set; }
        readonly float target; float acc; int frames; int goodWindows; float lockUntil = -1f; int lockedLevel = -1; float clock;
        public QualityGovernor(int startLevel, float targetFps = 60f) { Level = Mathf.Clamp(startLevel, 0, MaxLevel); target = targetFps; }

        bool justRose;   // (바로 전에 한 단계 올렸다)
        /// <summary>프레임마다 (실제 걸린 시간). 단계가 바뀌면 true.</summary>
        public bool Tick(float dt)
        {
            clock += dt; acc += dt; frames++;
            if (acc < Window) return false;
            float fps = frames / acc; acc = 0f; frames = 0;
            if (fps < target * .87f && Level < MaxLevel)
            {
                // (올리자마자 떨어졌다: 그 좋은 단계는 1분 동안 다시 올리지 않는다)
                if (justRose) { lockedLevel = Level; lockUntil = clock + 60f; }
                justRose = false; Level++; goodWindows = 0; return true;
            }
            if (fps >= target * .98f)
            {
                justRose = false;
                if (Level > 0 && ++goodWindows >= 5 && !(Level - 1 == lockedLevel && clock < lockUntil)) { Level--; goodWindows = 0; justRose = true; return true; }
                return false;
            }
            goodWindows = 0; return false;
        }
    }

    /// <summary>
    /// 실행 중 화질 조절: 렌더 배율·MSAA·그림자 해상도를 QualityGovernor 단계에 맞추고, 그림자 거리는 카메라 거리에 맞춘다
    /// (멀리 보기 24 m 에서도 섬 끝까지 그림자). 설정 파일을 바꾸지 않게 실행 중 복사본에만 쓴다.
    /// 오래된 기기(메모리 4 GB 미만)는 한 단계 낮춰 시작한다.
    /// </summary>
    public class AdaptiveQuality : MonoBehaviour
    {
        static readonly (float scale, int msaa, int shadowRes)[] Levels = { (1f, 4, 4096), (.9f, 4, 2048), (.8f, 2, 2048), (.7f, 2, 1024) };
        public QualityGovernor Governor { get; private set; }
        public IslandCamera Cam;
        public static bool Enabled = true;   // (스크린샷·테스트는 끈다: 늘 같은 화질)
        UniversalRenderPipelineAsset urp;

        void Start()
        {
            int start = SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 4000 ? 1 : 0;
            Governor = new QualityGovernor(start);
            // (에디터에서는 설정을 건드리지 않는다: 실행 중에 바꾸면 프로젝트 설정 파일이 바뀔 수 있다)
            if (Application.isEditor || !(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset src)) return;
            urp = Instantiate(src); urp.name = src.name + " (실행 중)";
            GraphicsSettings.defaultRenderPipeline = urp; if (QualitySettings.renderPipeline) QualitySettings.renderPipeline = urp;
            Apply();
        }

        void Update()
        {
            if (!urp) return;
            if (Enabled && Governor.Tick(Time.unscaledDeltaTime)) { Apply(); Debug.Log($"[CatIsland] quality level {Governor.Level}"); }
            // 그림자 거리: 카메라에서 보는 곳까지 + 10 m (가까이 약 18 m, 멀리 약 34 m) - 멀리 보기에서도 섬 끝까지 그림자
            if (Cam) { float want = Mathf.Clamp(Cam.CurrentDistance + 10f, 18f, 36f); if (Mathf.Abs(urp.shadowDistance - want) > 1f) urp.shadowDistance = want; }
        }

        void Apply()
        {
            var (scale, msaa, res) = Levels[Governor.Level];
            urp.renderScale = scale;
            if (!IsSimulator()) urp.msaaSampleCount = msaa;   // (시뮬레이터는 MSAA 없음: GameBootstrap)
            urp.mainLightShadowmapResolution = res;
        }
        static bool IsSimulator() => !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SIMULATOR_UDID"));
    }
}
