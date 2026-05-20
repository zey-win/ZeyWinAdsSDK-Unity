#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

#if __has_include(<AppTrackingTransparency/AppTrackingTransparency.h>)
#import <AppTrackingTransparency/AppTrackingTransparency.h>
#endif

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

extern "C" {
    void _ZeyWinAds_RequestTrackingAuthorization(const char* gameObjectName) {
        NSString *goName = gameObjectName ? [NSString stringWithUTF8String:gameObjectName] : nil;

        dispatch_async(dispatch_get_main_queue(), ^{
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
        });
    }
}
