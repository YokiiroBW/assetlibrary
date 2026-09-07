using System.Text.Json.Nodes;
using AssetLibrary.AssetLink;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using static AssetLibrary.CoreServer.Hosting.Trial.TrialManagementRequest;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal sealed class TrialLibraryManagementControls(TrialLibraryServices libraries)
{
    public static bool CanHandle(string operation) => operation is "storage_sources.list" or "libraries.register" or "libraries.update_category";

    public async ValueTask<JsonObject> ExecuteAsync(ControlRequestMessage request, Guid principalId, CancellationToken token)
    {
        if (request.Operation == "storage_sources.list")
        {
            return new JsonObject
            {
                ["sources"] = new JsonArray(libraries.Sources.Select(source => (JsonNode)new JsonObject
                {
                    ["source_key"] = source.SourceKey,
                    ["display_name"] = source.DisplayName,
                    ["default_root_path"] = source.AllowedRoot.Value,
                }).ToArray()),
            };
        }

        if (request.Operation == "libraries.register")
        {
            var id = await libraries.Registration.RegisterAsync(
                new LibraryRegistrationRequest(Text(request.Body, "source_key"), Text(request.Body, "display_name"), Text(request.Body, "root_path"),
                    request.Body.ContainsKey("category") ? LibraryCategories.Parse(Text(request.Body, "category")) : LibraryCategory.General),
                Operation(request, principalId), token).ConfigureAwait(false);
            return new JsonObject { ["library_id"] = id.Value.ToString("D") };
        }

        var libraryId = new LibraryId(Identifier(request.Body, "library_id"));
        if (request.Operation == "libraries.update_category")
        {
            var category = await libraries.Categories.UpdateAsync(new LibraryCategoryUpdate(libraryId,
                LibraryCategories.Parse(Text(request.Body, "category")), LibraryCategories.Parse(Text(request.Body, "expected_category"))),
                Operation(request, principalId), token).ConfigureAwait(false);
            return new JsonObject { ["library_id"] = libraryId.Value.ToString("D"), ["category"] = LibraryCategories.ToWire(category) };
        }

        throw new ArgumentException("Unsupported library management operation.");
    }
}
