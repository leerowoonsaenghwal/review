using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CatIsland.Game
{
    /// <summary>
    /// 아이폰 기기 서비스 (Plugins/iOS/CatServices.mm): 기기 안 알림, iCloud 키값 저장, Game Center, 인앱 결제(StoreKit).
    /// 서버도 API 키도 없다. 에디터·테스트에서는 가짜 모듈을 쓴다 (GameBootstrap).
    /// </summary>
    public class IosServices : MonoBehaviour, INotifier, ICloudStore, IGameCenter, IStore
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CatNotify_Request();
        [DllImport("__Internal")] static extern void CatNotify_ClearAll();
        [DllImport("__Internal")] static extern void CatNotify_Schedule(string id, double unix, string title, string body);
        [DllImport("__Internal")] static extern void CatCloud_Write(string json);
        [DllImport("__Internal")] static extern IntPtr CatCloud_Read();
        [DllImport("__Internal")] static extern void CatGC_Auth();
        [DllImport("__Internal")] static extern void CatGC_Achievement(string id);
        [DllImport("__Internal")] static extern void CatGC_Score(string board, long value);
        [DllImport("__Internal")] static extern void CatStore_Init(string ids);
        [DllImport("__Internal")] static extern void CatStore_Buy(string pid);
        [DllImport("__Internal")] static extern void CatStore_Finish(string tid);
        [DllImport("__Internal")] static extern void CatStore_Restore();
#else
        static void CatNotify_Request() { } static void CatNotify_ClearAll() { } static void CatNotify_Schedule(string id, double unix, string title, string body) { }
        static void CatCloud_Write(string json) { } static IntPtr CatCloud_Read() => IntPtr.Zero;
        static void CatGC_Auth() { } static void CatGC_Achievement(string id) { } static void CatGC_Score(string board, long value) { }
        static void CatStore_Init(string ids) { } static void CatStore_Buy(string pid) { } static void CatStore_Finish(string tid) { } static void CatStore_Restore() { }
#endif
        public static IosServices Create()
        {
            var s = new GameObject("CatServices").AddComponent<IosServices>(); DontDestroyOnLoad(s.gameObject);
            CatGC_Auth(); CatStore_Init(string.Join("\t", Catalog.Products.Select(p => p.id)));
            return s;
        }

        // ---- 알림 (권한은 처음 알림을 예약할 때 한 번 묻는다)
        bool asked;
        public void ClearAll() => CatNotify_ClearAll();
        public void Schedule(string id, DateTime utc, string title, string body)
        {
            if (!asked) { asked = true; CatNotify_Request(); }
            CatNotify_Schedule(id, TimeUtil.ToUnix(utc), title, body);
        }
        void OnNotifyPermission(string granted) { }

        // ---- iCloud
        public string Read() { var p = CatCloud_Read(); if (p == IntPtr.Zero) return null; var s = Marshal.PtrToStringUTF8(p); Marshal.FreeHGlobal(p); return s; }
        public void Write(string json) { if (json != null && json.Length < 900_000) CatCloud_Write(json); }   // (키값 저장 한도 1 MB)

        // ---- Game Center
        public void Report(string id) => CatGC_Achievement("com.nolgoseom.ach." + id);
        public void Score(string board, long v) => CatGC_Score("com.nolgoseom.board." + board, v);
        void OnGameCenter(string ok) { }

        // ---- 결제
        readonly Dictionary<string, Action<PurchaseResult, string>> waiting = new Dictionary<string, Action<PurchaseResult, string>>();
        readonly List<(string product, string tx)> unfinished = new List<(string, string)>();
        /// <summary>요청하지 않은 결제 완료(지난번 끝나지 않은 결제, 구매 복원)가 왔을 때: 게임이 RestorePending 으로 지급한다.</summary>
        public Action OnUnsolicited;
        public readonly Dictionary<string, string> LocalPrices = new Dictionary<string, string>();   // (앱스토어 지역 가격 표시)

        public void Purchase(string productId, Action<PurchaseResult, string> done) { waiting[productId] = done; CatStore_Buy(productId); }
        public void Finish(string tx) { unfinished.RemoveAll(u => u.tx == tx); CatStore_Finish(tx); }
        public IEnumerable<(string product, string tx)> Unfinished() => unfinished.ToArray();
        public void RestorePurchases() => CatStore_Restore();
        void OnProducts(string list)
        {
            foreach (var line in list.Split('\n')) { var p = line.Split('\t'); if (p.Length == 2) LocalPrices[p[0]] = p[1]; }
        }
        void OnPurchase(string msg)
        {
            var p = msg.Split('\t'); if (p.Length < 2) return;
            string status = p[0], pid = p[1], tx = p.Length > 2 ? p[2] : "";
            var r = status == "ok" ? PurchaseResult.Success : status == "cancel" ? PurchaseResult.Cancelled : status == "pending" ? PurchaseResult.Pending : PurchaseResult.Failed;
            if (r == PurchaseResult.Success && !unfinished.Any(u => u.tx == tx)) unfinished.Add((pid, tx));
            if (waiting.TryGetValue(pid, out var cb)) { waiting.Remove(pid); cb(r, r == PurchaseResult.Success ? tx : null); }
            else if (r == PurchaseResult.Success) OnUnsolicited?.Invoke();
        }
    }

    /// <summary>광고 회사 모듈을 넣기 전의 출시 빌드: 광고가 준비되지 않은 것으로 (광고 버튼이 보이지 않는다, 보상이 거저 나가지 않는다).</summary>
    public class NoAds : IAds
    {
        public bool RewardedReady => false;
        public void ShowRewarded(string spot, Action<AdResult> done) => done(AdResult.NotReady);
        public void ShowInterstitial(Action done) => done();
    }
}
