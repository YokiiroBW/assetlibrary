using System.Security.Cryptography;
using System.Text;

namespace AssetLibrary.CoreServer.Hosting;

internal sealed class TrialRequestTrust
{
    public const string CsrfHeader = "X-AssetLibrary-CSRF";
    private readonly string origin;
    private readonly string authority;

    public TrialRequestTrust(Uri publicOrigin)
    {
        ArgumentNullException.ThrowIfNull(publicOrigin);
        if (!publicOrigin.IsAbsoluteUri || publicOrigin.Scheme != Uri.UriSchemeHttps
            || publicOrigin.AbsolutePath != "/" || publicOrigin.Query.Length != 0
            || publicOrigin.Fragment.Length != 0 || publicOrigin.UserInfo.Length != 0)
        {
            throw new ArgumentException("The trial HTTPS origin is invalid.", nameof(publicOrigin));
        }

        origin = publicOrigin.GetLeftPart(UriPartial.Authority);
        authority = publicOrigin.Authority;
    }

    public bool Allows(HttpRequest request)
    {
        if (!request.IsHttps || !string.Equals(request.Host.Value, authority, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suppliedOrigin = request.Headers.Origin;
        if (HttpMethods.IsPost(request.Method) || suppliedOrigin.Count != 0)
        {
            if (suppliedOrigin.Count != 1 || !string.Equals(suppliedOrigin[0], origin, StringComparison.Ordinal))
            {
                return false;
            }
        }

        var site = request.Headers["Sec-Fetch-Site"];
        return site.Count == 0 || (site.Count == 1 && site[0] is "same-origin" or "none");
    }

    public static bool MatchesCsrf(HttpRequest request, TrialBrowserTicket ticket)
    {
        var header = request.Headers[CsrfHeader];
        if (header.Count != 1 || header[0] is not { Length: 43 } supplied)
        {
            return false;
        }

        var expectedBytes = Encoding.ASCII.GetBytes(ticket.CsrfToken.Export());
        var suppliedBytes = Encoding.ASCII.GetBytes(supplied);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(suppliedBytes);
        }
    }
}
