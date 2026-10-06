using System;
using System.Collections.Generic;
using System.IO;

namespace CatIsland.Game
{
    // 바깥 서비스는 모두 인터페이스 뒤에 둔다. 출시 전에는 가짜 모듈(테스트·개발), 출시 때는 실제 모듈을 끼운다
    // (광고 회사 앱 ID·결제 상품 등록은 사용자 할 일: docs/RELEASE_TODO.md). 우리 서버는 없다.

    public enum AdResult { Rewarded, Failed, Cancelled, NotReady }
    public enum PurchaseResult { Success, Failed, Cancelled, Pending }

    /// <summary>보상형·전면 광고. 결과는 콜백으로 (광고를 다 봐야 Rewarded).</summary>
    public interface IAds { bool RewardedReady { get; } void ShowRewarded(string spot, Action<AdResult> done); void ShowInterstitial(Action done); }
    /// <summary>결제 (StoreKit 2: 기기 안 검증). 지급 확인(Finish)은 게임이 지급을 저장한 뒤에.</summary>
    public interface IStore { void Purchase(string productId, Action<PurchaseResult, string> done); void Finish(string transactionId); IEnumerable<(string product, string tx)> Unfinished(); void Restore(); }
    public interface IGameCenter { void Report(string achievementId); void Score(string board, long value); }
    /// <summary>기기 안 알림 (서버 없음). 다시 예약할 때마다 전부 지우고 새로 넣는다.</summary>
    public interface INotifier { void ClearAll(); void Schedule(string id, DateTime utc, string title, string body); }
    /// <summary>iCloud 키값 저장 (최대 1 MB): 저장 파일을 그대로 둔다.</summary>
    public interface ICloudStore { string Read(); void Write(string json); }
    public interface IFileStore { string Read(string name); void Write(string name, string text); bool Exists(string name); void Delete(string name); }

    // ---------------------------------------------------------------- 가짜 모듈 (테스트·개발용)
    public class FakeAds : IAds
    {
        public AdResult next = AdResult.Rewarded; public int shown, interstitials; public bool ready = true;
        public bool RewardedReady => ready;
        public void ShowRewarded(string spot, Action<AdResult> done) { shown++; done(ready ? next : AdResult.NotReady); }
        public void ShowInterstitial(Action done) { interstitials++; done(); }
    }
    public class FakeStore : IStore
    {
        public PurchaseResult next = PurchaseResult.Success; public int seq; public bool crashBeforeFinish;
        public readonly List<(string product, string tx)> unfinished = new List<(string, string)>();
        public void Purchase(string productId, Action<PurchaseResult, string> done)
        {
            if (next != PurchaseResult.Success) { done(next, null); return; }
            var tx = "tx" + (++seq); unfinished.Add((productId, tx));
            if (crashBeforeFinish) return;                     // (앱이 지급 전에 꺼진 경우: 콜백 없음 → 다음 실행에 Unfinished 로 다시 온다)
            done(PurchaseResult.Success, tx);
        }
        public void Finish(string tx) => unfinished.RemoveAll(u => u.tx == tx);
        public IEnumerable<(string product, string tx)> Unfinished() => unfinished.ToArray();
        public void Restore() { }
    }
    public class FakeGameCenter : IGameCenter
    {
        public readonly List<string> reported = new List<string>(); public readonly Dictionary<string, long> scores = new Dictionary<string, long>();
        public void Report(string id) { if (!reported.Contains(id)) reported.Add(id); }
        public void Score(string board, long v) => scores[board] = v;
    }
    public class FakeNotifier : INotifier
    {
        public readonly List<(string id, DateTime utc, string title, string body)> scheduled = new List<(string, DateTime, string, string)>();
        public void ClearAll() => scheduled.Clear();
        public void Schedule(string id, DateTime utc, string title, string body) => scheduled.Add((id, utc, title, body));
    }
    public class MemoryCloud : ICloudStore { public string data; public void Write(string json) => data = json; public string Read() => data; }
    public class MemoryFiles : IFileStore
    {
        public readonly Dictionary<string, string> files = new Dictionary<string, string>();
        public string Read(string n) => files.TryGetValue(n, out var s) ? s : null;
        public void Write(string n, string t) => files[n] = t;
        public bool Exists(string n) => files.ContainsKey(n);
        public void Delete(string n) => files.Remove(n);
    }

    /// <summary>기기 저장소 (Application.persistentDataPath): 임시 파일에 쓴 뒤 바꿔치기 → 쓰는 도중 앱이 꺼져도 깨지지 않는다.</summary>
    public class DiskFiles : IFileStore
    {
        readonly string dir;
        public DiskFiles(string dir) { this.dir = dir; Directory.CreateDirectory(dir); }
        string P(string n) => Path.Combine(dir, n);
        public string Read(string n) => File.Exists(P(n)) ? File.ReadAllText(P(n)) : null;
        public bool Exists(string n) => File.Exists(P(n));
        public void Delete(string n) { if (File.Exists(P(n))) File.Delete(P(n)); }
        public void Write(string n, string text)
        {
            var tmp = P(n + ".tmp");
            File.WriteAllText(tmp, text);
            if (File.Exists(P(n))) File.Replace(tmp, P(n), null); else File.Move(tmp, P(n));
        }
    }
}
