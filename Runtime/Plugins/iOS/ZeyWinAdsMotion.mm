#import <Foundation/Foundation.h>
#import <CoreMotion/CoreMotion.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static const NSInteger kZeyWinAdsMotionMaxFrames = 32;
static const NSTimeInterval kZeyWinAdsMotionMinFrameGapMs = 60.0;
static const NSTimeInterval kZeyWinAdsMotionWindowMs = 2000.0;
static const double kZeyWinAdsMotionGToMs2 = 9.80665;

// Kept alive for the duration of a collection window — ARC would otherwise
// deallocate it once _ZeyWinAds_CollectMotion returns, silently stopping updates.
static CMMotionManager *ZeyWinAdsMotionManager;
static NSString *ZeyWinAdsMotionGoName;
static NSMutableString *ZeyWinAdsMotionFrames;
static NSInteger ZeyWinAdsMotionKeptCount;
static NSInteger ZeyWinAdsMotionEventsCount;
static CFAbsoluteTime ZeyWinAdsMotionLastKeptTime;
static CFAbsoluteTime ZeyWinAdsMotionStartTime;
static BOOL ZeyWinAdsMotionHasGyro;
static BOOL ZeyWinAdsMotionFinished;

// m/s^2 as an integer millimeter/s^2, clamped to the range the server accepts —
// mirrors ZeyWinAdsMotionCollector.java's mm().
static int ZeyWinAds_MotionMm(double gValue) {
    long long scaled = llround(gValue * kZeyWinAdsMotionGToMs2 * 1000.0);
    if (scaled > 40000) return 40000;
    if (scaled < -40000) return -40000;
    return (int)scaled;
}

static void ZeyWinAds_SendMotionResult(int elapsedMs, int events, BOOL hasAccel, BOOL hasGyro, NSString *s) {
    NSDictionary *json = @{
        @"v": @1,
        @"elapsed_ms": @(elapsedMs),
        @"events": @(events),
        @"has_accel": @(hasAccel),
        @"has_gyro": @(hasGyro),
        @"s": s ?: @""
    };

    NSError *jsonError = nil;
    NSData *data = [NSJSONSerialization dataWithJSONObject:json options:0 error:&jsonError];
    if (!data || jsonError) {
        // Malformed result — drop it, same as a lost network callback.
        return;
    }

    NSString *jsonString = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    UnitySendMessage([ZeyWinAdsMotionGoName UTF8String], "OnMotionCollected", [jsonString UTF8String]);
}

static void ZeyWinAds_FinishMotion(void) {
    if (ZeyWinAdsMotionFinished) return;
    ZeyWinAdsMotionFinished = YES;

    [ZeyWinAdsMotionManager stopAccelerometerUpdates];
    int elapsedMs = (int)((CFAbsoluteTimeGetCurrent() - ZeyWinAdsMotionStartTime) * 1000.0);
    ZeyWinAds_SendMotionResult(elapsedMs, (int)ZeyWinAdsMotionEventsCount, YES, ZeyWinAdsMotionHasGyro, ZeyWinAdsMotionFrames);
}

extern "C" {
    // Samples the accelerometer for up to kZeyWinAdsMotionWindowMs (or
    // kZeyWinAdsMotionMaxFrames kept frames, whichever comes first), then sends
    // the result to Unity via UnitySendMessage. Never blocks the calling thread.
    // If there's no accelerometer, calls back immediately. Mirrors
    // ZeyWinAdsMotionCollector.java's collect().
    void _ZeyWinAds_CollectMotion(const char* gameObjectName) {
        ZeyWinAdsMotionGoName = gameObjectName ? [NSString stringWithUTF8String:gameObjectName] : @"";

        ZeyWinAdsMotionManager = [[CMMotionManager alloc] init];
        BOOL hasAccel = ZeyWinAdsMotionManager.isAccelerometerAvailable;
        BOOL hasGyro = ZeyWinAdsMotionManager.isGyroAvailable;
        ZeyWinAdsMotionHasGyro = hasGyro;

        if (!hasAccel) {
            ZeyWinAds_SendMotionResult(0, 0, NO, hasGyro, @"");
            return;
        }

        ZeyWinAdsMotionFrames = [NSMutableString string];
        ZeyWinAdsMotionKeptCount = 0;
        ZeyWinAdsMotionEventsCount = 0;
        ZeyWinAdsMotionLastKeptTime = 0;
        ZeyWinAdsMotionStartTime = CFAbsoluteTimeGetCurrent();
        ZeyWinAdsMotionFinished = NO;

        ZeyWinAdsMotionManager.accelerometerUpdateInterval = 0.02; // ~50Hz, comparable to SENSOR_DELAY_GAME

        NSOperationQueue *queue = [[NSOperationQueue alloc] init];
        queue.maxConcurrentOperationCount = 1;

        [ZeyWinAdsMotionManager startAccelerometerUpdatesToQueue:queue withHandler:^(CMAccelerometerData *data, NSError *error) {
            if (ZeyWinAdsMotionFinished || !data) return;

            ZeyWinAdsMotionEventsCount++; // counts every callback, not just kept frames

            CFAbsoluteTime now = CFAbsoluteTimeGetCurrent();
            if (ZeyWinAdsMotionKeptCount > 0 &&
                (now - ZeyWinAdsMotionLastKeptTime) * 1000.0 < kZeyWinAdsMotionMinFrameGapMs) {
                return;
            }
            ZeyWinAdsMotionLastKeptTime = now;

            if (ZeyWinAdsMotionKeptCount > 0) {
                [ZeyWinAdsMotionFrames appendString:@";"];
            }
            [ZeyWinAdsMotionFrames appendFormat:@"%d,%d,%d",
                ZeyWinAds_MotionMm(data.acceleration.x),
                ZeyWinAds_MotionMm(data.acceleration.y),
                ZeyWinAds_MotionMm(data.acceleration.z)];
            ZeyWinAdsMotionKeptCount++;

            if (ZeyWinAdsMotionKeptCount >= kZeyWinAdsMotionMaxFrames) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    ZeyWinAds_FinishMotion();
                });
            }
        }];

        // Window always closes, whichever trigger (frame count or timer) comes first.
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(kZeyWinAdsMotionWindowMs * NSEC_PER_MSEC)),
            dispatch_get_main_queue(), ^{
                ZeyWinAds_FinishMotion();
            });
    }
}
