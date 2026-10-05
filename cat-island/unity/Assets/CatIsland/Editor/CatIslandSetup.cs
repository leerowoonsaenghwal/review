using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CatIsland.EditorTools
{
    /// <summary>
    /// Editor 작업 자동화. 배치 모드에서 -executeMethod 로 부른다.
    ///   CatIsland.EditorTools.CatIslandSetup.Run        프로젝트 설정 + URP + 재질 + 장면
    ///   CatIsland.EditorTools.CatIslandBuild.BuildIOSSimulator / BuildIOSDevice
    /// 여러 번 실행해도 결과가 같다.
    /// </summary>
    public static class CatIslandSetup
    {
        const string Root = "Assets/CatIsland";
        const string SettingsDir = Root + "/Settings";
        const string ScenePath = Root + "/Scenes/Main.unity";

        [MenuItem("CatIsland/Setup Project")]
        public static void Run()
        {
            Directory.CreateDirectory(SettingsDir);
            CatArtImport.Run();
            SetupPlayer();
            SetupUrp();
            SetupMaterials();
            SetupScene();
            SetupNativePlugins();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CatIslandSetup] OK");
        }

        static void SetupPlayer()
        {
            PlayerSettings.companyName = "rowoon";
            PlayerSettings.productName = "고양이 섬";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.rowoon.CatIsland");
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.iOS.buildNumber = "1";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetOSVersionString = "16.0";
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.appleDeveloperTeamID = "53JSX9F7FL"; // RRR과 같은 개발자 팀
            PlayerSettings.statusBarHidden = true;
            PlayerSettings.runInBackground = false;

            // 입력: 새 입력 시스템만 사용
            var ps = Resources.FindObjectsOfTypeAll<PlayerSettings>();
            if (ps.Length > 0)
            {
                var so = new SerializedObject(ps[0]);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue != 1)
                {
                    prop.intValue = 1;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("[CatIslandSetup] activeInputHandler = InputSystem (restart applies)");
                }
            }
        }

        static void SetupUrp()
        {
            string rendererPath = SettingsDir + "/CatIsland_Renderer.asset";
            string urpPath = SettingsDir + "/CatIsland_URP.asset";

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }

            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(urpPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(urp, urpPath);
            }

            var so = new SerializedObject(urp);
            SetInt(so, "m_MainLightShadowmapResolution", 2048);
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetFloat(so, "m_ShadowDistance", 24f); // 카메라가 11~19 m 떨어져 있어 섬 전체가 들어가게
            SetInt(so, "m_ShadowCascadeCount", 1);
            SetInt(so, "m_MSAA", 4);
            SetBool(so, "m_SupportsHDR", false);
            SetFloat(so, "m_RenderScale", 1f);
            SetInt(so, "m_AdditionalLightsRenderingMode", 0);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        static void SetInt(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; else Debug.LogWarning("[CatIslandSetup] missing " + name); }
        static void SetBool(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; else Debug.LogWarning("[CatIslandSetup] missing " + name); }
        static void SetFloat(SerializedObject so, string name, float v) { var p = so.FindProperty(name); if (p != null) p.floatValue = v; else Debug.LogWarning("[CatIslandSetup] missing " + name); }

        static void SetupMaterials()
        {
            MakeMaterial(Root + "/Resources/CatSoftLit.mat", "CatIsland/SoftLit");
            MakeMaterial(Root + "/Resources/CatBillboard.mat", "CatIsland/Billboard");
        }

        static void MakeMaterial(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogError("[CatIslandSetup] shader not found: " + shaderName); return; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = shader;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }

        static void SetupNativePlugins()
        {
            // 골골 진동 플러그인은 CoreHaptics 프레임워크가 필요하다
            var imp = AssetImporter.GetAtPath("Assets/Plugins/iOS/CatHaptics.mm") as PluginImporter;
            if (imp == null) { Debug.LogError("[CatIslandSetup] CatHaptics.mm importer not found"); return; }
            imp.SetCompatibleWithAnyPlatform(false);
            imp.SetCompatibleWithEditor(false);
            imp.SetCompatibleWithPlatform(BuildTarget.iOS, true);
            imp.SetPlatformData(BuildTarget.iOS, "FrameworkDependencies", "CoreHaptics;");
            imp.SaveAndReimport();
        }

        static void SetupScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Bootstrap");
            go.AddComponent<GameBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
