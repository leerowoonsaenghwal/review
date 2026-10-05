#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace CatIsland.EditorTools
{
    /// <summary>
    /// Xcode 프로젝트에 기능을 켠다: iCloud 키값 저장(저장 옮기기), Game Center, 인앱 결제, 알림.
    /// 애플 개발자 계정에서도 같은 기능을 켜야 서명된다 (docs/RELEASE_TODO.md).
    /// </summary>
    public static class IosPostBuild
    {
        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string proj = PBXProject.GetPBXProjectPath(path);
            var pbx = new PBXProject(); pbx.ReadFromFile(proj);
            string main = pbx.GetUnityMainTargetGuid();
            var caps = new ProjectCapabilityManager(proj, "Unity-iPhone/nolgoseom.entitlements", null, main);
            caps.AddiCloud(true, false, false, false, null);
            caps.AddGameCenter();
            caps.AddInAppPurchase();
            caps.WriteToFile();
            // 앱 이름·암호화 신고 (표준 암호화만 씀: 수출 신고 질문을 건너뛴다)
            var plistPath = Path.Combine(path, "Info.plist"); var plist = new PlistDocument(); plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            plist.root.SetString("CFBundleDisplayName", "놀러와요 고양이섬");
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
