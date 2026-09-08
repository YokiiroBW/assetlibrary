using AssetLibrary.Modules.PreviewProvider.Contracts;

namespace AssetLibrary.CoreServer.Hosting.Trial.Preview;

internal static class TrialImageResponses
{
    public static IResult Unauthenticated(HttpContext context) =>
        TrialAuthenticationResponses.Error(401, "unauthenticated", "请登录后继续。", context);

    public static IResult Failure(HttpContext context, ImagePreviewFailure failure)
    {
        var (status, code, message) = failure switch
        {
            ImagePreviewFailure.NotFound => (404, "not_found", "条目不存在或不可访问。"),
            ImagePreviewFailure.SourceChanged => (409, "source_changed", "源文件已变化，请刷新文件信息。"),
            ImagePreviewFailure.Unsupported => (415, "preview_unsupported", "暂不支持此内容的图片预览。"),
            ImagePreviewFailure.Invalid => (422, "preview_invalid", "图片内容损坏，无法生成预览。"),
            ImagePreviewFailure.LimitExceeded => (422, "preview_limit_exceeded", "图片超出预览限制。"),
            ImagePreviewFailure.Busy => (429, "preview_busy", "图片预览繁忙，请稍后重试。"),
            ImagePreviewFailure.Timeout => (504, "preview_timeout", "图片预览超时。"),
            _ => (503, "preview_unavailable", "图片预览暂不可用。"),
        };
        if (failure == ImagePreviewFailure.Busy) context.Response.Headers.RetryAfter = "1";
        return TrialAuthenticationResponses.Error(status, code, message, context);
    }
}
