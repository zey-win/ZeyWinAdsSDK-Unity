#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <WebKit/WebKit.h>

@interface ZeyWinAdsWebViewController : UIViewController <WKNavigationDelegate>
@property (nonatomic, strong) WKWebView *webView;
@property (nonatomic, strong) NSString *initialUrl;
@end

@implementation ZeyWinAdsWebViewController

- (void)viewDidLoad {
    [super viewDidLoad];

    // Configure WKWebView
    WKWebViewConfiguration *config = [[WKWebViewConfiguration alloc] init];
    config.allowsInlineMediaPlayback = YES;

    self.webView = [[WKWebView alloc] initWithFrame:self.view.bounds configuration:config];
    self.webView.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
    self.webView.navigationDelegate = self;
    self.webView.allowsBackForwardNavigationGestures = YES;

    [self.view addSubview:self.webView];

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

@end

static ZeyWinAdsWebViewController *_webViewController = nil;

extern "C" {
    void* _ZeyWinAds_CreateWebView(const char* url) {
        if (_webViewController != nil) {
            return (__bridge void*)_webViewController;
        }

        NSString *urlString = [NSString stringWithUTF8String:url];

        _webViewController = [[ZeyWinAdsWebViewController alloc] init];
        _webViewController.initialUrl = urlString;
        _webViewController.modalPresentationStyle = UIModalPresentationFullScreen;

        return (__bridge void*)_webViewController;
    }

    void _ZeyWinAds_ShowWebView(void* webViewPtr) {
        if (webViewPtr == NULL || _webViewController == nil) {
            return;
        }

        dispatch_async(dispatch_get_main_queue(), ^{
            UIViewController *rootVC = [UIApplication sharedApplication].keyWindow.rootViewController;

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
