using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CatIsland.EditorTools
{
    /// <summary>
    /// 파이프라인(cat-island/assets)에서 가져온 고양이·용품 FBX를 게임에 쓸 수 있게 만든다.
    /// - 고양이: Generic 리그(Root), 동작 반복 여부(clips.json), 내장 텍스처 꺼내기, 우리 셰이더 재질,
    ///   Animator(걷기·종종걸음·달리기 블렌드 + 나머지 동작), 점프 이동 곡선(JumpUp) 기록
    /// - 용품: 색·노멀 텍스처를 쓰는 우리 셰이더 재질
    /// 결과 프리팹: Assets/CatIsland/Resources/Art/Cats/<품종>.prefab, .../Items/<용품>.prefab
    /// </summary>
    public static class CatArtImport
    {
        const string Art = "Assets/CatIsland/Art";
        const string OutRes = "Assets/CatIsland/Resources/Art";

        [Serializable] class XZ { public float x; public float z; }
        [Serializable] class DrinkInfo { public XZ bowl; }
        [Serializable] class ClipInfo { public string name; public float dur; public bool loop; public bool rootMotion; public DrinkInfo drink; }
        [Serializable] class CushionSpot { public float x; public float z; public float catLift; }
        [Serializable] class ItemSpots { public CushionSpot cushion; }
        [Serializable] class SkipInfo { public string clip; public string reason; public string playInstead; }
        [Serializable] class ClipsJson { public string id; public ClipInfo[] clips; public SkipInfo[] skippedClips; public ItemSpots itemSpots; }

        [Serializable] public class RootCurve { public string clip; public float fps; public float[] forward; public float[] up; }
        [Serializable] public class CatArtInfo
        {
            public string id;
            public string[] clips;
            public string[] loops;
            public RootCurve jump;
            public float headRadius;
            public float bowlX, bowlZ, cushionZ, cushionLift;
            public Vector3 flopBelly;   // 발라당(FlopIdle) 자세에서 배가 향하는 방향 (고양이 기준)
        }

        [MenuItem("CatIsland/Import Art")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(OutRes + "/Cats");
            Directory.CreateDirectory(OutRes + "/Items");
            Directory.CreateDirectory(Art + "/Materials");
            Directory.CreateDirectory(Art + "/Animation");

            var manifest = ReadManifest();
            foreach (var cat in manifest.cats) ImportCat(cat);
            foreach (var item in manifest.items) ImportItem(item);
            AssetDatabase.SaveAssets();
            Debug.Log("[CatArtImport] OK");
        }

        [Serializable] class Manifest { public string[] cats; public string[] items; }

        static Manifest ReadManifest()
        {
            string path = Path.Combine(Application.dataPath, "../tools/art_manifest.json");
            return JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
        }

        // ------------------------------------------------------------------ 고양이

        static void ImportCat(string id)
        {
            string fbx = $"{Art}/Cats/{id}.fbx";
            var json = JsonUtility.FromJson<ClipsJson>(File.ReadAllText($"{Art}/Cats/{id}.clips.json"));
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (imp == null) { Debug.LogError("[CatArtImport] missing " + fbx); return; }

            // 1. 리그와 동작
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importBlendShapes = true;
            imp.importAnimation = true;
            imp.motionNodeName = FindMotionNode(fbx);
            var loops = new HashSet<string>(json.clips.Where(c => c.loop).Select(c => c.name));
            var clips = imp.defaultClipAnimations.Select(c =>
            {
                string shortName = c.name.Contains("|") ? c.name.Substring(c.name.LastIndexOf('|') + 1) : c.name;
                c.name = shortName;
                c.loopTime = loops.Contains(shortName);
                c.loopPose = false;
                // 루트 이동은 모두 뽑아낸다 → 동작은 제자리에서 재생되고, 이동은 게임 코드가 한다
                c.lockRootRotation = true;
                c.lockRootHeightY = false;
                c.lockRootPositionXZ = false;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
                return c;
            }).ToArray();
            imp.clipAnimations = clips;
            imp.SaveAndReimport();

            // 2. 내장 텍스처 꺼내기
            string texDir = $"{Art}/Cats/{id}_tex";
            if (!Directory.Exists(texDir) || Directory.GetFiles(texDir, "*.jpg").Length + Directory.GetFiles(texDir, "*.png").Length == 0)
            {
                Directory.CreateDirectory(texDir);
                imp.ExtractTextures(texDir);
                AssetDatabase.Refresh();
            }
            foreach (var p in Directory.GetFiles(texDir).Where(f => f.EndsWith(".png") || f.EndsWith(".jpg")))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                bool normal = p.Contains("normal");
                bool changed = false;
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
                if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; changed = true; }
                if (changed) ti.SaveAndReimport();
            }

            // 3. 재질: 원래 재질의 색을 읽어 우리 셰이더로
            // 원본(FBX 내장) 재질 이름. 이미 바꿔 끼운 것은 외부 연결표에서 이름을 읽는다
            var srcNames = SourceMaterialNames(imp, fbx);
            // 얼굴 색 아틀라스 (sync_art.py 가 glb 에서 꺼냄). 색 칸이 섞이지 않게 점 필터
            string facePath = $"{Art}/Cats/{id}_face.png";
            var fti = (TextureImporter)AssetImporter.GetAtPath(facePath);
            if (fti != null && (fti.filterMode != FilterMode.Point || fti.mipmapEnabled || fti.textureCompression != TextureImporterCompression.Uncompressed))
            {
                fti.filterMode = FilterMode.Point;
                fti.mipmapEnabled = false;
                fti.textureCompression = TextureImporterCompression.Uncompressed;
                fti.SaveAndReimport();
            }
            var faceTex = AssetDatabase.LoadAssetAtPath<Texture2D>(facePath);
            if (faceTex == null) Debug.LogError("[CatArtImport] face atlas missing: run tools/sync_art.py");
            var coat = LoadTex(texDir, "coat");
            var normalTex = LoadTex(texDir, "normal");
            foreach (var srcName in srcNames)
            {
                var m = SoftMat($"{Art}/Materials/{id}_{srcName}.mat");
                if (srcName.EndsWith("Coat"))
                {
                    m.SetTexture("_BaseMap", coat);
                    if (normalTex) { m.SetTexture("_BumpMap", normalTex); m.EnableKeyword("_NORMALMAP"); }
                    m.SetColor("_BaseColor", Color.white);
                    m.SetFloat("_Gloss", 0f);
                }
                else if (srcName.Contains("Highlight"))
                {
                    // 눈 반짝임: 빛과 무관하게 밝게 (순백 대신 크림빛)
                    m.SetTexture("_BaseMap", null);
                    m.SetColor("_BaseColor", new Color(1f, 0.98f, 0.94f));
                    m.SetFloat("_Gloss", 0f);
                    m.SetFloat("_Emission", 1f);
                    m.SetFloat("_RimStrength", 0f);
                    m.SetFloat("_GroundAO", 0f);
                }
                else
                {
                    m.SetTexture("_BaseMap", faceTex);
                    m.SetColor("_BaseColor", Color.white);
                    m.SetFloat("_Gloss", srcName.Contains("Gloss") ? 0.85f : 0f);
                    m.SetFloat("_Emission", 0f);
                    m.SetFloat("_RimStrength", 0.05f);
                    m.SetFloat("_GroundAO", 0f);
                }
                EditorUtility.SetDirty(m);
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), srcName), m);
                Debug.Log($"[CatArtImport] {id} material {srcName} -> {m.name} tex={(m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "-")}");
            }
            imp.SaveAndReimport();

            // 4. Animator
            var allClips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
            var skipped = new HashSet<string>((json.skippedClips ?? new SkipInfo[0]).Select(s => s.clip));
            string ctrlPath = $"{Art}/Animation/{id}.controller";
            AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = ctrl.layers[0].stateMachine;
            AnimationClip Clip(string n) => allClips.FirstOrDefault(c => c.name == n);

            var loco = new BlendTree { name = "Locomotion", blendParameter = "Speed", blendType = BlendTreeType.Simple1D, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(loco, ctrl);
            loco.AddChild(Clip("Idle"), 0f);
            loco.AddChild(Clip("Walk"), 0.4f);
            loco.AddChild(Clip("Trot"), 1.06f);
            if (Clip("Gallop") && !skipped.Contains("Gallop")) loco.AddChild(Clip("Gallop"), 2.27f);
            var locoState = sm.AddState("Locomotion");
            locoState.motion = loco;
            sm.defaultState = locoState;
            foreach (var c in allClips)
            {
                if (skipped.Contains(c.name)) continue;
                var st = sm.AddState(c.name);
                st.motion = c;
            }
            EditorUtility.SetDirty(ctrl);

            // 5. 점프 이동 곡선 (게임 코드가 오르기·내리기에 같이 쓴다)
            var info = new CatArtInfo
            {
                id = id,
                clips = allClips.Where(c => !skipped.Contains(c.name)).Select(c => c.name).ToArray(),
                loops = loops.ToArray(),
                jump = SampleRoot(Clip("JumpUp")),
            };
            var drink = json.clips.FirstOrDefault(c => c.name == "Drink")?.drink;
            if (drink?.bowl != null) { info.bowlX = drink.bowl.x; info.bowlZ = drink.bowl.z; }
            if (json.itemSpots?.cushion != null) { info.cushionZ = json.itemSpots.cushion.z; info.cushionLift = json.itemSpots.cushion.catLift; }

            // 6. 프리팹
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var anim = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            info.flopBelly = MeasureFlopBelly(inst, Clip("FlopIdle"));
            var head = inst.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
            var face = inst.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == "Face");
            info.headRadius = Mathf.Max(face.bounds.extents.x, face.bounds.extents.y) * 0.95f;
            PrefabUtility.SaveAsPrefabAsset(inst, $"{OutRes}/Cats/{id}.prefab");
            UnityEngine.Object.DestroyImmediate(inst);

            File.WriteAllText($"{OutRes}/Cats/{id}_info.json", JsonUtility.ToJson(info, true));
            AssetDatabase.ImportAsset($"{OutRes}/Cats/{id}_info.json");
            Debug.Log($"[CatArtImport] cat {id}: motionNode='{imp.motionNodeName}' clips={info.clips.Length} skipped=[{string.Join(",", skipped)}] jumpFrames={info.jump?.forward?.Length} headR={info.headRadius:F3}");
        }

        /// <summary>FlopIdle 첫 프레임을 재생해 등뼈(Spine2)의 "배 쪽"이 고양이 기준 어디를 향하는지 잰다.</summary>
        static Vector3 MeasureFlopBelly(GameObject inst, AnimationClip flop)
        {
            if (flop == null) return Vector3.down;
            var spine = inst.GetComponentsInChildren<Transform>().First(t => t.name == "Spine2");
            var bind = spine.rotation;
            var saved = inst.GetComponentsInChildren<Transform>().Select(t => (t, t.localPosition, t.localRotation, t.localScale)).ToList();
            flop.SampleAnimation(inst, 0f);
            Vector3 bellyWorld = spine.rotation * Quaternion.Inverse(bind) * Vector3.down;
            Vector3 local = inst.transform.InverseTransformDirection(bellyWorld);
            foreach (var (t, p, r, sc) in saved) { t.localPosition = p; t.localRotation = r; t.localScale = sc; }
            return local.normalized;
        }

        static string FindMotionNode(string fbx)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var root = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Root");
            if (root == null) return "";
            return AnimationUtility.CalculateTransformPath(root, model.transform);
        }

        static RootCurve SampleRoot(AnimationClip clip)
        {
            if (clip == null) return null;
            // 원본 곡선: Root 의 로컬 위치. 부모 회전 때문에 -y = 앞, z = 위 (Blender 축)
            var bindings = AnimationUtility.GetCurveBindings(clip).Where(b => b.path.EndsWith("Root") && b.propertyName.StartsWith("m_LocalPosition")).ToList();
            AnimationCurve cy = null, cz = null;
            foreach (var b in bindings)
            {
                if (b.propertyName.EndsWith(".y")) cy = AnimationUtility.GetEditorCurve(clip, b);
                if (b.propertyName.EndsWith(".z")) cz = AnimationUtility.GetEditorCurve(clip, b);
            }
            if (cy == null || cz == null) { Debug.LogWarning("[CatArtImport] JumpUp root curves not found"); return null; }
            const float fps = 30f;
            int n = Mathf.RoundToInt(clip.length * fps) + 1;
            var rc = new RootCurve { clip = clip.name, fps = fps, forward = new float[n], up = new float[n] };
            for (int i = 0; i < n; i++)
            {
                float t = i / fps;
                rc.forward[i] = -cy.Evaluate(t);
                rc.up[i] = cz.Evaluate(t);
            }
            return rc;
        }

        static List<string> SourceMaterialNames(ModelImporter imp, string fbx)
        {
            var names = imp.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).Select(k => k.name).ToList();
            names.AddRange(AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>().Select(m => m.name));
            return names.Distinct().ToList();
        }

        static Texture2D LoadTex(string dir, string contains)
        {
            var p = Directory.GetFiles(dir).FirstOrDefault(f => Path.GetFileName(f).Contains(contains) && !f.EndsWith(".meta"));
            return p == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }

        static Material SoftMat(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("CatIsland/SoftLit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("CatIsland/SoftLit");
            m.enableInstancing = true;
            return m;
        }

        // ------------------------------------------------------------------ 용품

        static void ImportItem(string id)
        {
            string dir = $"{Art}/Items/{id}";
            string fbx = $"{dir}/{id}.fbx";
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (imp == null) { Debug.LogError("[CatArtImport] missing " + fbx); return; }
            imp.importAnimation = false;
            imp.animationType = ModelImporterAnimationType.None;

            var normalPath = $"{dir}/{id}_normal.png";
            var ti = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            if (ti != null && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }

            var m = SoftMat($"{Art}/Materials/item_{id}.mat");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{id}_color.jpg"));
            var nt = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (nt) { m.SetTexture("_BumpMap", nt); m.EnableKeyword("_NORMALMAP"); }
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_GroundAO", 0.12f);
            EditorUtility.SetDirty(m);
            foreach (var srcName in SourceMaterialNames(imp, fbx))
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), srcName), m);
            imp.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            PrefabUtility.SaveAsPrefabAsset(inst, $"{OutRes}/Items/{id}.prefab");
            UnityEngine.Object.DestroyImmediate(inst);
            File.Copy($"{dir}/{id}.json", $"{OutRes}/Items/{id}_info.json", true);
            AssetDatabase.ImportAsset($"{OutRes}/Items/{id}_info.json");
            Debug.Log($"[CatArtImport] item {id} OK");
        }
    }
}
