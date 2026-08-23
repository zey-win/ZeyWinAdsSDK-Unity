#import <Foundation/Foundation.h>
#include <stdlib.h>

static NSArray<NSString *>* ZeyWinAds_JailbreakAppPaths(void) {
    return @[
        @"/Applications/Cydia.app",
        @"/Applications/Sileo.app",
        @"/Applications/Zebra.app",
        @"/Applications/blackra1n.app",
        @"/Applications/FakeCarrier.app",
        @"/Applications/Icy.app",
    ];
}

static NSArray<NSString *>* ZeyWinAds_InjectionInfraPaths(void) {
    return @[
        @"/Library/MobileSubstrate/MobileSubstrate.dylib",
        @"/Library/MobileSubstrate/DynamicLibraries",
        @"/usr/lib/libsubstrate.dylib",
        @"/usr/lib/libhooker.dylib",
    ];
}

static NSArray<NSString *>* ZeyWinAds_PackageManagerPaths(void) {
    return @[
        @"/etc/apt",
        @"/private/var/lib/apt/",
        @"/private/var/lib/cydia",
        @"/var/cache/apt",
    ];
}

static NSArray<NSString *>* ZeyWinAds_ShellAccessPaths(void) {
    return @[
        @"/bin/bash",
        @"/bin/sh",
        @"/usr/sbin/sshd",
        @"/etc/ssh/sshd_config",
    ];
}

static BOOL ZeyWinAds_AnyPathExists(NSArray<NSString *> *paths, NSMutableArray<NSString *> *found) {
    BOOL any = NO;
    NSFileManager *fm = [NSFileManager defaultManager];
    for (NSString *path in paths) {
        if ([fm fileExistsAtPath:path]) {
            [found addObject:path];
            any = YES;
        }
    }
    return any;
}

static BOOL ZeyWinAds_SandboxEscapeProbe(void) {
    NSString *testPath = @"/private/zeywinads_jailbreak_test.txt";
    NSError *error = nil;
    BOOL wrote = [@"zeywinads" writeToFile:testPath
                                 atomically:YES
                                   encoding:NSUTF8StringEncoding
                                      error:&error];
    if (wrote) {
        [[NSFileManager defaultManager] removeItemAtPath:testPath error:nil];
        return YES;
    }
    return NO;
}

static BOOL ZeyWinAds_InjectionEnvProbe(void) {
    const char *env = getenv("DYLD_INSERT_LIBRARIES");
    return env != NULL && strlen(env) > 0;
}

static NSString* ZeyWinAds_JoinIndicators(NSArray<NSString *> *indicators) {
    return [indicators componentsJoinedByString:@","];
}

static const char* ZeyWinAds_CStringCopy(NSString *value) {
    const char *utf8 = [value UTF8String];
    return utf8 ? strdup(utf8) : strdup("");
}

extern "C" {
    // High-confidence jailbreak indicators — mirrors Android's getRootIndicators().
    const char* _ZeyWinAds_GetRootIndicators(void) {
        NSMutableArray<NSString *> *found = [NSMutableArray array];
        ZeyWinAds_AnyPathExists(ZeyWinAds_JailbreakAppPaths(), found);
        ZeyWinAds_AnyPathExists(ZeyWinAds_InjectionInfraPaths(), found);
        if (ZeyWinAds_SandboxEscapeProbe()) {
            [found addObject:@"sandbox_escape"];
        }
        return ZeyWinAds_CStringCopy(ZeyWinAds_JoinIndicators(found));
    }

    // Full indicator superset — mirrors Android's getDetectedPackages().
    const char* _ZeyWinAds_GetDetectedPackages(void) {
        NSMutableArray<NSString *> *found = [NSMutableArray array];
        ZeyWinAds_AnyPathExists(ZeyWinAds_JailbreakAppPaths(), found);
        ZeyWinAds_AnyPathExists(ZeyWinAds_InjectionInfraPaths(), found);
        ZeyWinAds_AnyPathExists(ZeyWinAds_PackageManagerPaths(), found);
        ZeyWinAds_AnyPathExists(ZeyWinAds_ShellAccessPaths(), found);
        if (ZeyWinAds_SandboxEscapeProbe()) {
            [found addObject:@"sandbox_escape"];
        }
        if (ZeyWinAds_InjectionEnvProbe()) {
            [found addObject:@"dyld_insert_libraries"];
        }
        return ZeyWinAds_CStringCopy(ZeyWinAds_JoinIndicators(found));
    }
}
