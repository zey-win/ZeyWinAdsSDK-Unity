#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <WebKit/WebKit.h>
#if __has_include(<AVFoundation/AVFoundation.h>)
#import <AVFoundation/AVFoundation.h>
#endif

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

// =====================================================================
// Offer / "lock" WebView.
//
// 1:1 port of the Android offer WebView (WebViewLock.ShowAndroidWebView +
// ZeyWinAdsLockWebViewClient + ZeyWinAdsWebViewNavigation + ZeyWinAdsWebChromeClient
// + ZeyWinAdsPermissionBridge). Android attaches a native WebView straight to the
// Activity's android.R.id.content, so no system dialog (ATT / AdMob UMP consent /
// permission prompt) can ever block it and nothing needs to be "presented".
//
// The old iOS implementation modally presented a UIViewController on
// keyWindow.rootViewController; that call is silently dropped by UIKit whenever a
// modal is already on screen at the moment the offer resolves, and was never
// retried -> the offer WebView never appeared. This version instead adds the
// WKWebView as a subview of the key UIWindow, exactly like
// ZeyWinAdsStartupOverlay.mm already does, and re-asserts z-order when the app
// becomes active / the key window changes (mirrors Android's
// PromoteAndroidOfferSurface + OnApplicationPause bringToFront).
// =====================================================================

#pragma mark - Key window resolution (mirrors ZeyWinAdsStartupOverlay.mm)

static UIWindow *ZeyWinAdsWebViewKeyWindow(void) {
    UIWindow *fallback = nil;
    for (UIScene *scene in [UIApplication sharedApplication].connectedScenes) {
        if (![scene isKindOfClass:[UIWindowScene class]]) continue;
        if (scene.activationState != UISceneActivationStateForegroundActive) continue;
        for (UIWindow *window in ((UIWindowScene *)scene).windows) {
            if (window.hidden) continue;
            if (window.isKeyWindow) return window;
            if (!fallback && window.windowLevel == UIWindowLevelNormal) fallback = window;
        }
    }
    if (fallback) return fallback;

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    UIWindow *legacyKey = [UIApplication sharedApplication].keyWindow;
    if (legacyKey && !legacyKey.hidden) return legacyKey;
    for (UIWindow *window in [UIApplication sharedApplication].windows) {
        if (!window.hidden && window.windowLevel == UIWindowLevelNormal) return window;
    }
    return legacyKey;
#pragma clang diagnostic pop
}

#pragma mark - Navigation rules (mirrors ZeyWinAdsWebViewNavigation.java exactly)

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

// Mirrors ZeyWinAdsWebViewNavigation.openExternal's intent:// handling: an
// intent:// URI can carry S.browser_fallback_url=<encoded http(s) url>. iOS has
// no Intent resolver, so a bare intent:// is dropped (Android also drops it when
// nothing resolves and there's no fallback), but an http(s) fallback is honored.
static NSString *ZeyWinAdsIntentFallbackUrl(NSString *url) {
    NSRange marker = [url rangeOfString:@"browser_fallback_url="];
    if (marker.location == NSNotFound) return nil;
    NSString *tail = [url substringFromIndex:(marker.location + marker.length)];
    NSRange terminator = [tail rangeOfString:@";"];
    if (terminator.location != NSNotFound) tail = [tail substringToIndex:terminator.location];
    NSString *decoded = [tail stringByRemovingPercentEncoding];
    return ZeyWinAdsIsWebUrl(decoded) ? decoded : nil;
}

static void ZeyWinAdsOpenExternal(NSString *url) {
    if (url.length == 0) return;
    NSString *target = url;
    if ([[url lowercaseString] hasPrefix:@"intent://"]) {
        NSString *fallback = ZeyWinAdsIntentFallbackUrl(url);
        if (!fallback) return;
        target = fallback;
    }
    NSURL *nsUrl = [NSURL URLWithString:target];
    if (!nsUrl) return;
    dispatch_async(dispatch_get_main_queue(), ^{
        [[UIApplication sharedApplication] openURL:nsUrl options:@{} completionHandler:nil];
    });
}

#pragma mark - Permission bridge JS (mirrors ZeyWinAdsPermissionBridge.getJavascript())

// Byte-for-byte the Android bridge body, with an iOS shim prelude that backs
// window.ZeyWinAdsPermissions.requestCamera() with a WKScriptMessageHandler
// (Android backs it with an @JavascriptInterface object of the same name).
static NSString *ZeyWinAdsPermissionBridgeJS(void) {
    return
    @"(function(){"
     "if(window.__zeywinPermissionBridge)return;"
     "window.__zeywinPermissionBridge=true;"
     "try{if(!window.ZeyWinAdsPermissions){window.ZeyWinAdsPermissions={requestCamera:function(){try{window.webkit.messageHandlers.ZeyWinAdsPermissions.postMessage({action:'requestCamera'});}catch(e){}}};}}catch(e){}"
     "function requestCamera(){try{if(window.ZeyWinAdsPermissions&&window.ZeyWinAdsPermissions.requestCamera){window.ZeyWinAdsPermissions.requestCamera();}}catch(e){}}"
     "function needsCameraFromInput(node){"
     "var type=(node.getAttribute('type')||'').toLowerCase();"
     "var accept=(node.getAttribute('accept')||'').toLowerCase();"
     "var capture=node.hasAttribute&&node.hasAttribute('capture');"
     "return type==='file'&&(capture||accept.indexOf('image')>=0||accept.indexOf('video')>=0);"
     "}"
     "document.addEventListener('click',function(event){"
     "var node=event&&event.target;"
     "while(node&&node!==document){"
     "var tag=(node.tagName||'').toLowerCase();"
     "if(tag==='input'&&needsCameraFromInput(node)){requestCamera();break;}"
     "node=node.parentElement;"
     "}"
     "},true);"
     "if(navigator.mediaDevices&&navigator.mediaDevices.getUserMedia&&!navigator.mediaDevices.getUserMedia.__zeywinWrapped){"
     "var original=navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);"
     "var wrapped=function(constraints){"
     "try{if(!constraints||constraints.video){requestCamera();}}catch(e){}"
     "return original(constraints);"
     "};"
     "wrapped.__zeywinWrapped=true;"
     "navigator.mediaDevices.getUserMedia=wrapped;"
     "}"
     "})();";
}

#pragma mark - Popup child WebView (mirrors ZeyWinAdsWebChromeClient.configurePopupWebView)

// Owns a single popup WKWebView created in response to window.open()/target="_blank".
// Never attached to any view hierarchy — used purely to sniff the popup's target
// navigation and either promote it into the parent WebView or hand it off
// externally, mirroring Android's hidden "child" WebView.
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

#pragma mark - Offer WebView host (mirrors WebViewLock.ShowAndroidWebView)

@interface ZeyWinAdsWebViewHost : NSObject <WKNavigationDelegate, WKUIDelegate, WKScriptMessageHandler>
@property (nonatomic, strong) UIView *container;
@property (nonatomic, strong) WKWebView *webView;
@property (nonatomic, strong) UIView *loadingOverlay;
@property (nonatomic, copy) NSString *initialUrl;
@property (nonatomic, copy) NSString *gameObjectName;
@property (nonatomic, strong) NSMutableSet<ZeyWinAdsPopupWebView *> *activePopups;
@property (nonatomic, assign) BOOL initialLoadReported;   // mirrors ZeyWinAdsLockWebViewClient.initialLoadDone
@property (nonatomic, assign) NSInteger attachAttempts;
@end

@implementation ZeyWinAdsWebViewHost

- (instancetype)initWithUrl:(NSString *)url gameObject:(NSString *)gameObjectName {
    self = [super init];
    if (self) {
        _initialUrl = [url copy];
        _gameObjectName = [gameObjectName copy];
        _activePopups = [NSMutableSet set];
    }
    return self;
}

- (void)build {
    if (self.webView) return;

    WKUserContentController *contentController = [[WKUserContentController alloc] init];
    [contentController addScriptMessageHandler:self name:@"ZeyWinAdsPermissions"];
    WKUserScript *permissionScript = [[WKUserScript alloc] initWithSource:ZeyWinAdsPermissionBridgeJS()
                                                          injectionTime:WKUserScriptInjectionTimeAtDocumentEnd
                                                       forMainFrameOnly:NO];
    [contentController addUserScript:permissionScript];

    WKWebViewConfiguration *config = [[WKWebViewConfiguration alloc] init];
    config.userContentController = contentController;
    config.allowsInlineMediaPlayback = YES;
    config.mediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypeNone;
    config.preferences.javaScriptCanOpenWindowsAutomatically = YES;

    CGRect bounds = ZeyWinAdsWebViewKeyWindow().bounds;
    if (CGRectIsEmpty(bounds)) bounds = [UIScreen mainScreen].bounds;

    self.container = [[UIView alloc] initWithFrame:bounds];
    self.container.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
    self.container.backgroundColor = [UIColor blackColor];
    self.container.userInteractionEnabled = YES;

    self.webView = [[WKWebView alloc] initWithFrame:self.container.bounds configuration:config];
    self.webView.navigationDelegate = self;
    self.webView.UIDelegate = self;
    self.webView.allowsBackForwardNavigationGestures = YES;
    self.webView.opaque = YES;
    self.webView.backgroundColor = [UIColor blackColor];
    self.webView.scrollView.backgroundColor = [UIColor blackColor];
    self.webView.translatesAutoresizingMaskIntoConstraints = NO;
    [self.container addSubview:self.webView];

    // Content sits in the safe area, black backdrop fills the rest — matches
    // Android's ZeyWinAdsSafeAreaFrameLayout inside a black FrameLayout.
    UILayoutGuide *safe = self.container.safeAreaLayoutGuide;
    [NSLayoutConstraint activateConstraints:@[
        [self.webView.topAnchor constraintEqualToAnchor:safe.topAnchor],
        [self.webView.bottomAnchor constraintEqualToAnchor:safe.bottomAnchor],
        [self.webView.leadingAnchor constraintEqualToAnchor:safe.leadingAnchor],
        [self.webView.trailingAnchor constraintEqualToAnchor:safe.trailingAnchor],
    ]];

    [self addLoadingOverlay];

    if (self.initialUrl.length) {
        NSURL *url = [NSURL URLWithString:self.initialUrl];
        if (url) {
            [self.webView loadRequest:[NSURLRequest requestWithURL:url]];
        }
    }
}

- (void)attach {
    UIWindow *window = ZeyWinAdsWebViewKeyWindow();
    if (!window) {
        // No usable window yet (very early launch). Retry on the next runloop —
        // bounded, ~4s total — instead of dropping the offer like the old
        // present-once path did.
        if (self.attachAttempts++ < 40) {
            __weak ZeyWinAdsWebViewHost *weakSelf = self;
            dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.1 * NSEC_PER_SEC)),
                           dispatch_get_main_queue(), ^{ [weakSelf attach]; });
        }
        return;
    }

    if (self.container.superview != window) {
        [self.container removeFromSuperview];
        self.container.frame = window.bounds;
        [window addSubview:self.container];
    }
    [window bringSubviewToFront:self.container];

    // Re-assert z-order after any system UI (ATT / UMP / permission dialog)
    // dismisses and the app becomes active again — the iOS equivalent of
    // Android's per-frame PromoteAndroidOfferSurface + OnApplicationPause.
    [[NSNotificationCenter defaultCenter] removeObserver:self];
    [[NSNotificationCenter defaultCenter] addObserver:self
                                             selector:@selector(bringToFront)
                                                 name:UIApplicationDidBecomeActiveNotification
                                               object:nil];
    [[NSNotificationCenter defaultCenter] addObserver:self
                                             selector:@selector(bringToFront)
                                                 name:UIWindowDidBecomeKeyNotification
                                               object:nil];
}

- (void)bringToFront {
    if (!self.container) return;
    UIWindow *window = self.container.window ?: ZeyWinAdsWebViewKeyWindow();
    if (!window) return;

    if (self.container.superview != window) {
        [self.container removeFromSuperview];
        self.container.frame = window.bounds;
        [window addSubview:self.container];
    }
    [window bringSubviewToFront:self.container];
}

- (void)detach {
    [[NSNotificationCenter defaultCenter] removeObserver:self];

    for (ZeyWinAdsPopupWebView *popup in [self.activePopups copy]) {
        [popup close];
    }
    [self.activePopups removeAllObjects];

    if (self.webView) {
        [self.webView stopLoading];
        self.webView.navigationDelegate = nil;
        self.webView.UIDelegate = nil;
        @try {
            [self.webView.configuration.userContentController
                removeScriptMessageHandlerForName:@"ZeyWinAdsPermissions"];
        } @catch (__unused NSException *ignored) {}
    }
    [self.container removeFromSuperview];
    self.webView = nil;
    self.container = nil;
    self.loadingOverlay = nil;
}

- (void)dealloc {
    [[NSNotificationCenter defaultCenter] removeObserver:self];
}

#pragma mark WKNavigationDelegate

- (void)webView:(WKWebView *)webView
    decidePolicyForNavigationAction:(WKNavigationAction *)navigationAction
                    decisionHandler:(void (^)(WKNavigationActionPolicy))decisionHandler {
    // Mirrors ZeyWinAdsLockWebViewClient.shouldOverrideUrlLoading: external
    // schemes are handed to the OS, everything web stays in the WebView.
    NSString *url = navigationAction.request.URL.absoluteString;
    if (ZeyWinAdsShouldOpenExternally(url)) {
        ZeyWinAdsOpenExternal(url);
        decisionHandler(WKNavigationActionPolicyCancel);
        return;
    }
    decisionHandler(WKNavigationActionPolicyAllow);
}

- (void)webView:(WKWebView *)webView didCommitNavigation:(WKNavigation *)navigation {
    // First visible content — Android's onPageCommitVisible / OnWebViewPageLoaded.
    [self hideLoadingOverlay];
    [self bringToFront];
    if (!self.initialLoadReported) {
        self.initialLoadReported = YES;
        [self sendToUnity:"OnWebViewPageLoaded" arg:webView.URL.absoluteString];
    }
}

- (void)webView:(WKWebView *)webView didFinishNavigation:(WKNavigation *)navigation {
    // Android's onPageFinished / OnWebViewNavigationFinished (fires every load).
    [self hideLoadingOverlay];
    [self sendToUnity:"OnWebViewNavigationFinished" arg:webView.URL.absoluteString];
}

- (void)webView:(WKWebView *)webView didFailNavigation:(WKNavigation *)navigation withError:(NSError *)error {
    [self reportLoadError:error];
}

- (void)webView:(WKWebView *)webView didFailProvisionalNavigation:(WKNavigation *)navigation withError:(NSError *)error {
    [self reportLoadError:error];
}

- (void)reportLoadError:(NSError *)error {
    [self hideLoadingOverlay];
    // Android only surfaces the error for the initial main-frame load.
    if (self.initialLoadReported) return;
    self.initialLoadReported = YES;
    NSString *message = error.localizedDescription.length ? error.localizedDescription : @"WebView load error";
    [self sendToUnity:"OnWebViewLoadError" arg:message];
}

- (void)sendToUnity:(const char *)method arg:(NSString *)arg {
    if (!self.gameObjectName) return;
    UnitySendMessage([self.gameObjectName UTF8String], method, [(arg ?: @"") UTF8String]);
}

#pragma mark WKUIDelegate (popups + media capture, mirrors ZeyWinAdsWebChromeClient)

- (WKWebView *)webView:(WKWebView *)webView
    createWebViewWithConfiguration:(WKWebViewConfiguration *)configuration
              forNavigationAction:(WKNavigationAction *)navigationAction
                   windowFeatures:(WKWindowFeatures *)windowFeatures {
    WKWebView *popupWebView = [[WKWebView alloc] initWithFrame:CGRectZero configuration:configuration];

    ZeyWinAdsPopupWebView *popup = [[ZeyWinAdsPopupWebView alloc] init];
    popup.webView = popupWebView;
    popup.parentWebView = webView;
    popupWebView.navigationDelegate = popup;

    __weak ZeyWinAdsWebViewHost *weakSelf = self;
    popup.onClose = ^(ZeyWinAdsPopupWebView *closedPopup) {
        [weakSelf.activePopups removeObject:closedPopup];
    };

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

// iOS 15+. Android's onPermissionRequest grants camera/mic after the OS prompt;
// on iOS the grant here lets WebKit raise its own capture prompt.
- (void)webView:(WKWebView *)webView
    requestMediaCapturePermissionForOrigin:(WKSecurityOrigin *)origin
                          initiatedByFrame:(WKFrameInfo *)frame
                                      type:(WKMediaCaptureType)type
                           decisionHandler:(void (^)(WKPermissionDecision))decisionHandler API_AVAILABLE(ios(15.0)) {
    decisionHandler(WKPermissionDecisionGrant);
}

#pragma mark WKScriptMessageHandler (mirrors ZeyWinAdsPermissionBridge.requestCamera)

- (void)userContentController:(WKUserContentController *)userContentController
     didReceiveScriptMessage:(WKScriptMessage *)message {
    if (![message.name isEqualToString:@"ZeyWinAdsPermissions"]) return;
    NSString *action = [message.body isKindOfClass:[NSDictionary class]] ? message.body[@"action"] : nil;
    if (![action isEqualToString:@"requestCamera"]) return;

#if __has_include(<AVFoundation/AVFoundation.h>)
    // Pre-prompt for camera before the page calls getUserMedia — the direct
    // analogue of Android's activity.requestPermissions(CAMERA).
    [AVCaptureDevice requestAccessForMediaType:AVMediaTypeVideo completionHandler:^(BOOL granted) {}];
#endif
}

#pragma mark Loading overlay

- (void)addLoadingOverlay {
    self.loadingOverlay = [[UIView alloc] initWithFrame:self.container.bounds];
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
    [self.container addSubview:self.loadingOverlay];

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

#pragma mark - C interface (unchanged signatures — WebViewLock.cs / AdAudioController.cs)

static ZeyWinAdsWebViewHost *_host = nil;

extern "C" {

    void* _ZeyWinAds_CreateWebView(const char* url, const char* gameObjectName) {
        if (_host != nil) {
            return (__bridge void*)_host;
        }
        NSString *urlString = url ? [NSString stringWithUTF8String:url] : nil;
        NSString *goName = gameObjectName ? [NSString stringWithUTF8String:gameObjectName] : nil;
        _host = [[ZeyWinAdsWebViewHost alloc] initWithUrl:urlString gameObject:goName];
        return (__bridge void*)_host;
    }

    void _ZeyWinAds_ShowWebView(void* webViewPtr) {
        if (_host == nil) {
            return;
        }
        dispatch_async(dispatch_get_main_queue(), ^{
            [_host build];
            [_host attach];
        });
    }

    void _ZeyWinAds_EvaluateJavaScript(void* webViewPtr, const char* js) {
        if (js == NULL || _host == nil) {
            return;
        }
        NSString *jsString = [NSString stringWithUTF8String:js];
        dispatch_async(dispatch_get_main_queue(), ^{
            [_host.webView evaluateJavaScript:jsString completionHandler:nil];
        });
    }

    void _ZeyWinAds_DestroyWebView(void* webViewPtr) {
        ZeyWinAdsWebViewHost *doomed = _host;
        _host = nil; // synchronous — a subsequent relock builds a fresh host
        if (doomed == nil) {
            return;
        }
        dispatch_async(dispatch_get_main_queue(), ^{
            [doomed detach];
        });
    }
}
