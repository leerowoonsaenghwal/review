using System;
using UnityEngine;

namespace CatIsland.UI
{
    /// <summary>
    /// 사진 고르기 → 고양이 털 읽기. 사진은 기기 안에서만 읽고 어디로도 보내지 않는다 (기획서 1부 4장).
    /// iOS: 사진 고르기(PHPicker) 네이티브 플러그인이 기기 안 임시 파일 경로를 돌려주면 PhotoAnalysis 로 읽는다.
    /// </summary>
    public static class PhotoReader
    {
        /// <summary>테스트·에디터: 고를 사진을 직접 넣는다.</summary>
        public static Func<Texture2D> EditorPick;

        public static void Pick(Action<PhotoCat> done)
        {
#if UNITY_IOS && !UNITY_EDITOR
            NativePhoto.Pick(path => { if (string.IsNullOrEmpty(path)) { done(null); return; } var t = new Texture2D(2, 2); t.LoadImage(System.IO.File.ReadAllBytes(path)); try { System.IO.File.Delete(path); } catch { } done(PhotoAnalysis.Read(t)); UnityEngine.Object.Destroy(t); });
#else
            var tex = EditorPick?.Invoke(); done(tex ? PhotoAnalysis.Read(tex) : null);
#endif
        }
    }
}
