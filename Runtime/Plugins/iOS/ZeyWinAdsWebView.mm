#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <WebKit/WebKit.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

@interface ZeyWinAdsWebViewController : UIViewController <WKNavigationDelegate>
@property (nonatomic, strong) WKWebView *webView;
@property (nonatomic, strong) UIView *loadingOverlay;
@property (nonatomic, strong) NSString *initialUrl;
@property (nonatomic, copy) NSString *gameObjectName;
@end

@implementation ZeyWinAdsWebViewController

- (void)viewDidLoad {
    [super viewDidLoad];

    // Configure WKWebView
    WKWebViewConfiguration *config = [[WKWebViewConfiguration alloc] init];
    config.allowsInlineMediaPlayback = YES;
    config.mediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypeNone;

    self.webView = [[WKWebView alloc] initWithFrame:self.view.bounds configuration:config];
    self.webView.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
    self.webView.navigationDelegate = self;
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
