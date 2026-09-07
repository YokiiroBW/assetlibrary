using System.Security.Claims;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.CoreServer.Hosting;

internal sealed record TrialAuthenticatedRequest(AuthenticatedIdentity Identity, TrialBrowserTicket Ticket)
{
    private static readonly object ContextKey = new();

    public static TrialAuthenticatedRequest? Get(HttpContext context) =>
        context.Items.TryGetValue(ContextKey, out var value) ? value as TrialAuthenticatedRequest : null;

    public void Attach(HttpContext context)
    {
        context.Items[ContextKey] = this;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Identity.Subject.Value)], "AssetLibrary.LocalSession"));
    }

    public static void Clear(HttpContext context)
    {
        context.Items.Remove(ContextKey);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
    }
}
