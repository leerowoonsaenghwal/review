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
            // 앱 개인정보 매니페스트 (추적·수집 없음, 설정 저장): 앱 본체에 넣는다 (UnityFramework 것은 엔진 몫만 담는다)
            pbx = new PBXProject(); pbx.ReadFromFile(proj);
            File.Copy(Path.Combine(UnityEngine.Application.dataPath, "CatIsland/Editor/iOS/PrivacyInfo.xcprivacy"), Path.Combine(path, "PrivacyInfo.xcprivacy"), true);
            if (pbx.FindFileGuidByProjectPath("PrivacyInfo.xcprivacy") == null)
                pbx.AddFileToBuild(main, pbx.AddFile("PrivacyInfo.xcprivacy", "PrivacyInfo.xcprivacy", PBXSourceTree.Source));
            pbx.WriteToFile(proj);
            // 앱 이름·암호화 신고 (표준 암호화만 씀: 수출 신고 질문을 건너뛴다)
            var plistPath = Path.Combine(path, "Info.plist"); var plist = new PlistDocument(); plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            // 공유 화면의 '이미지 저장'(이용자가 고를 때만): 이 문구가 없으면 누르는 순간 앱이 꺼진다
            plist.root.SetString("NSPhotoLibraryAddUsageDescription", "찍은 고양이 사진을 사진 앱에 저장할 때만 써요.");
            plist.root.SetString("CFBundleDisplayName", "놀고섬");   // (홈 화면 아이콘 아래 이름: 6~7글자만 보이므로 줄임말, BRAND.md. 정식 이름은 앱스토어에)
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
