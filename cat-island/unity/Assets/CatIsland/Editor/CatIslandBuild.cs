using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CatIsland.EditorTools
{
    /// <summary>iOS Xcode 프로젝트 생성. 서명과 실기 설치는 Xcode에서 개발자 계정으로.</summary>
    public static class CatIslandBuild
    {
        static string OutDir(string name) => Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds", name));

        [MenuItem("CatIsland/Build iOS (Simulator)")]
        public static void BuildIOSSimulator() => Build(iOSSdkVersion.SimulatorSDK, "iOS-Simulator");

        [MenuItem("CatIsland/Build iOS (Device)")]
        public static void BuildIOSDevice() => Build(iOSSdkVersion.DeviceSDK, "iOS-Device");

        static void Build(iOSSdkVersion sdk, string folder)
        {
            CatIslandSetup.Run();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
            PlayerSettings.iOS.sdkVersion = sdk;
            EditorUserBuildSettings.development = false; // Unity 6 iOS 프로필 기본값이 개발 빌드라 명시적으로 끈다
            // Apple Silicon Mac의 시뮬레이터는 arm64 (기본값 x86_64면 대상 기기를 못 찾음)
            PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;
            // 출시 빌드: 보통 로그에는 호출 경로를 붙이지 않는다 (로그마다 비용, 기기 기록이 지저분해짐). 경고·오류·예외는 그대로
            PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/CatIsland/Scenes/Main.unity" },
                locationPathName = OutDir(folder),
                target = BuildTarget.iOS,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[CatIslandBuild] {folder}: {s.result} errors={s.totalErrors} warnings={s.totalWarnings} size={s.totalSize} out={opts.locationPathName}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
