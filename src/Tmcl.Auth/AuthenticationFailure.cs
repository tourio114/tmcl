using System.Net;
using CmlLib.Core.Auth.Microsoft;
using Microsoft.Identity.Client;
namespace Tmcl.Auth;

public enum AuthenticationFailure { Configuration, InteractionRequired, Cancelled, Network, Timeout, ApplicationPermission, XboxProfile, FamilyRestriction, RegionRestriction, GameProfile, Unknown }

public sealed class AuthenticationException(AuthenticationFailure failure, string message) : Exception(message)
{
    public AuthenticationFailure Failure { get; } = failure;
}

public static class AuthenticationErrors
{
    // Only return fixed, actionable messages; OAuth exceptions can contain sensitive URLs or tokens.
    public static AuthenticationException Translate(Exception error)
    {
        if (error is AuthenticationException known) return known;
        var errors = new List<Exception>();
        for (Exception? current = error; current != null && errors.Count < 16; current = current.InnerException) errors.Add(current);
        var text = string.Join(" ", errors.Select(e => e.Message));
        var msal = errors.OfType<MsalException>().FirstOrDefault();
        if (errors.Any(e => e is OperationCanceledException) || msal?.ErrorCode is "authentication_canceled" or "access_denied")
            return new(AuthenticationFailure.Cancelled, "登录已取消。可以重新登录，或继续使用离线账号。");
        if (errors.Any(e => e is MsalUiRequiredException)) return InteractionRequired();
        if (msal?.ErrorCode is "invalid_client" or "unauthorized_client" || Contains(text, "AADSTS700016", "AADSTS50011", "AADSTS9002326"))
            return new(AuthenticationFailure.Configuration, "微软应用配置不正确。请确认 Client ID、个人 Microsoft 账号类型，以及桌面应用回调 http://localhost。");
        if (Contains(text, "Invalid app registration", "AppRegInfo", "AzureAppNotPermitted"))
            return new(AuthenticationFailure.ApplicationPermission, "Minecraft 服务拒绝了此应用。请检查该 Client ID 是否获准访问 Minecraft API；注册 Azure 应用并不等于获得此权限。");
        if (Contains(text, "2148916233")) return new(AuthenticationFailure.XboxProfile, "该微软账号尚未建立 Xbox 档案。请先在 xbox.com 登录并完成 Xbox 账号设置，再重试。");
        if (Contains(text, "2148916238")) return new(AuthenticationFailure.FamilyRestriction, "Xbox 家庭或儿童账号设置阻止了登录。请由家庭组织者检查账号权限。");
        if (Contains(text, "2148916235", "2148916236", "2148916237")) return new(AuthenticationFailure.RegionRestriction, "Xbox 服务的地区或年龄验证限制阻止了登录。请在微软账号中检查地区和年龄验证状态。");
        if (Contains(text, "doesn't own", "does not own", "no profile") || errors.Any(e => e is JEAuthException) && Contains(text, "NOT_FOUND"))
            return new(AuthenticationFailure.GameProfile, "未找到可用的 Minecraft Java 版档案。请确认登录的是拥有 Java 版的账号，并已建立游戏档案。");
        if (errors.OfType<HttpRequestException>().Any(e => e.StatusCode == HttpStatusCode.Forbidden) || errors.Any(e => e is JEAuthException) && Contains(text, "403", "FORBIDDEN"))
            return new(AuthenticationFailure.ApplicationPermission, "Minecraft/Xbox 服务拒绝了请求。请检查应用的 API 访问权限、账号的 Java 版资格和 Xbox 设置。");
        if (errors.Any(e => e is TimeoutException)) return new(AuthenticationFailure.Timeout, "登录等待超时，请重试。浏览器无法回到启动器时，可改用设备代码登录。");
        if (errors.Any(e => e is HttpRequestException) || msal?.ErrorCode is "service_not_available" or "request_timeout" or "http_request_failed")
            return new(AuthenticationFailure.Network, "无法连接微软、Xbox 或 Minecraft 登录服务。请检查网络后重试；游戏下载镜像不会代理账号授权。");
        return new(AuthenticationFailure.Unknown, "微软登录未完成。请检查应用配置和账号状态后重试；浏览器方式不可用时可尝试设备代码登录。");
    }
    public static AuthenticationException InteractionRequired() => new(AuthenticationFailure.InteractionRequired, "没有可恢复的登录授权，或授权需要重新确认。请点击“登录微软账号”完成授权。");
    private static bool Contains(string text, params string[] values) => values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}
