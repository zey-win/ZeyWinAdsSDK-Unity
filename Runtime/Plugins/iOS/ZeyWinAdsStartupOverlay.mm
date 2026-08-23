#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

// Mirrors ZeyWinAdsLoadingOverlay.java / ZeyWinAdsStartupOverlay.java: full-screen
// navy backdrop with an animated "money bar" progress indicator, shown before
// Unity C# has had a chance to create its own UI and dismissed explicitly by
// SDK code once WebView, fallback, or startup checks finish.

#pragma mark - Progress curve (mirrors Android MoneyProgressView keyframes)

static const NSTimeInterval kZeyWinAdsProgressDuration = 8.0;
static const NSTimeInterval kZeyWinAdsAutoDismissDelay = 15.0;

static const CGFloat kZeyWinAdsProgressTimes[]  = {0.0f, 0.08f, 0.14f, 0.27f, 0.34f, 0.48f, 0.58f, 0.71f, 0.83f, 0.93f, 1.0f};
static const CGFloat kZeyWinAdsProgressValues[] = {0.0f, 0.03f, 0.12f, 0.18f, 0.36f, 0.45f, 0.62f, 0.70f, 0.86f, 0.92f, 1.0f};
static const NSInteger kZeyWinAdsProgressCount = 11;

static CGFloat ZeyWinAdsSmoothStep(CGFloat t) {
    return t * t * (3.0f - 2.0f * t);
}

static CGFloat ZeyWinAdsEvaluateSteppedProgress(CGFloat time) {
    time = MAX(0.0f, MIN(1.0f, time));
    for (NSInteger i = 1; i < kZeyWinAdsProgressCount; i++) {
        if (time > kZeyWinAdsProgressTimes[i]) {
            continue;
        }
        CGFloat span = kZeyWinAdsProgressTimes[i] - kZeyWinAdsProgressTimes[i - 1];
        CGFloat segment = span < 0.0001f ? 0.0f : MAX(0.0f, MIN(1.0f, (time - kZeyWinAdsProgressTimes[i - 1]) / span));
        segment = ZeyWinAdsSmoothStep(segment);
        return kZeyWinAdsProgressValues[i - 1] + (kZeyWinAdsProgressValues[i] - kZeyWinAdsProgressValues[i - 1]) * segment;
    }
    return 1.0f;
}

#pragma mark - Money icon bitmap (byte-identical to Android's MoneyImageBase64 asset)

static NSString * const kZeyWinAdsMoneyImageBase64 =
    @"iVBORw0KGgoAAAANSUhEUgAAADQAAABgCAYAAABSZ1EKAAAgAElEQVR4nMW8Waxl2Xke9q1pj2c+5851a+yu6nlgk2JzECVK"
    @"lChKSGjBhgfoQYDtxE+GgQAJED8Eecn0EAR+8IPfHCBOgEAyBQNWLEuWGFGiOXWzyZ675uHWnc+85zUE/zq3msVmMxTjBFmF"
    @"W/fWuefsvf/1j9/3/6vYJz7177AxPMHD0zV0kgVirqEbBS0Y7t3fFf/kf/zPN+dZuvff/Hf/ZbS3v1O+8vL30etNEIYV1jaO"
    @"kMYl3v/gCfzKF76B137wIk6O1/DGW88hjSqsrZ0gTZbotDIkSYF2Osf64Bg1t3j2xe/jv/6v/if8t//kP8PXfv9VMDnCnfd2"
    @"cfP6LvrrGZRq0OmViFON/kaJzd1TvPEXz6HOhrjyzF20ugsYI/DRJbZ2fhetJMeiSBHIBqHQsFZgkbf4xtrxf/HCCz/860mg"
    @"f+vGnQthElW90/FQdLvzQVnGtZBaDfrTZjrrIQga9HtTnIyHaBqF0/EQnXQJIQ0kN1CygQFHO8rR6ACDtUMsl23cuX0Nv/ob"
    @"f4Q/+5NP48q1MT54axtJW8M5QEgHziwYZ+gMcnBucfxwDUIK9NamsIZ/vEBpXCArYyRRgbJI0EmzYJBO/+Fv/87v/bXnX/ne"
    @"V7vDw5c+8+lv/s1nX/jBcy89/9ZXaqM+0WnNny/K5KpS+ok4qvS9e7udJy/fmhyPh9xo6U5Ph/4GcVRBCBLIQAgLzp0XdLbo"
    @"4vmXvof//X/7Xfz23/oa3nzjAroDjfmkhdmkhSAyYACCcPU5IRmGm1McPRihKVN0+wtIqeH8uz4i0Kg/wd29XWxtHmAyGSKQ"
    @"+lNf/c0//Gef+PJfXFh74r6AmHOTTPHsCzd2Ouu3zn/68994+fyVWy9cvfbuZ1Sc/8ZoePLVk9PBb14+f/9ituxsB0oPP7j+"
    @"5L4QJqnr0KVpbp3liMIaxnH0WktkeYKLT1zH9Q+eRqNTPPfS9/D6d57H7sU53n9zB61O47UkpYPgFoyRVhbQtcDkeIhAAd3h"
    @"DPYjZieGa38P2+tHmM76AHe801p8+atf+cN/9vJXvtm1Vw7T/UKx3DCUziKzDC6s+JLV2Lx4EiWjg9a1F95qjbb2Rp//5T/e"
    @"TdLlFy5fvvHLvd7kN7/4S3/xm4rrzd2d/Vdv3LxSDPqTK00TdbnUCzgWpknWlHWIJ66+j6/9/t/G7/zdf4Gv/7uXsXtpgf17"
    @"Q2+WQllvcmR69CUDh/5ojoN7a9B1gv5oho8oCOLlF38dJ9MRdtYPcePu5Wd/+7f+4B//2hf/+DPrn7ge3uYKY8OhuYEJJayU"
    @"qEUAKzhmjUDDG5bRRToFatGgTUFg/Tjevvhu99nn37r06U/+5S8988w7v/j5V7/16X5/9is7G/uv3nlwYXdn6+FXuOWDybyb"
    @"XXvq7cV3vvU5N9qYYDh6gDt3LmAwKnH7g020OjWsI7Oz4HBwEBhtTbGcxZiPe4jTBq3uEtb+yJfEFz77GVy/8wTrtrLzv/r5"
    @"P/uf/9p//AcvXXzyTnxsgZnhCGwDYSzAASYYNAdq52BIOBagZhKkwQYcSwM00qARFhm3KNKK9dZPZGtwf/tzn39td7R+59ov"
    @"fe7Pnjl37uGX253TT82z9t9pd+fXqir97IMHV+yXvvInp3/6R6+E5y8v63s3RzCOtKTBGfPBhbQUJRpJu8Tx3hqcjdBfm8C5"
    @"H6lJXLv2VYpq/XPbD//x3/4bv/cfXX323XbBLI4rhsgYyNqi1ThI66CcBZyGNQbGMq8xyyQcZ3DQYJJBOwHDOTI45LHDggEZ"
    @"czjNgLibgYWL9vnLB4jbB70XP/PtYZC4T+0++fbLo7XsPx1u3n3pxvXtL69tzIb7964wbRFks7QOo5oBXCiljXMK6+fGmBy3"
    @"kc16aLULREkJ51ZaYq98+t/Iz7z4nX/wK1/4+v/wxa/8ZZqxCuMc0AEg6T0c0DW86muvJaAGUABohPShGNxCgMyCvhikAQxn"
    @"sPSHNo8BgVFAHSJwHKYC2kEGxwyGQ+B4IhAmwHLexWzc0ycH52WrPXrvL//i6oNisXb95lvn3owTXUxPOje7o+LecGsxb6pw"
    @"8s53nsPmdoaLT92GsQKgIHL/4Bwubt0enTt307KowmSu0EQSlavRChlQaoQJQ105KLI8B28CijmUVkMzwBmAMUAygDnnf++0"
    @"BKyGIN0JB+EsjLJwkOBSYNq0IXiD2WkDGWssjEO0NkZ/Yyo3nj1CAPvU9ktff8o161863d8aH+9t1/PTrb17N9fvMCQ3J2O3"
    @"OHfl6N+f7G3sF3l8L06qJeP0DMzpWdl6Z5y33NGco7EWizBB5lLogB68guAGXDgUeY5ASmijIZWCNBaCAdY6OG2hSIMOq+Ah"
    @"AzCr4GwF6QwYSY0CltWwisHJAJZM1SpoyWG4wbTSsBEJv0RaA4ksEQQnEJduDi48kaDUg81fZ+rle7efcrpZm8/HD+tb7z3/"
    @"vSqrXv/ha9E/nU7Fodxe30OZJ8ezPArIrGbO4d25w1QztAOJRAGjTg+Ba9AZAIsmA9lMyDm0MH7nmdeahqWfrUFD5ig5nKXf"
    @"CzRupSHrnPc1UqeVFoYJCMfAvN7IXAGjgZKsuGQw0iHnDMZVaGQFoSbIWMgvPvEAc73evyav4omXT37r+putL/7L//Xzfz4b"
    @"J4cyjZdoaql0HUz7Lbt5uxjhgwXw9r0FsHSQMTAaklACO60WRt11cJdhPUpQVXP0Q4WmLiEjB24NXFnB8QJgFYkFy5g3x1pT"
    @"LbOyAA5BLu6zvLEOzKw2JjZuZdKW/JCj0Qaa7JjDJ9nGAg2r0IgKuslxd1FBocFkfFV1e3NlSgZ5694VtFvzaZHHPd4osHYP"
    @"gWQ4vjHD/f0GSwt0jscIpMEoSbHdVVgLBTbbNdqxwCiVaMcpQsnRDRW4oFJngdgW4JoeqgYjk+SAYSv/oihpa+EjjGCSAhhI"
    @"T2AGwloIw73mAANHPkmqkxSTJRoj0egGZW6wmGQwyyXGD4MFTMW7/SWXQVBDCj3NT0ZvZfPOJ4uuQqMrJLFAGTag+x5L2kVg"
    @"b5zh7oIhqhzWAoGgNhimLSTK4tzWEL20wdpQIE5SdKIYQ7LFpAK3FRLWoG4qRIaejWFR1WAQMI55f2QkMaOwb6BJAuEgOWnX"
    @"wUnAUQ7UEWrdQVbkWCxmKJc5Elaj25u1223XvXenBdlNZ1Ci0roKmYwMFC/QVDMo3UBKQMUMOqZQxr3OG+3QS4GQklytMJ0V"
    @"mGiLe3v3EYQKkirhYYA04djoh+i2BXaGKdpCo9+JEVuNiHNw1fjsbyrjNUbhnkyPouaCCTDhEEnt/YtMljMBYyWaSmI+dxgv"
    @"KaI1iNMxVHdRL2adcHw8sHI676PKw9PlpNMz2qDPTnCeN/jArqrdhiIFLBx3PgeRjXe6wLMXE1zp96BnDlnWIJ/n2D8yKJYG"
    @"070F9hnHe5h5X+l0BToRx9pAYnvI0AsttkcBUqEwSh2ayiHiFnFTYso4FipCyDgsy8D00uc3QT5oLYp8ieV86c3YCoamkSiq"
    @"uNJMj4Oo5FIbibKJ6qxIbjlWXaG0mWhG/gtOZk3pxIOTlWOSNcQSWG8znGsLjNoKFQRyHcCWFtWpQZNb3J0zjKsKe0c1cmNw"
    @"836FWw8cosBBCo7RSKCTBDi3HaPfDtGPJIYqQB1zHyENWggQ+bTA7dJbRzEpsJhQimBQFPINRxoBpyYWaXfebw8CKwfpBOut"
    @"4552/KB00BqQFH+oSnAC8HWfII8kDVlAO8QAVFVAmRk4l2iZBryyWA85kj6gtls4rjkmro3ccTwYl8hqi5PJHEcLg+Nxg3Fj"
    @"8M7NHGy/ghQCa6MIo0QgWbPod4z3vzYHhlGEfkj3LbEsG+QZWYkEDyhoSugqggoNy5et2+P985CVCXFwtHMwXnTr6bItOjiF"
    @"YwIlt2hC58WDCFcZUxc+/E4XQDbTYL0coZJAUaJlHJCRsBxNmaEbRQgsQ9AdYCu0sDL2Znt3Acwai4PS4OYJ8PqtU9ze0/hg"
    @"WSEJJfh9jX4K9Ooa5zvAJy8HaO8KxEpCSQ3BOOiP32KKmOSHTqfGJL4el3mdeHM6nQ6PVXvGGCQEFLgtV4UbSwAWAtLCUVFH"
    @"9sy9vsCg/A0EJUz6EkAGi8JSGirgZTxaIAxiX9cFicITsUTZFtiCQm+gkCHGaVMgtxY5YR4J7E2AeV5Bz4Dttsal7QRJIBGG"
    @"Ahl3cM3Krzk3sCBI78ac4Dr3lmURpTm40o02cRPCqZADCWMI8xBLKwFSOdO0Jd6myRwjKl9c5JOfsxJWN77arQLmK3GwANZx"
    @"cFPDUC4yBqyuoCJydot2HGEAhaG0aAnKUwK6y2AYQ4uSqXMIWg6Fc6gJvjAOJUM4ul6jEcbOv1Y1OeJO2XOCwwWG6mSGZZWg"
    @"FjzLZiMpKJkJ7Ws3ySj/1IDOAbf0mb+sGZqaU3UBVxs0NWAs1WUCdUDVmkPOLHKmUTCgUoHP9j6XUHI1FjVl/FojUhy80RCV"
    @"Q2ytr/noeUoIsICjodQUOdSW6kABxkLvN8xHXIaqIY+vIRMoLl1XawVpuUWYzPmDg93XmlwS+E+toDc6SFWCce5tn0KmB1CU"
    @"/Oj62sI1JQKTINQrk9SyBFmDJgAIA0Zma1efZXaFlilSUuRpggSVo68KjWlQBatqgqKQ1gyNW5VMdoW04Ot28hnuwNWZ71AB"
    @"QVoqwgMuqma0tc+4klQPMceM6+jG2YRVUJ6UAErjVh8jYdwKEbLAoeEWS6bRCA3BHZRhkA0QGiCVAWLaxdpCEdvjLAILhBQZ"
    @"LaAMEDOGgEmUlULZSBjSJOmfbsrowalMsqiM3zefVAWis+LWeqFoe6UiiQM0ph465GJ20nGckJyuAxcFOsjzXrCgupdxBFQl"
    @"rMopeNxtpEd7dCECelUIFLFDFmkUqUQRCETpAE6miKMWummKSIYIwBAYILIrgegqATmioesH6CYJIrJ/xmAQeD8lRyAOjm5d"
    @"auZBpIH0QtKXw2p/KU8qwRElD5kM8udnpz1/fbTaGRbzzbems0vvV3bvBc0naKh4I40QmWcC7/jM1RC6BkVqGQFGCVTCAaFA"
    @"ZdooHLC0DZyx6MYCRkv02gwy0+CV9gGFnW0Qk2eh2AkoMmtCZ1wABdV+FoHACmu5lTZoBwwzK4EsQCmRzN9qII6nwlbpv9++"
    @"cAg56OcQkhMbaavmQin4m2iwxNKUvvTnPILVLR8qhWmQKqAXAb0E6AvmocBho3GwCHFyeILDzMBag1GbYdBTeHbIsUEfEkBi"
    @"OZw2Hi4HKgB3BWKz8AGIfo9iVZkTyjVnKDgUzPsd6cuxGkI5UCwgL/CcnHLQRdKsbSx2FTuAFCqE9bvfTFXELlU6QRCGcGyJ"
    @"ykfGCMy1CfB77LHeAp4eMZyPOBJnwRvg4djgtTsz3HpQ4DQHFoY4A2Czb3BwgeOFNYGdVGI9kt4pKDk3TKPWGawrEAQGzuN7"
    @"4X2C2BG6F8F5ym+acBE0eGDB1YrQYJb5aj0MJzDZDmCTvCy6kMRpqaAhnFIdPNx640qV/ppJFXgE1NahsQZcWQ/gusrh/Ijj"
    @"Wl/gihLYLDROSo29rIvrh0e4PwVI9pILLI3D4QSY1AbjhcVzmwLPbUi0lPI0V+0yj5UoSrjwLPyRmngMYxvafnCirrzZkxeV"
    @"HkuRZfp8aCW4C6BsiUi5oCr6bHZ0FbI/zH3JXyy7rixaS2EjZ2rBUiERUN5pMo/9W32DqzsJXt6R2E4ajJxDlDs0Jce4lJgv"
    @"DWoiT1ICaxIyCD0suD9dQh4679b9pMG5XnAWFEr0CTS2gEQx8KmDDTlkIGFrg6yyaCQhXXhAR6FBCLdSInc+PShhYXWAOt8u"
    @"md0cc1lBNtUIi2mL7HERI8x7zchshans1wFadYOZ1ojSGUYDgUvnU1zuReg7DZ7VkNqgVgZWTwGuoRIGI0IYTbGNSqoGTuXY"
    @"LyySE4fdnkYvdlhPJRJO/F2FnUiiZ2OkxQKFaSBDhyawqCXH1BkqD1HzFd/HqZZihJkcjLBQYYGGriPkTEVzvblbgNd1z0OC"
    @"IguMbC4fnnfn5cvBNTxfb+JqLrAxAy5ah91QYxRWCHzGKCCMhlMcdUti1J5jY+gQBCsuzkVEgsCTkDaKUUmBZQ0czHwQA7cS"
    @"qWFoGWAj6OD5bg9PKYl4qeFysogGLnFoFDzhEpAfow3pEria+4BAmqpigyZ1MGqxEaR7TxsdQp7ul5gcOQy2AnVyXS3E+5E7"
    @"t9tjv5yvoSmPcN5mqJYWW47jCS2wjZASMQTtmrKIEuDpocPJKTDLDLK8Aj0po3aIFGCeL+AocoOydsgrB1dTXrJQjsEFAS6t"
    @"RTiYKTyoNA6y8sydnC8yqkagrmKE6CDmmU/M3JcIxheyHG0Edb8gkmXQ3WPy4c3K108IjFm89U5665+/nl0ePmh13AyfWGp0"
    @"AoeAcwR3BJ7bPI8uiyFabXB+gpwV2GEWaRyg2DCo8xr6yOLuYQ0MGqrLwSPhYyyXVGVY7+gEIinXWO1QoMBwEGD3ksU28Rdj"
    @"59kdigZUYlF9BpMgsBESs/Q1X7xKTLCOIW1SbKe9OKzT8c0Phk7KgGozjjjbv9rfe/v55uDb0UF55JPepa5G3AbmHYd0x6F1"
    @"v8To2T4wcog2UuQYo0ekJBx2zrdwtZXh23en+MM7pziZWh8BkWoMI2C7D1zYDHyi5Vz7BEkEiGFzJInB9pbFVclxz1ocF4Br"
    @"4Kt2LiS0W9FfDjWIzG25FX9na4eNOkB8iCY2drMs5KpSoNJ9k88uXMzM0+28L0e2gLRLJCccA2ExjoDibaD57jEONidQ2xzy"
    @"vEN8KYHYcRj0ImztruHScBeffMLi0+dm+MHtO7hXWDws9nF+kGC41uD8IMZ6xKGKAo6oKLGqph2W6MccT65JHFUWr98ymCyd"
    @"d/7cWSxdjZwgjWjApPMMLddAxyhsLRhS+boU+nOzzYEkOtoiZJlITLG5213rpPZVyOoGpL2Lopl44mKjJMIkRn6gUF+3KPQY"
    @"5QYwHU0gt2KUsUP38hhyu4fRU5v40oUUv9p9DrNLCvfUGKGsEbbowRdQJkdFbL1TnuuurfZguMUsLscxJl2NBy5DngMqAWyj"
    @"URFhKSiaMZ9CiAkKA2CkW1i/F2J60rDNltm4H0rIHpuACeO6SdXfuTLo9sQr6ExjZIsW3PIe5tUCyXKB0DJ0HZVBFhUcssMc"
    @"2aGDfbdAL1ZY/PED8O4BJpu3EVxNwM8N0LvWx7mNBrtPbSOPKrS2+qhOj2A6KRBpzNQULV4gJ3qMR8hsih1U6GYZujmw1EBE"
    @"TS9bwbgIHAE4cRoaaDcK5+stmG9LfOvtsVl/RgX9sIEMVO1JqlY72O08sa3ba1CYFAgLgaDsAIsZHDV3juawlYFYFODGAylw"
    @"myG0Ep1cgQp3U1rgtMH0zWPU7Qn21xmqloQ5/xDlOkN6KUFKrM6mws7VNlLTYO2pIao5ULU6/itkE9wMKuhqjtuEuTRDRNV9"
    @"WYPVEn1Cc6lGJ0vAf2Dxg3/9AFwZPl8ze+fbDXUfgGUlE+FEywquKNzaDiMyG7psECRkWwVY7sAXGjiZQhQlwnqBKjuFzgvk"
    @"WQ1dVuiZAMtmii3EqGYF5BxYuAb2XW800BsSDwMOM4pwkjhs9wWiKwlUv4V0g4NfbHmHL5Z9tE2OC7JBPx7gmuhjMx6ho0v0"
    @"OutYZkdo9hocfO0Qkw8mGL74ZL1wQS+joDAvHQlUVmW5p3WQI88S1BWcKIHAoagLqE7sIxPb2UKTj4hCQZJXCMocpixRn45R"
    @"zcaoCof69ACLXEPqGMtshh4EdFmjT8XLXQ3qxdW3aiwVMC6BeGOBsjpC98IMbP0horbC82KJQQDsrwFJGmAnTLHZ2kK73YMZ"
    @"HyA/WOLt33+A0XcaJCIAZ9vm3sO19x4uUiaJ/S+pJGPOsbCJqT9jKw1XN1CCwFgAkxvPBJWzBfJWDGUZWmkA2ep4/jnc3EFS"
    @"Fd6Bw9OJ70CUZQU5nWE+y+AWE/AiQ1BWiI1FahxCW6KisH1oECOAmeZgwQIzViNOLTYih40B+egYnXMV+k8ZRIM1LIk5fWeG"
    @"/JsOcbYiKEU7SQKIq3cnw39D9AUGQd6rFds9soydEwJcSQgtwYmtoXajCSi+gqsQrCSobYGcNEjkCJX0CjwNIEQA1R54GB3o"
    @"GryoUJc16tkMqsyRjWdgywKL8dgn19pkcEuLuBaIohZM+RAt5TxpGWeAPQXqmxXCToX6TydobOS57yBv8Oy4hw02RNxTqEWo"
    @"TY1poipqw7RRmmRa6vF4bjErnOtSJo4M9zUX9adA/UsuwZlCxBSUzSCoAGyMD7moG1/ieKAihefnBA989Rx1BwhGI/CmAUEZ"
    @"tswhsgL5MoedL9AsczRFhaaqIBcCtj5AP64hJwWM5UiJk6Du+iGl1cI3xc55an+EbrxD+4xMh7KemnJyQiSQkdB6iazE640z"
    @"SStqg08lGPEIJAiVGfSEEF5rTBLFVaxwjPUtYnhGkSCmxaorRUSH4ES9wJoaTAnPC4ggBBtESHoNImKPSg1uNcqiRF03cMsC"
    @"5fQEs2wOtMfg8ymiOkdRZL5jKKmhBo0+E54+li0LtCSWYg19Y4baOsimoeqZO6dNqUuR1TnvdZCAmZnnkz1bTzBRr8zQsRJG"
    @"lmDMeMqYExgTZ+0WomnpO9VuxEUQMU5dAtIk43B15ZkX5grwkEMpByYlWu0uTQv4n8tyF85o1NMpdEYxagG3mKMZT2GKHHp5"
    @"TO13z+khnUEzC8kvoRXw5rkuwQ9hwF0NpaR0NYq4dj2vHWpfPJo78dwvMRa+UeNJRf8rwvWMqgjrWRvnQTH3XB73713NBTDS"
    @"JDGNlCOI4SDbqwo/v+MqElL4l8kiIqWAVoiwtQE0DSqmgCyDaQxMtoQ4PYC7PYfLDFDX/jqynKDD677lIWSkGixdwDIRFxVv"
    @"eEX52J0xMETzCgdG2yeUZ049DLaRl4/AlhHGN4dXVIzz3WwSzRLmd3qF/T1l2ngt+p/to44GByMKS+sVjVXWEKIG15UvSqlY"
    @"ijmZVeA3p9noAb0OWHGC5vo98IoQ7gDBPNZPBuMLC3lbyiPXgbXKFW62MFwug1a6AR3RGBRctaJwPWFM5kfIhkg5k3gY7F/3"
    @"pKSBZRqO21VzDGf9JBhPQ3Hf6288JcZtsPJJz+rwVZtGEgNE7yOTtrBm9X4isJhpwIiuoksS/UW+JzQaVyGkTWliKK1EacX7"
    @"zjInaQJk3Zzw0Jk0PjrV4SbtWO5bghQA0Il8n4FoLOYU3Jx5VoYTy0eBgK80Q4nXD3NQGUxsDQl4NmXyKK6Qq3lOzkg4Z86o"
    @"2NVfpFUfwsBR6xqS85WFe01S85jaoRpKM5S8ghENqLpzbglm+ugl7OKNfGTkZ6p3EaJm5VStTV+701kc3kQcHEGOaug+OX8M"
    @"HqdwDT1RBNbqASXZfQBUZzMzlJca7q10xdw4WC5X/sVon/VKMD+TQOGfryShz7oztIaziSo/08A9aiWky87aNj4fku5dBWNK"
    @"4j7BWQDmUjge4GqPm0tFzmSIBnNCFlUdq3qaPHznFi6rBbQ4Qt5tIM71IeMYNIzDeQssHUH0OgCLwaJwJYCgZFAAQvicRA5G"
    @"eYdRo8xRwG58C4XihKEGjpeZecHYo0mqR9w5iUztRjJpR1YhfLONggwxRw1BcwWfCgjxCuqpq4SdVMpd6lonb7MLWPIu6yF/"
    @"cxp9f3G31h3e7LF+M0FVztGaTPwHKd7TTtkw8onS9bpQnR7Q7oG1OhBhBCcUWCtaaYwnngUl4VhT+ofXJkMTAaw2PlF78pqq"
    @"dm++btUlJKGI7PdB1p1FVyIWrc8MNNxrAqKQKVeKVSLXHDyfCY0eZCM6aDPtSpd+/4fi0v9x6ZXx3yunb7PD8RQxTV3kS4RI"
    @"oOfHvrdKS4/3oKlL3erAhCnCQR+60/btcdEZQKQpkLTBSGNEgseJj5i+emCVj5z2bBzNj4Xx1ZAQyU+b8Gj6jbTouXDrpTwL"
    @"OMwHF1c5P0tEaq+yDHvZcZaVc8iU2ifCwEhTjNeu/OkTf6v6nYuqShZv/TnkYQV5VyAv2giOLLJ8TrgMvKlWN50WRNCCH94H"
    @"iwNo6vp1e1hGCZLhGnSSQHX7EL01sDgEUwZBpwPb5EASwy4XEFEMZ1YmyVXgQ/cj31mZJvc+6X0pCqGKxk96MepUkKDa4Kg8"
    @"dFPjejx9ltzDQCRANLRs3m3frV9NDqJrw8vy7iXMbuwhf48iyDZOfjDGBaNxfH8P8WEGkxVQmUGeEVdtURYUdeDnCChh2ts3"
    @"vUAmieHSNmSrC9kKgdEIxKe7Xh88ioEyg2ilQF16sxWWe7hNiZ3EoPzEzJmDGYeAZlFZ4qe5KLHOyjFuLKbmNBXLBX4YyPSS"
    @"QjRy6Gyduv2p0mX7X7b300Oc7o4x7xRoehPsXt3F/asOx80G+guF+s4SvdyiPlhi7UBhMp6gOS1g5hbKOizGFdYEkOcLbFRL"
    @"zE6OwGWImiu4VhsqCqHbLfAwAhsOwVoxRNKCzpeQMgRvt8AitapWqHdDIZ76mFRK1avCt3IGk+oY9/MFTkIp51sJO2xfa+Sl"
    @"z9zAaOMGGtbg9T/67Qc2uKnm47vIuhs4SQRUD7jt/gTzl4gzjXGvYhi+zOGKBsO6g9GyDflwA+rQQR6XmGYl6gcL3JxmiIkr"
    @"OJxDjDnEsvK5Ro8PfIFEgzGRjKCJjExjlEkMtLvQgUK8tgYXxwj7AyCKoZJoJZDqALEC+AmW9SneX97EHirMhxt2+Klu/+Hb"
    @"4PLSU9/w7ZRA1tjeOhSH+xdutfv3P/GD0wpLJzwBssU1eu3CR6k0ZlgMLUUT3AxPcFJ1oF/h6E3PY3EaoKz62F8IdGd96Mrg"
    @"8tEO9A8neOWewuGdU0SZglmUqG2NSFOrpEFcLoAx5Z4D8ECgCm7AhRHyNIUY9CG6PaSqBRkPwNI2yuURTuqHOJBLZNsc3Rdi"
    @"Pvps97Z6adPKqkxR1BGSKMM/+Dv/VCQoJsne79YAAA4gSURBVKUY4D0d4s7NA1xtG+xcCn3+42fJT5lV8oxriULMfWvj4dqb"
    @"EN0UJ3XoQ/QdqrNYjKMxx4VXB3h73+DJwx1MD0+QnND42h7Coo3FvTFELqBnNSJe+pAe1gHs8hRunKC8dxdNK0HGu4iSLlwn"
    @"wv3pIW5kJ8g2gN7nWtj60pWpuzR6f30eO5nGOX7hqTfwzPkb6PQO7k6qtb270QDGMLfkASSvWCKc1/iHy31Ys3hh/JEATc6c"
    @"YycufPhdD7hHl1USYG6Bu1cV9ps9bKoNVEcVYnsee3cbsH2F+l6D1nULuVd4/NScVEgLgeowR1coLJdThLCo5wvMDwweYopp"
    @"D0heFlj/xQTps7Kz2NCviR84yN/90u8hDkuUVYwyT8N2qneqQCOuDKLQIE2YL+N+4tTER5YPrATosEIJNC8UMOfnRmmuWosS"
    @"LGTY4zchL0aY6COoJxKYqUHaiyHuOYxmEXDEkR5HyI4N9JHG4rsamEuEbzPYUYE5VeILYPQ0x+izIdiLCtNBPtbs8PJi0zyQ"
    @"NO2xLJOVKSG4YrnayHmFsF5iljdOtwUjLFWbnyXSYwp0j77/aB+k8eUnQipMWePziq6nYIQ4mwzuqsQyA9TLDO2lAKsVbMHQ"
    @"DRPM386QHFpkH5T+pIrOFdrrEumTEaZDgTv5RBpdfrPp3qDxCuq7EgYSxBfLOWtYjxVo1zmj4fOHy1UN+R+6Hr/Eo/qNpg9W"
    @"F2dwU+2nFuvcYRI5LEML1bKoeQn3FRqaqH2ZU49D2NMUY7NElSmc5A3GerFM5OQiA27IFg0uMKJlM0QZsoLbNECNFqNJemAY"
    @"cz/88PMs9hEB/iqLdv6Rb9LYEI3UZAJoXI5gWkGnDNIKmHWFQjaYPaxxqjQyo8BDc1CzW/esm0MeqimWvPaweMNFtgprKWlo"
    @"3K0sZmkMqN9aWbdC5f83S9KYjbBY1ApJYFA1fIW63c8noB+zkDFqFaGwLVTVqR/DofBTjysslhaLQkBTwUqzE9wZJ7KIOVFL"
    @"EsaXfQ6Yj5qBEIwrSEytsxV1yQLGbheWxQKI+YpnNh95QD93LR3ujlvYX6RIVINAWlzqL/2IZ1fRmAvDo6L6ZwpE3fcic+M6"
    @"dJKF6PEe12KJOisxn9auziSryhBCBavWf1ncYWZcg4R+dFCF2nyFNEPVhENhIqTI7cIZ9q0l+BuNwVABn2pxvNzmPxHytOX4"
    @"y+tr+FfvnEfeBJhrYHPrGJu9BM+sz9ENNJ5pNUiFw0cPmXn53Gr69/HXoqZk+3t7JrORe3Kb63YScPDa6cbyqpRM0UijTxma"
    @"O0dzAjmxon7ieLUocTbYkELMKydHhWF26Zj8oDCsyp2Hz28sHf6RAK7GHI9MXjCH9ydtfO2Hl/DuPIKJM/DBBIe2xjsZ8Ed3"
    @"U5yPNa6lGl8alnil06xg0IeADmfHzh5THWNoE8WVWX7jeK7bkcKF9ciocDVgCUu0knPO1jTWT7TudSrRnaO54TPCxtZ0RsdN"
    @"lELkRNOMjbEHHJx6QXQ/wmy3c4f/c+pwNcaHvjGtJP7VB1vu/Uwxd/4eWGvpH44L62cJSJ/3coETzf3xg0uJRkd81O5+/N+P"
    @"xBt2wZoTiNw6V2rnWpxqEMUEY8x6ToKz1b46sjy9srRqCDu7Bp0FdKRBOmNrwavqxBpbPB6vz2iAO6VDZj/cYffN25v47p0N"
    @"prszoLVcESs0WeRWqMZ/lM4wGOD7C4W3lwoMP37S7KN5+xEjSN2bqAXwELzxSDDg3IXMUrhizp+TOnvrex8+pm6+CGt2wcwA"
    @"TofXam0HhdN6aVZJ/6N3ItOrqPDljpq69sEyrRbLCCKsz46mnJGSH7MkdzisfvKopl2Nm//Ya3zFabLMginpmBCCOyZpEp8R"
    @"ZD/bLE8+P/6cnNoJLlDgbpQye042Nr3TOLWYr6Ylf2LRUCDdTDvmL97rZSZJa2fzCEyvxqHBPj6U0XYOlf2xKEk3sZ4j//HP"
    @"UO6j+XDCq5W2jOZYnZXM+HxC5ubOmv/eJW/8SCDPFwVw7HIGtjsTYr2e6VZ9Un2MNAw4MQ4nZ8VdO9D8Un8WbPRnzi26EJOh"
    @"Z0fdxwrEcC01uJyYj9jbGS/+keXnxv2wIDzRz0TInJHMrf6ceTbMmVD6MYE4dHSKanAYNym/UiPcWdSpXlbiJyVaMUs4PhOo"
    @"sRxPtnP51WfvswtpjXDaAzteh1umngp2vvvAvBY+3a3xO1u5n+F+/MLGCQhq15/9mwaCxVkQohHN0jAERBfTRCOnBoFZ3Zxo"
    @"15UP7QM4fnQ9WQzegVUZuFNPNI5/mWnZymemB6fkY4J/uOhEZPCIdQIwCDV+dfeUnUtLvPdwhENrca+SOLUhFqLBy8MCn+zW"
    @"eKXd+AT7uDBkZuKMjHwUSY9q58ecaTx06YcvPKHsSUsKZAbUFVee+jozBArkyw8FcrIiOohZZp+LJd/OCuNqw0VdC/UTPkQT"
    @"hny1g/yRR1JTSlo8O8zwic0ZjpYxYtXgvUmKzbRCS1ofDOKz2Z3HjdF3K9jKp97LHb41szhtHNYDhhfbHPN6lf9IQ3Rak2bH"
    @"uaSoQMK4sy3Fg8eDglxJ6QLu5KW8Ud13T0Pz+iQKi7JmPM2I//6RvzpgLQC2gtUI8qPXS7OKObWWGMa1H2n57MYcSy38DUgv"
    @"P63koU3Zrxy+dmTwxtJipoGuBN7JLEYV82Rs20q0Z5LOtDq5mliifhJjKzHIjE5/5EOrVXGwoDE2e+2gJb55cxAVR1tC1cmP"
    @"K4gxbEmJkP34Az7aHj+TbVfykzD+bo79VPjBzioG0s53F84LQz5E328WwNsZmZ/AjomQVByiVIQUfUvmkTQAHhJg/lAg72GM"
    @"xZbZk2mJ5vuHCR4uVBDnA4jxNmNV7FsH9PlER7gWBOgEHxvRf+7lzjbhTml9EfsoDInVaVNoLvB0kmKLmgSrc3xOUzvHEuX6"
    @"4RNkZ8dqVyZ3trMVzcl3QqN+YWtO0Nm+edSGa86zgHqwydS/ue9SbG1PXcArZsz/M9zz+KIHL3zUXLVmH1XKPh4zh6YJ3cUo"
    @"QY9LlllHR3aYKYXzAPFHO7r/+DXl2Xc6LHWuFdj0b1w7cdcGedZYxm6dJml+uIsoWmOV5jhyEje6D9h2rHGxv/S75n3nLDHa"
    @"n1IhfNzyzXPDcVgKHJbWY6nHlzESbdtmikkXCjJD5upaMNP8xBZOPk4gQttvMeBWJO2lV7fnNhA2ePekZe7OY3FvrlByicM8"
    @"wOv3N7CsFK6tTzFKK3SjBv2kQqpWxwZo+dMpP1MihmkZ4t1JjIOy+TBDPlIRtwLL003XnJ+zQjd+Ql8ZOsZimPvxmuzoYzUE"
    @"4OsO+O+NwxcYY7/44np24clB6SPYuFDkV3ZSBmxRcbdsYnz7VoufFApJWOF8L8NT61O8sD22AbesHTUotWCc/XSt+bM+NNXL"
    @"tK8NxUeKBWYChDZih8sGT/dr/1JEg+981Th8bJ38NIHoF/8LgO9oy77HGH4lkebpNDDtUdR0nx44py1TjeMoao77ixAfTGL3"
    @"1lHKv3lrk72xN8TXb27yF7am5lxvyZ/ZmLlYaaa4QW1WXXP3mLXQz52g8YPnhKmo0uKPW1ORwhZ0jmbh9UFoO1SMxQFHXtpH"
    @"vkshe/FxAuGsLqJo8QMA7zuHP7VgT8HiWQY8pS17VgnXU84maWJEL9LhK9sLfmsrcm8dt8Wf3+/i/rjrfrg/ELvdHDv9BX7t"
    @"yX2c7y2QKkf/LYhv8RjHzvAL85q/PqGzqUsqJx+TliakQ8xL5QrjD5AwJQi++R9h8WFApE72wU8T6PFFb3zTURXr8AcOuArg"
    @"cmXYLzDgXGPZOQ58oqnE4FKvQi8y7sWNhXvzqGVuT0N7d5bI42nP/vPvdPiTazOc62ZYSws3apfoR43tBLZcaoY7k/6yriMm"
    @"+MnQuB+5EMlrirbnL04ytZoCcIwwamlWZ25C30RaKcD8VQT6UPFn398GcJ0B/xbAJRLKAX8fwK8vG45Ymn47NM1GOglOc4HS"
    @"CndvGvHjXJnr05R/47jDFrViUdBQKSSe355hGNrTaZWcbgUFWzeJuO+qgZ/ZcMzxomPtcuB6oeWBMJySNZ3qIu7El5HOUweE"
    @"YuY/LWz/VVZ99h5Chx9gdbFvMGBoHNswGk8G3K33Y/N8IJqHO63qXKk5e7WWuD2N6u8+7MiHi5Aby83bD4fRouYbHeXaJY+L"
    @"za6uTnTcFCKX0irXHJ1nPUTuqVFmXlgveCys046VhMBDxbO8pnMz6J75j3r82dm/vb37c8j0E6tzJmiLiBoHXOHAExYYBMI9"
    @"Yyw+myibFlrslJrNj3PVmZXUwkZ9nCl+axpL8qPDkhs6d7BXWsWq2NF/5NCPG/2fvHTgXlpf0mE201i2xxmGy9LMTxeNKGtL"
    @"5c63APzGWVD7uTX0cWt+9lqJVW544FZak7VhOwy4sqjFLyjunlEcL1wbFmHRVFc5c9w4Ll9t5j4wnJZKjHMlFpXEUWEZZxk2"
    @"W5V8dpQZCu/a8NIBpXOYxIHotBMclU39Jpz75uPC/L+hoZ+1HkHkiwA2LfCi4u5559jLnLmudZhK7lqN4WtS2KDSQuYNbzWW"
    @"1W2lk0Q51zhGjA6huiUcplT3LGt87WhSfV9r8xqAtx5/hv9QDf2s9SgC3QFwnwPfMpbtEHq3jl0mDTc0UMHcOWNYN5Imkdxt"
    @"C+b6DO5y4/iVs03ZYXATMPxQcHadwf4L69x9B3byUS7i/2uBHl+PhNs7e8j3fDT2p9M8FohqwwrOMNKEsx27BmCNouqZj9Ln"
    @"9q11bzPnDh/nEf7/Eujx9Ui4R99pm32KcA4nZ+whOTzN3tCD988qmQ+h18deFcD/Bc1bkxgTs5gPAAAAAElFTkSuQmCC";

static UIImage *ZeyWinAdsMoneyImage(void) {
    static UIImage *image = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        NSData *data = [[NSData alloc] initWithBase64EncodedString:kZeyWinAdsMoneyImageBase64 options:0];
        if (data) {
            image = [UIImage imageWithData:data];
        }
    });
    return image;
}

@interface ZeyWinAdsMoneyIconView : UIView
@property (nonatomic, readonly) CGFloat aspectRatio;
@end

@implementation ZeyWinAdsMoneyIconView {
    UIImage *_moneyImage;
}

- (instancetype)initWithFrame:(CGRect)frame {
    self = [super initWithFrame:frame];
    if (self) {
        self.backgroundColor = [UIColor clearColor];
        self.contentMode = UIViewContentModeRedraw;
        _moneyImage = ZeyWinAdsMoneyImage();
        _aspectRatio = (_moneyImage && _moneyImage.size.height > 0) ? (_moneyImage.size.width / _moneyImage.size.height) : 0.0;
    }
    return self;
}

- (void)drawBillInContext:(CGContextRef)ctx x:(CGFloat)x y:(CGFloat)y width:(CGFloat)w height:(CGFloat)h color:(UIColor *)color slantDegrees:(CGFloat)slant {
    CGContextSaveGState(ctx);
    CGContextConcatCTM(ctx, CGAffineTransformMake(1, 0, slant / 100.0, 1, 0, 0));

    [[UIColor colorWithRed:0.067 green:0.522 blue:0.188 alpha:1.0] setFill];
    [[UIBezierPath bezierPathWithRoundedRect:CGRectMake(x, y, w, h) cornerRadius:4.0] fill];

    [color setFill];
    [[UIBezierPath bezierPathWithRoundedRect:CGRectMake(x + 3, y + 3, w - 6, h - 6) cornerRadius:3.0] fill];

    [[UIColor colorWithRed:0.078 green:0.627 blue:0.176 alpha:1.0] setFill];
    CGContextFillEllipseInRect(ctx, CGRectMake(x + w * 0.5 - 7, y + h * 0.5 - 7, 14, 14));

    CGContextRestoreGState(ctx);
}

- (void)drawBandInContext:(CGContextRef)ctx x:(CGFloat)x y:(CGFloat)y width:(CGFloat)w height:(CGFloat)h {
    [[UIColor colorWithRed:1.0 green:0.357 blue:0.424 alpha:1.0] setFill];
    [[UIBezierPath bezierPathWithRoundedRect:CGRectMake(x, y, w, h) cornerRadius:2.0] fill];

    [[UIColor colorWithRed:0.961 green:0.933 blue:0.792 alpha:1.0] setFill];
    [[UIBezierPath bezierPathWithRoundedRect:CGRectMake(x, y + 2, w, h - 4) cornerRadius:2.0] fill];
}

- (void)drawRect:(CGRect)rect {
    if (_moneyImage) {
        [_moneyImage drawInRect:self.bounds];
        return;
    }

    CGContextRef ctx = UIGraphicsGetCurrentContext();
    if (!ctx) {
        return;
    }

    CGContextSaveGState(ctx);
    CGContextTranslateCTM(ctx, CGRectGetMidX(self.bounds), CGRectGetMidY(self.bounds));

    [self drawBillInContext:ctx x:-28 y:-20 width:56 height:24 color:[UIColor colorWithRed:0.376 green:0.933 blue:0.412 alpha:1.0] slantDegrees:-6];
    [self drawBillInContext:ctx x:-30 y:-6  width:60 height:25 color:[UIColor colorWithRed:0.306 green:0.878 blue:0.365 alpha:1.0] slantDegrees:3];
    [self drawBillInContext:ctx x:-27 y:8   width:54 height:24 color:[UIColor colorWithRed:0.345 green:0.925 blue:0.376 alpha:1.0] slantDegrees:-4];
    [self drawBandInContext:ctx x:-12 y:-8 width:24 height:34];

    CGContextRestoreGState(ctx);
}

@end

#pragma mark - Loading overlay view

@interface ZeyWinAdsLoadingOverlayView : UIView
@property (nonatomic, strong) UIView *barBase;
@property (nonatomic, strong) UIView *barTrack;
@property (nonatomic, strong) UIView *barFill;
@property (nonatomic, strong) ZeyWinAdsMoneyIconView *icon;
@property (nonatomic, strong) UILabel *label;
@property (nonatomic, strong) CADisplayLink *displayLink;
@property (nonatomic, assign) CFTimeInterval startTime;
@property (nonatomic, assign) CGFloat progress;
@end

@implementation ZeyWinAdsLoadingOverlayView

- (instancetype)initWithFrame:(CGRect)frame {
    self = [super initWithFrame:frame];
    if (self) {
        self.backgroundColor = [UIColor colorWithRed:15.0/255.0 green:33.0/255.0 blue:158.0/255.0 alpha:1.0];
        self.userInteractionEnabled = YES;
        self.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;

        _barBase = [[UIView alloc] init];
        _barBase.backgroundColor = [UIColor colorWithRed:238.0/255.0 green:247.0/255.0 blue:255.0/255.0 alpha:1.0];
        _barBase.clipsToBounds = YES;
        [self addSubview:_barBase];

        _barTrack = [[UIView alloc] init];
        _barTrack.backgroundColor = [UIColor colorWithRed:28.0/255.0 green:42.0/255.0 blue:105.0/255.0 alpha:1.0];
        _barTrack.clipsToBounds = YES;
        [_barBase addSubview:_barTrack];

        _barFill = [[UIView alloc] init];
        _barFill.backgroundColor = [UIColor colorWithRed:255.0/255.0 green:188.0/255.0 blue:41.0/255.0 alpha:1.0];
        [_barTrack addSubview:_barFill];

        _icon = [[ZeyWinAdsMoneyIconView alloc] init];
        [self addSubview:_icon];

        _label = [[UILabel alloc] init];
        _label.textColor = [UIColor whiteColor];
        _label.textAlignment = NSTextAlignmentCenter;
        _label.font = [UIFont boldSystemFontOfSize:24.0];
        _label.text = @"Loading 0%";
        [self addSubview:_label];
    }
    return self;
}

- (BOOL)isLandscapeLayout {
    return self.bounds.size.width > self.bounds.size.height;
}

- (void)layoutSubviews {
    [super layoutSubviews];

    BOOL landscape = [self isLandscapeLayout];
    CGFloat width = self.bounds.size.width;

    // Mirrors ZeyWinAdsLoadingOverlay.java's resolveProgressHeight/resolveProgressBottomMargin:
    // the whole bar+icon+label group lives inside a bottom-anchored container, not a
    // top-anchored one, so it sits over the same on-screen real estate as Android.
    CGFloat containerHeight = landscape ? 108.0 : 240.0;
    CGFloat baseBottomMargin = landscape ? 16.0 : 58.0;
    CGFloat containerBottomMargin = baseBottomMargin + MAX(0.0, self.safeAreaInsets.bottom);
    CGFloat containerTop = self.bounds.size.height - containerBottomMargin - containerHeight;

    CGFloat barWidth = MAX(96.0, width * (landscape ? 0.50 : 0.70));
    CGFloat barLeft = MAX(16.0, (width - barWidth) * 0.5);
    CGFloat barRight = MIN(width - 16.0, barLeft + barWidth);
    CGFloat barTop = containerTop + (landscape ? 14.0 : 96.0);
    CGFloat barBottom = containerTop + (landscape ? 44.0 : 126.0);

    CGRect barFrame = CGRectMake(barLeft, barTop, barRight - barLeft, barBottom - barTop);
    self.barBase.frame = barFrame;
    self.barBase.layer.cornerRadius = barFrame.size.height * 0.5;

    CGRect trackFrame = CGRectInset(self.barBase.bounds, 4.0, 4.0);
    self.barTrack.frame = trackFrame;
    self.barTrack.layer.cornerRadius = trackFrame.size.height * 0.5;

    CGFloat radius = trackFrame.size.height * 0.5;
    CGFloat fillWidth = MAX(radius * 2.0, trackFrame.size.width * self.progress);
    self.barFill.frame = CGRectMake(0, 0, fillWidth, trackFrame.size.height);
    self.barFill.layer.cornerRadius = radius;

    CGFloat iconHeight = 58.0;
    CGFloat iconAspect = self.icon.aspectRatio > 0.0 ? self.icon.aspectRatio : 1.0;
    CGFloat iconWidth = iconHeight * iconAspect;
    CGFloat moneyX = barFrame.origin.x + radius + (barFrame.size.width - radius * 2.0) * self.progress;
    CGFloat minX = iconWidth * 0.5 + 22.0;
    CGFloat maxX = MAX(minX, width - iconWidth * 0.5 - 22.0);
    moneyX = MAX(minX, MIN(maxX, moneyX));
    CGFloat iconCenterY = barTop + 16.0;
    self.icon.frame = CGRectMake(moneyX - iconWidth * 0.5, iconCenterY - iconHeight * 0.5, iconWidth, iconHeight);

    self.label.frame = CGRectMake(0, barBottom + (landscape ? 12.0 : 20.0), width, 30.0);
}

- (void)applyProgress:(CGFloat)progress {
    _progress = progress;
    self.label.text = [NSString stringWithFormat:@"Loading %ld%%", (long)lround(progress * 100.0)];
    [self setNeedsLayout];
    [self layoutIfNeeded];
    [self.icon setNeedsDisplay];
}

- (void)tick {
    CFTimeInterval elapsed = CACurrentMediaTime() - self.startTime;
    CGFloat t = (CGFloat)MIN(1.0, elapsed / kZeyWinAdsProgressDuration);
    [self applyProgress:ZeyWinAdsEvaluateSteppedProgress(t)];
    if (t >= 1.0) {
        [self stopAnimating];
    }
}

- (void)startAnimating {
    [self stopAnimating];
    self.startTime = CACurrentMediaTime();
    [self applyProgress:0.0];
    self.displayLink = [CADisplayLink displayLinkWithTarget:self selector:@selector(tick)];
    [self.displayLink addToRunLoop:[NSRunLoop mainRunLoop] forMode:NSRunLoopCommonModes];
}

- (void)stopAnimating {
    [self.displayLink invalidate];
    self.displayLink = nil;
}

@end

#pragma mark - Bridge (mirrors ZeyWinAdsStartupOverlay.java)

static ZeyWinAdsLoadingOverlayView *_zeyWinAdsOverlayView = nil;
static NSTimer *_zeyWinAdsAutoDismissTimer = nil;

static UIWindow *ZeyWinAdsStartupOverlayKeyWindow(void) {
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
    return keyWindow;
}

// Mirrors ZeyWinAdsStartupOverlay.java's `dismissed` flag: once explicitly
// hidden, automatic re-install attempts (app resume, scene reactivation)
// no longer bring the overlay back. An explicit Show() always can.
static BOOL _zeyWinAdsOverlayDismissed = NO;

// Mirrors ZeyWinAdsStartupOverlay.java's `autoDismissScheduled` flag: the
// auto-dismiss timer is scheduled once per "show" and is NOT reset by every
// app resume (e.g. after an ATT/UMP/push-permission dialog is dismissed,
// which fires UIApplicationDidBecomeActiveNotification just like a real
// resume). Only an explicit Show() or the timer itself firing clears it.
static BOOL _zeyWinAdsAutoDismissScheduled = NO;

static void ZeyWinAdsStartupOverlayHide(void);

static void ZeyWinAdsStartupOverlayScheduleAutoDismiss(void) {
    if (_zeyWinAdsAutoDismissScheduled) {
        return;
    }
    _zeyWinAdsAutoDismissScheduled = YES;

    [_zeyWinAdsAutoDismissTimer invalidate];
    _zeyWinAdsAutoDismissTimer = [NSTimer scheduledTimerWithTimeInterval:kZeyWinAdsAutoDismissDelay
                                                                   repeats:NO
                                                                     block:^(NSTimer * _Nonnull timer) {
        _zeyWinAdsAutoDismissScheduled = NO;
        ZeyWinAdsStartupOverlayHide();
    }];
}

static void ZeyWinAdsStartupOverlayAttach(void) {
    UIWindow *window = ZeyWinAdsStartupOverlayKeyWindow();
    if (!window) {
        return;
    }

    if (!_zeyWinAdsOverlayView) {
        _zeyWinAdsOverlayView = [[ZeyWinAdsLoadingOverlayView alloc] initWithFrame:window.bounds];
    }

    // Only a genuine fresh attach (first show, or re-parented to a new
    // window) restarts the progress animation — matches Android's
    // onAttachedToWindow-gated restart. A mere resume (e.g. after a native
    // permission dialog) must not visibly reset progress back to 0%.
    if (_zeyWinAdsOverlayView.superview != window) {
        [_zeyWinAdsOverlayView removeFromSuperview];
        _zeyWinAdsOverlayView.frame = window.bounds;
        [window addSubview:_zeyWinAdsOverlayView];
        [_zeyWinAdsOverlayView startAnimating];
    }

    [window bringSubviewToFront:_zeyWinAdsOverlayView];

    ZeyWinAdsStartupOverlayScheduleAutoDismiss();
}

static void ZeyWinAdsStartupOverlayHide(void) {
    _zeyWinAdsOverlayDismissed = YES;
    _zeyWinAdsAutoDismissScheduled = NO;

    [_zeyWinAdsAutoDismissTimer invalidate];
    _zeyWinAdsAutoDismissTimer = nil;

    [_zeyWinAdsOverlayView stopAnimating];
    [_zeyWinAdsOverlayView removeFromSuperview];
}

static void ZeyWinAdsStartupOverlayShow(void) {
    _zeyWinAdsOverlayDismissed = NO;
    // Force a fresh 15s dismiss window on an explicit Show(), matching
    // Android's show(), even if already attached (in which case the
    // animation itself is correctly left alone by the guard above).
    _zeyWinAdsAutoDismissScheduled = NO;
    ZeyWinAdsStartupOverlayAttach();
}

// Auto-install entry point: mirrors ZeyWinAdsStartupProvider.java's
// onActivityCreated/Started/Resumed calling installFor(activity) — attaches
// the overlay at native launch/resume without any Unity C# involvement, but
// stays out of the way once the overlay has been explicitly dismissed.
static void ZeyWinAdsStartupOverlayAutoInstall(void) {
    if (_zeyWinAdsOverlayDismissed) {
        return;
    }
    ZeyWinAdsStartupOverlayAttach();
}

extern "C" {
    void _ZeyWinAdsStartupOverlay_SetVisible(BOOL visible) {
        dispatch_async(dispatch_get_main_queue(), ^{
            if (visible) {
                ZeyWinAdsStartupOverlayShow();
            } else {
                ZeyWinAdsStartupOverlayHide();
            }
        });
    }
}

#pragma mark - Auto-install hook (mirrors ZeyWinAdsStartupProvider ContentProvider)

// Unity regenerates the Xcode project from scratch on every export, so this
// can't rely on a manual AppDelegate edit surviving a rebuild. +load runs as
// soon as this plugin's object file is loaded into the process — well before
// UnityFramework hands control to any Unity C# code — matching the timing
// guarantee the Android ContentProvider gives us.
@interface ZeyWinAdsStartupOverlayAutoInstaller : NSObject
@end

@implementation ZeyWinAdsStartupOverlayAutoInstaller

+ (void)load {
    [[NSNotificationCenter defaultCenter] addObserver:self
                                              selector:@selector(handleLaunchOrResume:)
                                                  name:UIApplicationDidFinishLaunchingNotification
                                                object:nil];
    [[NSNotificationCenter defaultCenter] addObserver:self
                                              selector:@selector(handleLaunchOrResume:)
                                                  name:UIApplicationDidBecomeActiveNotification
                                                object:nil];
}

+ (void)handleLaunchOrResume:(NSNotification *)notification {
    dispatch_async(dispatch_get_main_queue(), ^{
        ZeyWinAdsStartupOverlayAutoInstall();
    });
}

@end
