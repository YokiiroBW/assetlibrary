using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.LibraryStorage.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class GatewayContractTests
{
    [TestMethod]
    public void GeneralRegistrationKeepsTheLegacyFingerprintAndExplicitCategoryChangesIt()
    {
        var request = new LibraryRegistrationRequest("fixture", "Fixture", "C:/sandbox/fixture");
        var legacy = JsonSerializer.Serialize(request, LibraryRegistrationJsonContext.Default.LibraryRegistrationRequest);
        Assert.AreEqual("{\"SourceKey\":\"fixture\",\"DisplayName\":\"Fixture\",\"RootPath\":\"C:/sandbox/fixture\"}", legacy);
        var categorized = JsonSerializer.Serialize(request with { Category = LibraryCategory.Images },
            LibraryRegistrationJsonContext.Default.LibraryRegistrationRequest);
        Assert.AreNotEqual(legacy, categorized);
    }

    [TestMethod]
    public void ReadContractsNormalizeAndBoundUntrustedInput()
    {
        Assert.AreEqual("folder/child", new BrowseParentPath("folder\\child").Value);
        Assert.AreEqual("two words", new AssetSearchText("  two   words  ").Value);
        Assert.AreEqual(100, new ReadPageOptions(100, TimeSpan.FromSeconds(10)).PageSize);

        Assert.ThrowsExactly<ArgumentException>(() => new AuthenticatedSubject(" subject "));
        Assert.ThrowsExactly<ArgumentException>(() => new BrowseParentPath("../escape"));
        Assert.ThrowsExactly<ArgumentException>(() => new BrowseParentPath("C:/absolute"));
        Assert.ThrowsExactly<ArgumentException>(() => new AssetSearchText("x"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new ReadPageOptions(101, TimeSpan.FromSeconds(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new ReadPageOptions(1, TimeSpan.FromSeconds(11)));
        Assert.ThrowsExactly<ArgumentException>(() => new ReadPageCursor("not+padded="));
    }

    [TestMethod]
    public void ProtectedCursorRejectsTamperingAndCrossFilterReuse()
    {
        var codec = new ProtectedReadCursorCodec(new EphemeralDataProtectionProvider());
        var libraryId = Guid.NewGuid();
        var cursor = codec.Encode("browse", $"{libraryId:D}\nfolder", "item", null, Guid.NewGuid());

        var decoded = codec.Decode(cursor, "browse", $"{libraryId:D}\nfolder");

        Assert.AreEqual("item", decoded.SortName);
        Assert.IsNotNull(decoded.EntryId);
        Assert.ThrowsExactly<InvalidReadCursorException>(
            () => codec.Decode(cursor, "search", $"{libraryId:D}\nfolder"));
        Assert.ThrowsExactly<InvalidReadCursorException>(
            () => codec.Decode(cursor, "browse", $"{libraryId:D}\nother"));

        var replacement = cursor.Value[^1] == 'A' ? 'B' : 'A';
        var tampered = new ReadPageCursor(cursor.Value[..^1] + replacement);
        Assert.ThrowsExactly<InvalidReadCursorException>(
            () => codec.Decode(tampered, "browse", $"{libraryId:D}\nfolder"));
    }
}
