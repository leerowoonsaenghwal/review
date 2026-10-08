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
        [Serializable] class JumpInfo { public float D; public float H; public float deckBack; public float turnIn; public float edge; }
        [Serializable] class ClipInfo { public string name; public float dur; public bool loop; public bool rootMotion; public DrinkInfo drink; public JumpInfo jump; public LickUpInfo lickUp; }
        [Serializable] class CushionSpot { public float x; public float z; public float catLift; public float yaw; }
        [Serializable] class ItemSpots { public CushionSpot cushion; public CushionSpot hideout; public CushionSpot mouse_toy; }
        [Serializable] class LickUpInfo { public float[] tip; }
        [Serializable] class SkipInfo { public string clip; public string reason; public string playInstead; }
        [Serializable] class FaceInfo { public string eye; public string whisker; }
        [Serializable] class ClipsJson { public string id; public ClipInfo[] clips; public SkipInfo[] skippedClips; public ItemSpots itemSpots; public FaceInfo face; }

        [Serializable] public class RootCurve { public string clip; public float fps; public float[] forward; public float[] up; }
        [Serializable] public class CatArtInfo
        {
            public string id;
            public string[] clips;
            public string[] loops;
            public RootCurve jump;
            public RootCurve jumpDown;   // JumpDown 이 있는 에셋만
            public float headRadius;
            public float bowlX, bowlZ, cushionZ, cushionLift;
            public Vector3 flopBelly;   // 발라당(FlopIdle) 자세에서 배가 향하는 방향 (고양이 기준)
            public float jumpD, jumpH, deckBack, turnIn; // clips.json JumpUp.jump: 판 뒤쪽 끝이 출발점에서 deckBack 앞 (qa_items 와 같은 배치)
            public JumpSet[] jumps;     // 높이별 점프 (CatRig.JumpSet 과 같은 모양)
            public float toyX, toyZ = .35f, toyYaw, hideZ = -.2f, hideLift = .08f; public Vector3 lickTip = new Vector3(0, .5f, .45f); public string faceEye = "", faceWhisker = "";   // (쥐돌이·숨숨집·츄르 자리: 고양이 루트 기준)
        }
        [Serializable] public class JumpSet { public string clip; public bool up; public float H, D, deckBack, turnIn, edge; public RootCurve curve; }

        /// <summary>사진 고양이 털 (CatCoat): 털 마스크는 색이 아닌 비율이라 sRGB 끔·512, 덧입히는 털 그림은 2048.</summary>
        static void CoatTextures()
        {
            foreach (var path in Directory.GetFiles(OutRes + "/Cats").Where(f => f.EndsWith("_coatmask.png") || f.EndsWith("_coat.jpg")))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                if (ti == null) continue;
                bool mask = path.EndsWith("_coatmask.png"); int size = mask ? 512 : 2048;   // (마스크는 넓은 색 면이라 작게: 앱 크기)
                var comp = mask ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Compressed;
                if (ti.sRGBTexture == !mask && ti.maxTextureSize == size && ti.textureCompression == comp) continue;
                ti.sRGBTexture = !mask; ti.maxTextureSize = size; ti.mipmapEnabled = true; ti.textureCompression = comp;
                if (mask) { ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false; }
                ti.SaveAndReimport();
            }
        }

        /// <summary>
        /// 휴대폰에 맞는 텍스처: 최대 크기 + 아이폰 ASTC (색 6x6, 노멀 5x5). 바뀌었으면 true.
        /// 고양이 털 1024: 가장 가까운 사진 시점에서도 몸이 화면의 절반이 안 된다. 노멀맵은 이미 40 % 로 약하게 쓰므로 512.
        /// </summary>
        static bool MobileTex(TextureImporter ti, int size, bool normal)
        {
            var ios = ti.GetPlatformTextureSettings("iPhone");
            var fmt = normal ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6;
            if (ti.maxTextureSize == size && ios.overridden && ios.maxTextureSize == size && ios.format == fmt) return false;
            ti.maxTextureSize = size;
            ios.overridden = true; ios.maxTextureSize = size; ios.format = fmt; ios.compressionQuality = 80;
            ti.SetPlatformTextureSettings(ios);
            return true;
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
            CoatTextures();
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
            // 앱 크기: 동작은 아주 작은 오차만 허용하는 키 줄이기 (회전 0.2°, 위치 0.2 %)
            imp.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            imp.animationRotationError = .2f; imp.animationPositionError = .2f; imp.animationScaleError = .5f;
            imp.meshCompression = ModelImporterMeshCompression.Off;   // (고양이는 압축 없음: 1 mm 수염·눈꺼풀 선이 격자에 맞춰 조각난다)
            imp.isReadable = true;   // (CatFace: 고른 눈·수염 조각만 합친 메시를 만들려면 얼굴 삼각형을 읽어야 한다)
            imp.SaveAndReimport();

            // 2. 내장 텍스처 꺼내기
            // FBX 가 바뀌면 텍스처도 다시 꺼낸다: 다시 만든 고양이는 UV 가 새로 펼쳐져 옛 텍스처가 맞지 않는다
            string texDir = $"{Art}/Cats/{id}_tex";
            string stamp = $"{texDir}/source.sha";
            string fbxHash = Hash128.Compute(File.ReadAllBytes(fbx)).ToString();
            bool fresh = Directory.Exists(texDir) && File.Exists(stamp) && File.ReadAllText(stamp) == fbxHash
                && Directory.GetFiles(texDir, "*.jpg").Length + Directory.GetFiles(texDir, "*.png").Length > 0;
            if (!fresh)
            {
                if (Directory.Exists(texDir)) AssetDatabase.DeleteAsset(texDir);
                Directory.CreateDirectory(texDir);
                imp.ExtractTextures(texDir);
                File.WriteAllText(stamp, fbxHash);
                AssetDatabase.Refresh();
                Debug.Log($"[CatArtImport] {id}: textures extracted from the current FBX");
            }
            foreach (var p in Directory.GetFiles(texDir).Where(f => f.EndsWith(".png") || f.EndsWith(".jpg")))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                bool normal = p.Contains("normal");
                bool changed = false;
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
                changed |= MobileTex(ti, normal ? 512 : 1024, normal);
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
                    // 노멀맵은 약하게: 구운 털 뭉침이 작은 화면에서 자글자글한 잡티로 보인다 (동물의 숲처럼 매끈하게)
                    m.SetFloat("_BumpScale", 0.3f);
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
            // 동작 구성이 같으면 Animator 를 다시 만들지 않는다 (git 에 의미 없는 변경이 쌓이지 않게)
            string signature = string.Join(",", allClips.Where(c => !skipped.Contains(c.name)).Select(c => c.name).OrderBy(n => n)) + "|v2";
            string sigPath = $"{Art}/Animation/{id}.controller.sig";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            bool rebuild = existing == null || !File.Exists(sigPath) || File.ReadAllText(sigPath) != signature;
            if (rebuild) { AssetDatabase.DeleteAsset(ctrlPath); File.WriteAllText(sigPath, signature); }
            var ctrl = rebuild ? AnimatorController.CreateAnimatorControllerAtPath(ctrlPath) : existing;
            if (rebuild) BuildController(ctrl, allClips, skipped);
            EditorUtility.SetDirty(ctrl);
            AnimationClip Clip(string n) => allClips.FirstOrDefault(c => c.name == n);

            // 5. 점프 이동 곡선 (게임 코드가 오르기·내리기에 같이 쓴다)
            var info = new CatArtInfo
            {
                id = id,
                clips = allClips.Where(c => !skipped.Contains(c.name)).Select(c => c.name).ToArray(),
                loops = loops.ToArray(),
                jump = SampleRoot(Clip("JumpUp")),
                jumpDown = Clip("JumpDown") ? SampleRoot(Clip("JumpDown")) : null,
            };
            var drink = json.clips.FirstOrDefault(c => c.name == "Drink")?.drink;
            if (drink?.bowl != null) { info.bowlX = drink.bowl.x; info.bowlZ = drink.bowl.z; }
            var jj = json.clips.FirstOrDefault(c => c.name == "JumpUp")?.jump;
            if (jj != null) { info.jumpD = jj.D; info.jumpH = jj.H; info.deckBack = jj.deckBack; }
            var jdn = json.clips.FirstOrDefault(c => c.name == "JumpDown")?.jump;
            if (jdn != null) info.turnIn = jdn.turnIn;
            // 높이별 점프 (JumpUp, JumpUp40, JumpUp80, JumpDown, JumpDown40, JumpDown80): 곡선 + 높이·거리
            info.jumps = json.clips.Where(c => c.jump != null && (c.name.StartsWith("JumpUp") || c.name.StartsWith("JumpDown")) && !skipped.Contains(c.name) && Clip(c.name))
                .Select(c => new JumpSet { clip = c.name, up = c.name.StartsWith("JumpUp"), H = c.jump.H, D = c.jump.D, deckBack = c.jump.deckBack, turnIn = c.jump.turnIn, edge = c.jump.edge, curve = SampleRoot(Clip(c.name)) })
                .Where(j => j.curve != null).ToArray();
            if (json.itemSpots?.mouse_toy != null) { info.toyX = json.itemSpots.mouse_toy.x; info.toyZ = json.itemSpots.mouse_toy.z; info.toyYaw = json.itemSpots.mouse_toy.yaw; }
            if (json.itemSpots?.hideout != null) { info.hideZ = json.itemSpots.hideout.z; info.hideLift = json.itemSpots.hideout.catLift; }
            var lu = json.clips.FirstOrDefault(c => c.name == "LickUp")?.lickUp; if (lu?.tip != null && lu.tip.Length == 3) info.lickTip = new Vector3(lu.tip[0], lu.tip[1], lu.tip[2]);
            if (json.face != null) { info.faceEye = json.face.eye ?? ""; info.faceWhisker = json.face.whisker ?? ""; }
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

        static void BuildController(AnimatorController ctrl, List<AnimationClip> allClips, HashSet<string> skipped)
        {
            AnimationClip Clip(string n) => allClips.FirstOrDefault(c => c.name == n);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MoveRate", AnimatorControllerParameterType.Float);
            var sm = ctrl.layers[0].stateMachine;

            // 이동 블렌드에는 길이가 비슷한 걸음 동작만 넣는다. 서 있기(Idle, 6초)를 섞으면
            // Unity 가 재생 주기를 맞추느라 걸음이 느려졌다 빨라져 출발·정지 때 버벅인다.
            var move = new BlendTree { name = "Move", blendParameter = "Speed", blendType = BlendTreeType.Simple1D, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(move, ctrl);
            move.AddChild(Clip("Walk"), 0.4f);
            move.AddChild(Clip("Trot"), 1.06f);
            if (Clip("Gallop") && !skipped.Contains("Gallop")) move.AddChild(Clip("Gallop"), 2.27f);
            var moveState = sm.AddState("Move");
            moveState.motion = move;
            moveState.speedParameter = "MoveRate";   // 걷기보다 느릴 때 재생 속도를 줄여 발이 미끄러지지 않게
            moveState.speedParameterActive = true;
            foreach (var c in allClips)
            {
                if (skipped.Contains(c.name)) continue;
                var st = sm.AddState(c.name);
                st.motion = c;
                if (c.name == "Idle") sm.defaultState = st;
            }
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
            bool tch = ti != null && ti.textureType != TextureImporterType.NormalMap; if (tch) ti.textureType = TextureImporterType.NormalMap;
            if (ti != null && (MobileTex(ti, 512, true) | tch)) ti.SaveAndReimport();
            var cti = (TextureImporter)AssetImporter.GetAtPath($"{dir}/{id}_color.jpg");
            if (cti != null && MobileTex(cti, 1024, false)) cti.SaveAndReimport();
            foreach (var v in Directory.GetFiles(dir, id + "_color_v*.jpg")) { var vti = (TextureImporter)AssetImporter.GetAtPath(v.Replace('\\', '/')); if (vti != null && MobileTex(vti, 1024, false)) vti.SaveAndReimport(); }
            if (imp.meshCompression != ModelImporterMeshCompression.Low) { imp.meshCompression = ModelImporterMeshCompression.Low; }

            var m = SoftMat($"{Art}/Materials/item_{id}.mat");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{id}_color.jpg"));
            var nt = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (nt) { m.SetTexture("_BumpMap", nt); m.EnableKeyword("_NORMALMAP"); }
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_GroundAO", 0.12f);
            var meta = File.ReadAllText($"{dir}/{id}.json");
            var gm = System.Text.RegularExpressions.Regex.Match(meta, "\"gloss\":\\s*([0-9.]+)");
            m.SetFloat("_Gloss", gm.Success ? float.Parse(gm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0f);   // (물·우유 그릇: 유약이 살짝 반짝)
            EditorUtility.SetDirty(m);
            imp.importBlendShapes = true;   // (방석: 'Press' 눌림 모양, 움직임 연결은 C단계)
            foreach (var srcName in SourceMaterialNames(imp, fbx))
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), srcName), m);
            imp.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            PrefabUtility.SaveAsPrefabAsset(inst, $"{OutRes}/Items/{id}.prefab");
            UnityEngine.Object.DestroyImmediate(inst);
            File.Copy($"{dir}/{id}.json", $"{OutRes}/Items/{id}_info.json", true);
            AssetDatabase.ImportAsset($"{OutRes}/Items/{id}_info.json");
            Debug.Log($"[CatArtImport] item {id} OK");
        }
    }
}
