#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

#if __has_include(<AdSupport/AdSupport.h>)
#import <AdSupport/AdSupport.h>
#endif

#if __has_include(<AppTrackingTransparency/AppTrackingTransparency.h>)
#import <AppTrackingTransparency/AppTrackingTransparency.h>
#endif

static const char* ZeyWinAds_CStringCopy(NSString *value) {
    const char *utf8 = [value UTF8String];
    return utf8 ? strdup(utf8) : strdup("");
}

extern "C" {
    const char* _ZeyWinAds_GetIDFA(void) {
#if __has_include(<AdSupport/AdSupport.h>) && __has_include(<AppTrackingTransparency/AppTrackingTransparency.h>)
        if (@available(iOS 14, *)) {
            if ([ATTrackingManager trackingAuthorizationStatus] != ATTrackingManagerAuthorizationStatusAuthorized) {
                return strdup("");
            }
        }

        NSUUID *idfa = [[ASIdentifierManager sharedManager] advertisingIdentifier];
        NSString *idfaString = [idfa UUIDString];
        if (!idfaString || [idfaString isEqualToString:@"00000000-0000-0000-0000-000000000000"]) {
            return strdup("");
        }
        return ZeyWinAds_CStringCopy(idfaString);
#else
        return strdup("");
#endif
    }

    const char* _ZeyWinAds_GetIDFV(void) {
        NSUUID *idfv = [[UIDevice currentDevice] identifierForVendor];
        NSString *idfvString = [idfv UUIDString];
        return ZeyWinAds_CStringCopy(idfvString ?: @"");
    }
}
