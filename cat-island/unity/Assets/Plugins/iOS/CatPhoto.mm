// 사진 고르기 (PHPicker): 고른 사진을 앱 안 임시 파일로 저장해 경로를 Unity 로 돌려준다.
// 사진은 기기 밖으로 나가지 않는다. 사진 보관함 권한 없이 동작한다 (PHPicker 는 고른 사진만 앱에 준다).
#import <PhotosUI/PhotosUI.h>
#import <UIKit/UIKit.h>

extern UIViewController *UnityGetGLViewController(void);
extern void UnitySendMessage(const char *obj, const char *method, const char *msg);

@interface CatPhotoDelegate : NSObject <PHPickerViewControllerDelegate>
@end

static CatPhotoDelegate *catPhotoDelegate;
static NSString *catPhotoReceiver;

@implementation CatPhotoDelegate
- (void)picker:(PHPickerViewController *)picker didFinishPicking:(NSArray<PHPickerResult *> *)results {
    [picker dismissViewControllerAnimated:YES completion:nil];
    NSItemProvider *p = results.firstObject.itemProvider;
    if (!p || ![p canLoadObjectOfClass:[UIImage class]]) { UnitySendMessage(catPhotoReceiver.UTF8String, "OnPicked", ""); return; }
    [p loadObjectOfClass:[UIImage class] completionHandler:^(id<NSItemProviderReading> obj, NSError *err) {
        UIImage *img = (UIImage *)obj; NSString *path = @"";
        if (img) {
            // 긴 변 1024 로 줄여 저장 (읽는 데 충분하고 메모리를 아낀다)
            CGFloat s = MIN(1.0, 1024.0 / MAX(img.size.width, img.size.height));
            CGSize sz = CGSizeMake(round(img.size.width * s), round(img.size.height * s));
            UIGraphicsImageRenderer *r = [[UIGraphicsImageRenderer alloc] initWithSize:sz];
            UIImage *small = [r imageWithActions:^(UIGraphicsImageRendererContext *c) { [img drawInRect:CGRectMake(0, 0, sz.width, sz.height)]; }];
            path = [NSTemporaryDirectory() stringByAppendingPathComponent:@"catphoto.jpg"];
            [UIImageJPEGRepresentation(small, .9) writeToFile:path atomically:YES];
        }
        dispatch_async(dispatch_get_main_queue(), ^{ UnitySendMessage(catPhotoReceiver.UTF8String, "OnPicked", path.UTF8String); });
    }];
}
@end

extern "C" void CatPhoto_Pick(const char *receiver) {
    catPhotoReceiver = [NSString stringWithUTF8String:receiver];
    PHPickerConfiguration *cfg = [[PHPickerConfiguration alloc] init];
    cfg.filter = [PHPickerFilter imagesFilter]; cfg.selectionLimit = 1;
    PHPickerViewController *vc = [[PHPickerViewController alloc] initWithConfiguration:cfg];
    if (!catPhotoDelegate) catPhotoDelegate = [CatPhotoDelegate new];
    vc.delegate = catPhotoDelegate;
    [UnityGetGLViewController() presentViewController:vc animated:YES completion:nil];
}
