namespace AssetLibrary.Modules.LibraryStorage.Contracts;

public enum LibraryCategory
{
    General = 0,
    Photos,
    Images,
    Videos,
    Music,
    Projects,
    Documents,
    Characters,
}

public static class LibraryCategories
{
    public static LibraryCategory Parse(string value) => value switch
    {
        "general" => LibraryCategory.General,
        "photos" => LibraryCategory.Photos,
        "images" => LibraryCategory.Images,
        "videos" => LibraryCategory.Videos,
        "music" => LibraryCategory.Music,
        "projects" => LibraryCategory.Projects,
        "documents" => LibraryCategory.Documents,
        "characters" => LibraryCategory.Characters,
        _ => throw new ArgumentException("The library category is invalid.", nameof(value)),
    };

    public static string ToWire(LibraryCategory value) => value switch
    {
        LibraryCategory.General => "general",
        LibraryCategory.Photos => "photos",
        LibraryCategory.Images => "images",
        LibraryCategory.Videos => "videos",
        LibraryCategory.Music => "music",
        LibraryCategory.Projects => "projects",
        LibraryCategory.Documents => "documents",
        LibraryCategory.Characters => "characters",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}

public sealed record LibraryCategoryUpdate(LibraryId LibraryId, LibraryCategory Category, LibraryCategory ExpectedCategory);

public interface ILibraryCategoryManagement
{
    ValueTask<LibraryCategory> UpdateAsync(
        LibraryCategoryUpdate request, ManagementOperation operation, CancellationToken cancellationToken);
}
