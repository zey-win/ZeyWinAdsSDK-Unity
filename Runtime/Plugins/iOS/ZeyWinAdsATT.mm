#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

#if __has_include(<AppTrackingTransparency/AppTrackingTransparency.h>)
#import <AppTrackingTransparency/AppTrackingTransparency.h>
#endif

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static void ZeyWinAds_PerformTrackingRequest(NSString *goName) {
    if (@available(iOS 14, *)) {
#if __has_include(<AppTrackingTransparency/AppTrackingTransparency.h>)
        [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
            if (goName) {
                NSString *statusString = [NSString stringWithFormat:@"%lu", (unsigned long)status];
                UnitySendMessage([goName UTF8String], "OnZeyWinAdsATTStatus", [statusString UTF8String]);
            }
        }];
#else
        if (goName) {
            UnitySendMessage([goName UTF8String], "OnZeyWinAdsATTStatus", "-1");
        }
#endif
    } else if (goName) {
        UnitySendMessage([goName UTF8String], "OnZeyWinAdsATTStatus", "authorized");
    }
}

static BOOL ZeyWinAds_TrackingRequestStarted = NO;

extern "C" {
    // Calling ATTrackingManager before the app is truly active (e.g. during
    // Unity's own startup/splash, before UIKit considers the window key) makes
    // iOS silently return .notDetermined without ever showing the prompt. Wait
    // for UIApplicationDidBecomeActiveNotification if we're not active yet.
    // Guarded to run at most once per process even if called more than once.
    void _ZeyWinAds_RequestTrackingAuthorization(const char* gameObjectName) {
        NSString *goName = gameObjectName ? [NSString stringWithUTF8String:gameObjectName] : nil;

        dispatch_async(dispatch_get_main_queue(), ^{
            if (ZeyWinAds_TrackingRequestStarted) {
                return;
            }
            ZeyWinAds_TrackingRequestStarted = YES;

            if ([UIApplication sharedApplication].applicationState == UIApplicationStateActive) {
                ZeyWinAds_PerformTrackingRequest(goName);
                return;
            }

            __block id observer = [[NSNotificationCenter defaultCenter]
                addObserverForName:UIApplicationDidBecomeActiveNotification
                            object:nil
                             queue:[NSOperationQueue mainQueue]
                        usingBlock:^(NSNotification *note) {
                            [[NSNotificationCenter defaultCenter] removeObserver:observer];
                            ZeyWinAds_PerformTrackingRequest(goName);
                        }];
        });
    }
}
