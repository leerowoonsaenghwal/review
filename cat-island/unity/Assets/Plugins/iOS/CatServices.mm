// 놀러와요 고양이섬: 기기 서비스 (서버 없음). 알림 · iCloud 키값 저장 · Game Center · 인앱 결제(StoreKit).
// 결과는 UnitySendMessage 로 "CatServices" 오브젝트에 돌려준다.
#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <UserNotifications/UserNotifications.h>
#import <GameKit/GameKit.h>
#import <StoreKit/StoreKit.h>
#import <AVFoundation/AVFoundation.h>

extern UIViewController *UnityGetGLViewController(void);
extern void UnitySendMessage(const char *obj, const char *method, const char *msg);
static void Send(NSString *method, NSString *msg) { UnitySendMessage("CatServices", method.UTF8String, (msg ?: @"").UTF8String); }
static NSString *S(const char *c) { return c ? [NSString stringWithUTF8String:c] : @""; }

// ---------------------------------------------------------------- 알림 (기기 안 예약)
extern "C" void CatNotify_Request() {
    [[UNUserNotificationCenter currentNotificationCenter] requestAuthorizationWithOptions:(UNAuthorizationOptionAlert | UNAuthorizationOptionSound | UNAuthorizationOptionBadge)
        completionHandler:^(BOOL granted, NSError *e) { dispatch_async(dispatch_get_main_queue(), ^{ Send(@"OnNotifyPermission", granted ? @"1" : @"0"); }); }];
}
extern "C" void CatNotify_ClearAll() {
    [[UNUserNotificationCenter currentNotificationCenter] removeAllPendingNotificationRequests];
}
extern "C" void CatNotify_Schedule(const char *ident, double unixTime, const char *title, const char *body) {
    double delay = unixTime - [[NSDate date] timeIntervalSince1970]; if (delay < 1) return;
    UNMutableNotificationContent *c = [UNMutableNotificationContent new]; c.title = S(title); c.body = S(body); c.sound = [UNNotificationSound defaultSound];
    UNTimeIntervalNotificationTrigger *t = [UNTimeIntervalNotificationTrigger triggerWithTimeInterval:delay repeats:NO];
    [[UNUserNotificationCenter currentNotificationCenter] addNotificationRequest:[UNNotificationRequest requestWithIdentifier:S(ident) content:c trigger:t] withCompletionHandler:nil];
}

// ---------------------------------------------------------------- iCloud 키값 저장 (최대 1 MB)
extern "C" void CatCloud_Write(const char *json) {
    NSUbiquitousKeyValueStore *kv = [NSUbiquitousKeyValueStore defaultStore]; [kv setString:S(json) forKey:@"save"]; [kv synchronize];
}
extern "C" const char *CatCloud_Read() {
    NSUbiquitousKeyValueStore *kv = [NSUbiquitousKeyValueStore defaultStore]; [kv synchronize];
    NSString *s = [kv stringForKey:@"save"]; if (!s) return NULL;
    const char *u = s.UTF8String; char *out = (char *)malloc(strlen(u) + 1); strcpy(out, u); return out;   // (Unity 가 해제)
}

// ---------------------------------------------------------------- Game Center
extern "C" void CatGC_Auth() {
    GKLocalPlayer.localPlayer.authenticateHandler = ^(UIViewController *vc, NSError *e) {
        if (vc) [UnityGetGLViewController() presentViewController:vc animated:YES completion:nil];
        Send(@"OnGameCenter", GKLocalPlayer.localPlayer.isAuthenticated ? @"1" : @"0");
    };
}
extern "C" void CatGC_Achievement(const char *ident) {
    if (!GKLocalPlayer.localPlayer.isAuthenticated) return;
    GKAchievement *a = [[GKAchievement alloc] initWithIdentifier:S(ident)]; a.percentComplete = 100; a.showsCompletionBanner = YES;
    [GKAchievement reportAchievements:@[a] withCompletionHandler:nil];
}
extern "C" void CatGC_Score(const char *board, long long value) {
    if (!GKLocalPlayer.localPlayer.isAuthenticated) return;
    [GKLeaderboard submitScore:(NSInteger)value context:0 player:GKLocalPlayer.localPlayer leaderboardIDs:@[S(board)] completionHandler:^(NSError *e) {}];
}

// ---------------------------------------------------------------- 인앱 결제 (StoreKit, 기기 안 처리)
@interface CatStore : NSObject <SKPaymentTransactionObserver, SKProductsRequestDelegate>
@property (nonatomic, strong) NSMutableDictionary<NSString *, SKProduct *> *products;
@property (nonatomic, strong) NSMutableDictionary<NSString *, SKPaymentTransaction *> *open;
@property (nonatomic, strong) SKProductsRequest *req;
@end
@implementation CatStore
- (instancetype)init { if ((self = [super init])) { _products = [NSMutableDictionary new]; _open = [NSMutableDictionary new]; [[SKPaymentQueue defaultQueue] addTransactionObserver:self]; } return self; }
- (void)productsRequest:(SKProductsRequest *)r didReceiveResponse:(SKProductsResponse *)resp {
    NSMutableArray *list = [NSMutableArray new];
    for (SKProduct *p in resp.products) { self.products[p.productIdentifier] = p;
        NSNumberFormatter *f = [NSNumberFormatter new]; f.numberStyle = NSNumberFormatterCurrencyStyle; f.locale = p.priceLocale;
        [list addObject:[NSString stringWithFormat:@"%@\t%@", p.productIdentifier, [f stringFromNumber:p.price]]]; }
    Send(@"OnProducts", [list componentsJoinedByString:@"\n"]);
}
- (void)paymentQueue:(SKPaymentQueue *)q updatedTransactions:(NSArray<SKPaymentTransaction *> *)txs {
    for (SKPaymentTransaction *t in txs) {
        NSString *pid = t.payment.productIdentifier, *tid = t.transactionIdentifier ?: [[NSUUID UUID] UUIDString];
        switch (t.transactionState) {
            case SKPaymentTransactionStatePurchased: case SKPaymentTransactionStateRestored:
                self.open[tid] = t; Send(@"OnPurchase", [NSString stringWithFormat:@"ok\t%@\t%@", pid, tid]); break;   // (게임이 지급·저장 뒤 Finish)
            case SKPaymentTransactionStateFailed:
                Send(@"OnPurchase", [NSString stringWithFormat:@"%@\t%@\t", t.error.code == SKErrorPaymentCancelled ? @"cancel" : @"fail", pid]);
                [q finishTransaction:t]; break;
            case SKPaymentTransactionStateDeferred: Send(@"OnPurchase", [NSString stringWithFormat:@"pending\t%@\t", pid]); break;
            default: break;
        }
    }
}
@end
static CatStore *catStore;
extern "C" void CatStore_Init(const char *idsTabSeparated) {
    if (!catStore) catStore = [CatStore new];
    NSSet *ids = [NSSet setWithArray:[S(idsTabSeparated) componentsSeparatedByString:@"\t"]];
    catStore.req = [[SKProductsRequest alloc] initWithProductIdentifiers:ids]; catStore.req.delegate = catStore; [catStore.req start];
}
extern "C" void CatStore_Buy(const char *pid) {
    SKProduct *p = catStore.products[S(pid)];
    if (!p || ![SKPaymentQueue canMakePayments]) { Send(@"OnPurchase", [NSString stringWithFormat:@"fail\t%@\t", S(pid)]); return; }
    [[SKPaymentQueue defaultQueue] addPayment:[SKPayment paymentWithProduct:p]];
}
extern "C" void CatStore_Finish(const char *tid) {
    SKPaymentTransaction *t = catStore.open[S(tid)]; if (t) { [[SKPaymentQueue defaultQueue] finishTransaction:t]; [catStore.open removeObjectForKey:S(tid)]; }
}
extern "C" void CatStore_Restore() { [[SKPaymentQueue defaultQueue] restoreCompletedTransactions]; }

// ---------------------------------------------------------------- 공유 (아이폰 공유 화면: 이용자가 고를 때만)
extern "C" void CatShare_Image(const char *path, const char *text) {
    UIImage *img = [UIImage imageWithContentsOfFile:S(path)]; if (!img) return;
    NSMutableArray *items = [NSMutableArray arrayWithObject:img]; if (text && strlen(text)) [items addObject:S(text)];
    UIActivityViewController *vc = [[UIActivityViewController alloc] initWithActivityItems:items applicationActivities:nil];
    UIViewController *root = UnityGetGLViewController();
    vc.popoverPresentationController.sourceView = root.view;
    vc.popoverPresentationController.sourceRect = CGRectMake(root.view.bounds.size.width / 2, root.view.bounds.size.height, 1, 1);
    [root presentViewController:vc animated:YES completion:nil];
}

// ---------------------------------------------------------------- 다른 앱 소리 (음악 앱 등): 나오는 동안 게임 배경음악은 쉰다 (효과음은 그대로)
extern "C" bool CatAudio_OtherPlaying() { return [AVAudioSession sharedInstance].secondaryAudioShouldBeSilencedHint; }
