#import <UIKit/UIKit.h>

extern "C" {
    // Returns 1 if the device is currently in system dark mode, 0 otherwise
    // (including on iOS < 12, where userInterfaceStyle doesn't exist — matches
    // the "couldn't determine, default to light" fallback used on other platforms).
    // Uses an int (not BOOL) return for unambiguous marshaling to C# bool.
    int _ZeyWinAds_IsDarkMode(void) {
        if (@available(iOS 12.0, *)) {
            UIWindow *keyWindow = nil;
            for (UIScene *scene in [UIApplication sharedApplication].connectedScenes) {
                if (scene.activationState == UISceneActivationStateForegroundActive &&
                    [scene isKindOfClass:[UIWindowScene class]]) {
                    keyWindow = ((UIWindowScene *)scene).windows.firstObject;
                    break;
                }
            }
            if (!keyWindow) {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
                keyWindow = [UIApplication sharedApplication].keyWindow;
#pragma clang diagnostic pop
            }

            UITraitCollection *traits = keyWindow ? keyWindow.traitCollection : [UITraitCollection currentTraitCollection];
            return traits.userInterfaceStyle == UIUserInterfaceStyleDark ? 1 : 0;
        }
        return 0;
    }
}
