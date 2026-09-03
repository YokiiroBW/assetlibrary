using System.Security.Cryptography;
using System.Text;
using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Application;

internal static class OperationConfirmation
{
    public static OperationConfirmationDigest Create(
        OperationPlanRequest request,
        IReadOnlyList<OperationItemPreflight> preflight,
        DateTimeOffset expiresAt)
    {
        var canonical = new StringBuilder(512);
        Append(canonical, request.PlanId.Value.ToString("D"));
        Append(canonical, request.IdempotencyKey.Value);
        Append(canonical, ((int)request.Operation).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(canonical, expiresAt.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var item in request.Items)
        {
            var inspected = preflight.Single(candidate => candidate.ItemId == item.ItemId);
            Append(canonical, item.ItemId.Value.ToString("D"));
            Append(canonical, item.Source.Value);
            Append(canonical, item.Target?.Value ?? string.Empty);
            Append(canonical, item.ExpectedSource.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(canonical, item.ExpectedSource.Sha256.Value);
            Append(canonical, ((int)inspected.Decision).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(canonical, inspected.ObservedSource?.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            Append(canonical, inspected.ObservedSource?.Sha256.Value ?? string.Empty);
            Append(canonical, inspected.RequiredBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(canonical, inspected.AvailableBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var bytes = Encoding.UTF8.GetBytes(canonical.ToString());
        return new OperationConfirmationDigest(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
        builder.Append('|');
    }
}
