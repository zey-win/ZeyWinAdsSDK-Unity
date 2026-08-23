#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <WebKit/WebKit.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

// Scheme-based routing for popup navigations, mirroring
// ZeyWinAdsWebViewNavigation.java exactly (isWebUrl/shouldOpenExternally).
static BOOL ZeyWinAdsIsWebUrl(NSString *url) {
    if (!url) return NO;
    NSString *lower = [url lowercaseString];
    return [lower hasPrefix:@"http://"] || [lower hasPrefix:@"https://"] ||
           [lower hasPrefix:@"about:"] || [lower hasPrefix:@"data:"] ||
           [lower hasPrefix:@"javascript:"];
}

static BOOL ZeyWinAdsShouldOpenExternally(NSString *url) {
    if (!url || url.length == 0 || ZeyWinAdsIsWebUrl(url)) return NO;
    NSString *lower = [url lowercaseString];
    static NSArray<NSString *> *externalPrefixes = nil;
    if (!externalPrefixes) {
        externalPrefixes = @[@"intent://", @"market://", @"tg://", @"telegram://",
                              @"whatsapp://", @"viber://", @"mailto:", @"tel:", @"sms:"];
    }
    for (NSString *prefix in externalPrefixes) {
        if ([lower hasPrefix:prefix]) return YES;
    }
    return NO;
}

static void ZeyWinAdsOpenExternal(NSString *url) {
    NSURL *nsUrl = [NSURL URLWithString:url];
    if (!nsUrl) return;
    dispatch_async(dispatch_get_main_queue(), ^{
        [[UIApplication sharedApplication] openURL:nsUrl options:@{} completionHandler:nil];
    });
}

// Owns a single popup WKWebView created in response to window.open()/target="_blank".
// Never attached to any view hierarchy — used purely to sniff the popup's target
// navigation and either promote it into the parent WebView or hand it off
// externally, mirroring Android's hidden "child" WebView in
// ZeyWinAdsWebChromeClient.configurePopupWebView.
@interface ZeyWinAdsPopupWebView : NSObject <WKNavigationDelegate>
@property (nonatomic, strong) WKWebView *webView;
@property (nonatomic, weak) WKWebView *parentWebView;
@property (nonatomic, copy) void (^onClose)(ZeyWinAdsPopupWebView *popup);
@end

@implementation ZeyWinAdsPopupWebView

- (void)webView:(WKWebView *)webView decidePolicyForNavigationAction:(WKNavigationAction *)navigationAction decisionHandler:(void (^)(WKNavigationActionPolicy))decisionHandler {
    NSString *url = navigationAction.request.URL.absoluteString;

    if (ZeyWinAdsShouldOpenExternally(url)) {
        ZeyWinAdsOpenExternal(url);
        decisionHandler(WKNavigationActionPolicyCancel);
        [self close];
        return;
    }

    if (ZeyWinAdsIsWebUrl(url) && self.parentWebView) {
        [self.parentWebView loadRequest:navigationAction.request];
        decisionHandler(WKNavigationActionPolicyCancel);
        [self close];
        return;
    }

    decisionHandler(WKNavigationActionPolicyAllow);
}

// Fallback path mirroring Android's onPageFinished promotion, in case
// decidePolicyForNavigationAction didn't already intercept.
- (void)webView:(WKWebView *)webView didFinishNavigation:(WKNavigation *)navigation {
    NSString *url = webView.URL.absoluteString;
    if (ZeyWinAdsIsWebUrl(url) && self.parentWebView) {
        [self.parentWebView loadRequest:[NSURLRequest requestWithURL:webView.URL]];
        [self close];
    }
}

- (void)close {
    [self.webView stopLoading];
    if (self.onClose) {
        self.onClose(self);
    }
}

@end

@interface ZeyWinAdsWebViewController : UIViewController <WKNavigationDelegate, WKUIDelegate>
@property (nonatomic, strong) WKWebView *webView;
@property (nonatomic, strong) UIView *loadingOverlay;
@property (nonatomic, strong) NSString *initialUrl;
@property (nonatomic, copy) NSString *gameObjectName;
@property (nonatomic, strong) NSMutableSet<ZeyWinAdsPopupWebView *> *activePopups;
@end

@implementation ZeyWinAdsWebViewController

- (void)viewDidLoad {
    [super viewDidLoad];

    self.activePopups = [NSMutableSet set];

    // Configure WKWebView
    WKWebViewConfiguration *config = [[WKWebViewConfiguration alloc] init];
    config.allowsInlineMediaPlayback = YES;
    config.mediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypeNone;
    config.preferences.javaScriptCanOpenWindowsAutomatically = YES;

    self.webView = [[WKWebView alloc] initWithFrame:self.view.bounds configuration:config];
    self.webView.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
    self.webView.navigationDelegate = self;
    self.webView.UIDelegate = self;
    self.webView.allowsBackForwardNavigationGestures = YES;

    [self.view addSubview:self.webView];
    [self addLoadingOverlay];

    if (self.initialUrl) {
        NSURL *url = [NSURL URLWithString:self.initialUrl];
        NSURLRequest *request = [NSURLRequest requestWithURL:url];
        [self.webView loadRequest:request];
    }
}

- (BOOL)prefersStatusBarHidden {
    return YES;
}

- (UIStatusBarAnimation)preferredStatusBarUpdateAnimation {
    return UIStatusBarAnimationFade;
}

- (void)webView:(WKWebView *)webView didFinishNavigation:(WKNavigation *)navigation {
    [self hideLoadingOverlay];
    if (self.gameObjectName) {
        UnitySendMessage([self.gameObjectName UTF8String], "OnWebViewPageLoaded", "");
    }
}

- (void)webView:(WKWebView *)webView didFailNavigation:(WKNavigation *)navigation withError:(NSError *)error {
    [self hideLoadingOverlay];
    if (self.gameObjectName) {
        UnitySendMessage([self.gameObjectName UTF8String], "OnWebViewLoadError", [[error localizedDescription] UTF8String]);
    }
}

- (void)webView:(WKWebView *)webView didFailProvisionalNavigation:(WKNavigation *)navigation withError:(NSError *)error {
    [self hideLoadingOverlay];
    if (self.gameObjectName) {
        UnitySendMessage([self.gameObjectName UTF8String], "OnWebViewLoadError", [[error localizedDescription] UTF8String]);
    }
}

#pragma mark - WKUIDelegate (popup handling)

- (WKWebView *)webView:(WKWebView *)webView createWebViewWithConfiguration:(WKWebViewConfiguration *)configuration forNavigationAction:(WKNavigationAction *)navigationAction windowFeatures:(WKWindowFeatures *)windowFeatures {
    // Must be created with the given configuration (shares the requesting
    // page's process/state) and never attached to a view hierarchy — matches
    // Android's bare, unattached "child" WebView.
    WKWebView *popupWebView = [[WKWebView alloc] initWithFrame:CGRectZero configuration:configuration];

    ZeyWinAdsPopupWebView *popup = [[ZeyWinAdsPopupWebView alloc] init];
    popup.webView = popupWebView;
    popup.parentWebView = webView;
    popupWebView.navigationDelegate = popup;

    __weak ZeyWinAdsWebViewController *weakSelf = self;
    popup.onClose = ^(ZeyWinAdsPopupWebView *closedPopup) {
        [weakSelf.activePopups removeObject:closedPopup];
    };

    // Retain the popup so it isn't deallocated mid-navigation.
    [self.activePopups addObject:popup];

    return popupWebView;
}

- (void)webViewDidClose:(WKWebView *)webView {
    for (ZeyWinAdsPopupWebView *popup in [self.activePopups copy]) {
        if (popup.webView == webView) {
            [popup close];
            break;
        }
    }
}

- (void)addLoadingOverlay {
    self.loadingOverlay = [[UIView alloc] initWithFrame:self.view.bounds];
    self.loadingOverlay.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
    self.loadingOverlay.backgroundColor = [[UIColor blackColor] colorWithAlphaComponent:0.80];
    self.loadingOverlay.userInteractionEnabled = YES;

    UIStackView *stack = [[UIStackView alloc] init];
    stack.axis = UILayoutConstraintAxisVertical;
    stack.alignment = UIStackViewAlignmentCenter;
    stack.spacing = 14.0;
    stack.translatesAutoresizingMaskIntoConstraints = NO;

    UIActivityIndicatorView *spinner = [[UIActivityIndicatorView alloc] initWithActivityIndicatorStyle:UIActivityIndicatorViewStyleWhiteLarge];
    [spinner startAnimating];

    UILabel *label = [[UILabel alloc] init];
    label.text = @"Loading";
    label.textColor = [UIColor whiteColor];
    label.font = [UIFont systemFontOfSize:20.0 weight:UIFontWeightSemibold];

    [stack addArrangedSubview:spinner];
    [stack addArrangedSubview:label];
    [self.loadingOverlay addSubview:stack];
    [self.view addSubview:self.loadingOverlay];

    [NSLayoutConstraint activateConstraints:@[
        [stack.centerXAnchor constraintEqualToAnchor:self.loadingOverlay.centerXAnchor],
        [stack.centerYAnchor constraintEqualToAnchor:self.loadingOverlay.centerYAnchor]
    ]];
}

- (void)hideLoadingOverlay {
    if (!self.loadingOverlay) return;
    [self.loadingOverlay removeFromSuperview];
    self.loadingOverlay = nil;
}

@end

static ZeyWinAdsWebViewController *_webViewController = nil;

extern "C" {
    void* _ZeyWinAds_CreateWebView(const char* url, const char* gameObjectName) {
        if (_webViewController != nil) {
            return (__bridge void*)_webViewController;
        }

        NSString *urlString = [NSString stringWithUTF8String:url];
        NSString *goName = [NSString stringWithUTF8String:gameObjectName];

        _webViewController = [[ZeyWinAdsWebViewController alloc] init];
        _webViewController.initialUrl = urlString;
        _webViewController.gameObjectName = goName;
        _webViewController.modalPresentationStyle = UIModalPresentationFullScreen;

        return (__bridge void*)_webViewController;
    }

    void _ZeyWinAds_ShowWebView(void* webViewPtr) {
        if (webViewPtr == NULL || _webViewController == nil) {
            return;
        }

        dispatch_async(dispatch_get_main_queue(), ^{
            UIWindowScene *windowScene = nil;
            for (UIScene *scene in [UIApplication sharedApplication].connectedScenes) {
                if (scene.activationState == UISceneActivationStateForegroundActive &&
                    [scene isKindOfClass:[UIWindowScene class]]) {
                    windowScene = (UIWindowScene *)scene;
                    break;
                }
            }
            UIWindow *keyWindow = windowScene.windows.firstObject;
            if (!keyWindow) {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
                keyWindow = [UIApplication sharedApplication].keyWindow;
#pragma clang diagnostic pop
            }
            UIViewController *rootVC = keyWindow.rootViewController;

            // Find the topmost presented view controller
            while (rootVC.presentedViewController) {
                rootVC = rootVC.presentedViewController;
            }

            if (![rootVC isEqual:_webViewController]) {
                [rootVC presentViewController:_webViewController animated:NO completion:nil];
            }
        });
    }

    void _ZeyWinAds_EvaluateJavaScript(void* webViewPtr, const char* js) {
        if (webViewPtr == NULL || js == NULL) {
            return;
        }

        ZeyWinAdsWebViewController *controller = (__bridge ZeyWinAdsWebViewController*)webViewPtr;
        NSString *jsString = [NSString stringWithUTF8String:js];

        dispatch_async(dispatch_get_main_queue(), ^{
            [controller.webView evaluateJavaScript:jsString completionHandler:nil];
        });
    }

    void _ZeyWinAds_DestroyWebView(void* webViewPtr) {
        if (_webViewController == nil) {
            return;
        }

        dispatch_async(dispatch_get_main_queue(), ^{
            if (_webViewController.presentingViewController) {
                [_webViewController dismissViewControllerAnimated:NO completion:^{
                    _webViewController = nil;
                }];
            } else {
                _webViewController = nil;
            }
        });
    }
}
