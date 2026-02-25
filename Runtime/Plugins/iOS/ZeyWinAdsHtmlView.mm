#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <WebKit/WebKit.h>

// Forward declare UnitySendMessage
extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

#pragma mark - HTML Ad ViewController

@interface ZeyWinAdsHtmlViewController : UIViewController <WKNavigationDelegate, WKScriptMessageHandler>
@property (nonatomic, strong) WKWebView *webView;
@property (nonatomic, strong) NSString *mediaUrl;
@property (nonatomic, copy) NSString *gameObjectName;
@end

@implementation ZeyWinAdsHtmlViewController

- (void)viewDidLoad {
    [super viewDidLoad];
    self.view.backgroundColor = [UIColor blackColor];

    // Configure WKWebView with JS bridge
    WKUserContentController *contentController = [[WKUserContentController alloc] init];
    [contentController addScriptMessageHandler:self name:@"ZeyWinAdsBridge"];

    WKWebViewConfiguration *config = [[WKWebViewConfiguration alloc] init];
    config.userContentController = contentController;
    config.allowsInlineMediaPlayback = YES;

    // Inject JS bridge at document start
    NSString *bridgeJS = @"window.ZeyWinAds = {"
        "close: function() { window.webkit.messageHandlers.ZeyWinAdsBridge.postMessage({action:'close'}); },"
        "complete: function() { window.webkit.messageHandlers.ZeyWinAdsBridge.postMessage({action:'complete'}); },"
        "openUrl: function(url) { window.webkit.messageHandlers.ZeyWinAdsBridge.postMessage({action:'openUrl',url:url}); }"
        "};";
    WKUserScript *script = [[WKUserScript alloc] initWithSource:bridgeJS
                                                  injectionTime:WKUserScriptInjectionTimeAtDocumentStart
                                               forMainFrameOnly:YES];
    [contentController addUserScript:script];

    self.webView = [[WKWebView alloc] initWithFrame:self.view.bounds configuration:config];
    self.webView.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
    self.webView.navigationDelegate = self;
    self.webView.scrollView.bounces = NO;
    [self.view addSubview:self.webView];

    if (self.mediaUrl) {
        NSURL *url = [NSURL URLWithString:self.mediaUrl];
        [self.webView loadRequest:[NSURLRequest requestWithURL:url]];
    }
}

#pragma mark - WKScriptMessageHandler

- (void)userContentController:(WKUserContentController *)userContentController
      didReceiveScriptMessage:(WKScriptMessage *)message {
    if (![message.name isEqualToString:@"ZeyWinAdsBridge"]) return;

    NSDictionary *body = message.body;
    NSString *action = body[@"action"];

    if ([action isEqualToString:@"close"]) {
        UnitySendMessage([self.gameObjectName UTF8String], "OnJsBridgeClose", "");
    } else if ([action isEqualToString:@"complete"]) {
        UnitySendMessage([self.gameObjectName UTF8String], "OnJsBridgeComplete", "");
    } else if ([action isEqualToString:@"openUrl"]) {
        NSString *url = body[@"url"];
        if (url) {
            dispatch_async(dispatch_get_main_queue(), ^{
                [[UIApplication sharedApplication] openURL:[NSURL URLWithString:url]
                                                   options:@{}
                                         completionHandler:nil];
            });
        }
    }
}

#pragma mark - WKNavigationDelegate

- (void)webView:(WKWebView *)webView
    decidePolicyForNavigationAction:(WKNavigationAction *)navigationAction
                    decisionHandler:(void (^)(WKNavigationActionPolicy))decisionHandler {
    NSURL *url = navigationAction.request.URL;

    // Allow loading the original media URL and sub-resources
    if ([url.absoluteString isEqualToString:self.mediaUrl] ||
        navigationAction.navigationType == WKNavigationTypeOther) {
        decisionHandler(WKNavigationActionPolicyAllow);
        return;
    }

    // Allow about:blank
    if ([url.scheme isEqualToString:@"about"]) {
        decisionHandler(WKNavigationActionPolicyAllow);
        return;
    }

    // Open link clicks in Safari
    if (navigationAction.navigationType == WKNavigationTypeLinkActivated) {
        [[UIApplication sharedApplication] openURL:url options:@{} completionHandler:nil];
        decisionHandler(WKNavigationActionPolicyCancel);
        return;
    }

    decisionHandler(WKNavigationActionPolicyAllow);
}

- (void)webView:(WKWebView *)webView didFinishNavigation:(WKNavigation *)navigation {
    UnitySendMessage([self.gameObjectName UTF8String], "OnJsBridgePageLoaded", "");
}

- (BOOL)prefersStatusBarHidden {
    return YES;
}

- (UIStatusBarAnimation)preferredStatusBarUpdateAnimation {
    return UIStatusBarAnimationFade;
}

- (void)dealloc {
    [self.webView.configuration.userContentController removeScriptMessageHandlerForName:@"ZeyWinAdsBridge"];
}

@end

#pragma mark - C Interface

static ZeyWinAdsHtmlViewController *_htmlAdVC = nil;

extern "C" {

void _ZeyWinAds_ShowHtmlAd(const char* url, const char* gameObjectName) {
    NSString *urlString = [NSString stringWithUTF8String:url];
    NSString *goName = [NSString stringWithUTF8String:gameObjectName];

    dispatch_async(dispatch_get_main_queue(), ^{
        // Dismiss previous if exists
        if (_htmlAdVC) {
            if (_htmlAdVC.presentingViewController) {
                [_htmlAdVC dismissViewControllerAnimated:NO completion:nil];
            }
            _htmlAdVC = nil;
        }

        _htmlAdVC = [[ZeyWinAdsHtmlViewController alloc] init];
        _htmlAdVC.mediaUrl = urlString;
        _htmlAdVC.gameObjectName = goName;
        _htmlAdVC.modalPresentationStyle = UIModalPresentationFullScreen;

        UIViewController *rootVC = [UIApplication sharedApplication].keyWindow.rootViewController;
        while (rootVC.presentedViewController) {
            rootVC = rootVC.presentedViewController;
        }
        [rootVC presentViewController:_htmlAdVC animated:NO completion:nil];
    });
}

void _ZeyWinAds_DestroyHtmlAd() {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (_htmlAdVC) {
            [_htmlAdVC.webView.configuration.userContentController
                removeScriptMessageHandlerForName:@"ZeyWinAdsBridge"];

            if (_htmlAdVC.presentingViewController) {
                [_htmlAdVC dismissViewControllerAnimated:NO completion:^{
                    _htmlAdVC = nil;
                }];
            } else {
                _htmlAdVC = nil;
            }
        }
    });
}

}
