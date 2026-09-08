namespace AssetLibrary.CoreServer.Hosting.Trial.Preview;

internal static class TrialImageSession
{
    public static async ValueTask<bool> RevalidateAsync(HttpContext context, TrialAuthenticatedRequest authenticated)
    {
        var sessions = context.RequestServices.GetRequiredService<TrialAuthenticationServices>().BrowserSessions;
        var current = await sessions.AuthenticateForMutationAsync(authenticated.Ticket.SessionToken,
            authenticated.Ticket.CsrfToken, context.RequestAborted).ConfigureAwait(false);
        return current.Identity?.Subject == authenticated.Identity.Subject;
    }
}
