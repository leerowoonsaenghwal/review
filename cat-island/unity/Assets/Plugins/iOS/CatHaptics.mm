// 고양이 섬 진동: 쓰다듬을 때 골골송에 맞춘 Core Haptics 연속 진동 + 짧은 톡 진동.
#import <UIKit/UIKit.h>
#import <CoreHaptics/CoreHaptics.h>

static CHHapticEngine *sEngine = nil;
static id<CHHapticAdvancedPatternPlayer> sPurrPlayer = nil;
static BOOL sPurrRunning = NO;
static BOOL sSupported = NO;
static int sPurrLevel = 0;

static void CatHapticsEnsureEngine(void)
{
    if (sEngine != nil) return;
    sSupported = CHHapticEngine.capabilitiesForHardware.supportsHaptics;
    if (!sSupported) return;

    NSError *error = nil;
    sEngine = [[CHHapticEngine alloc] initAndReturnError:&error];
    if (error != nil || sEngine == nil) { sEngine = nil; sSupported = NO; return; }
    sEngine.playsHapticsOnly = YES;
    sEngine.autoShutdownEnabled = YES;
    __weak CHHapticEngine *weakEngine = sEngine;
    sEngine.resetHandler = ^{
        sPurrPlayer = nil;
        sPurrRunning = NO;
        sPurrLevel = 0;
        [weakEngine startAndReturnError:nil];
    };
    sEngine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
        sPurrPlayer = nil;
        sPurrRunning = NO;
        sPurrLevel = 0;
    };
    [sEngine startAndReturnError:nil];
}

static id<CHHapticAdvancedPatternPlayer> CatHapticsMakePurrPlayer(float baseIntensity)
{
    // 들숨(약하게) 1.1초 + 날숨(조금 세게) 1.3초. 낮은 선명도 = 부드러운 웅웅거림
    NSError *error = nil;
    CHHapticEventParameter *sharp = [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness value:0.08f];
    CHHapticEventParameter *inten = [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity value:baseIntensity];
    CHHapticEvent *ev = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous
                                                      parameters:@[sharp, inten]
                                                    relativeTime:0
                                                        duration:2.4];
    NSArray<CHHapticParameterCurveControlPoint *> *pts = @[
        [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:0.0 value:0.15f],
        [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:0.25 value:0.45f],
        [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:0.9 value:0.4f],
        [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:1.1 value:0.12f],
        [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:1.4 value:0.6f],
        [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:2.1 value:0.5f],
        [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:2.4 value:0.15f],
    ];
    CHHapticParameterCurve *curve = [[CHHapticParameterCurve alloc] initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl
                                                                          controlPoints:pts
                                                                           relativeTime:0];
    CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:@[ev] parameterCurves:@[curve] error:&error];
    if (error != nil || pattern == nil) return nil;
    id<CHHapticAdvancedPatternPlayer> player = [sEngine createAdvancedPlayerWithPattern:pattern error:&error];
    if (error != nil) return nil;
    player.loopEnabled = YES;
    player.loopEnd = 2.4;
    return player;
}

extern "C" {

void CatHaptics_Prepare(void)
{
    CatHapticsEnsureEngine();
}

void CatHaptics_Impact(int style, float intensity)
{
    UIImpactFeedbackStyle s = UIImpactFeedbackStyleSoft;
    if (style == 1) s = UIImpactFeedbackStyleLight;
    else if (style == 2) s = UIImpactFeedbackStyleMedium;
    UIImpactFeedbackGenerator *gen = [[UIImpactFeedbackGenerator alloc] initWithStyle:s];
    [gen impactOccurredWithIntensity:intensity];
}

void CatHaptics_SetPurr(float intensity)
{
    CatHapticsEnsureEngine();
    if (!sSupported || sEngine == nil) return;
    NSError *error = nil;

    // 세기를 4단계로 나눠 단계가 바뀔 때만 패턴을 다시 만든다
    int level = intensity <= 0.001f ? 0 : (int)ceilf(fminf(intensity, 1.0f) * 4.0f);
    if (level == sPurrLevel && (level == 0 || sPurrRunning)) return;

    if (sPurrPlayer != nil && sPurrRunning) [sPurrPlayer stopAtTime:CHHapticTimeImmediate error:&error];
    sPurrPlayer = nil;
    sPurrRunning = NO;
    sPurrLevel = level;
    if (level == 0) return;

    // 손끝에 살짝 느껴질 정도로 약하게: 0.3 ~ 0.6
    float base = 0.3f + 0.075f * (float)level;
    sPurrPlayer = CatHapticsMakePurrPlayer(base);
    if (sPurrPlayer == nil) return;
    [sEngine startAndReturnError:nil];
    error = nil;
    [sPurrPlayer startAtTime:CHHapticTimeImmediate error:&error];
    sPurrRunning = (error == nil);
}

}
