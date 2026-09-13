namespace AssetLibrary.Windows.Client;

public enum DerivedImageProfile { Thumbnail512 = 1, Preview1600 = 2 }

public sealed class DerivedImageSpecification
{
    private DerivedImageSpecification(int edge, int encodedBytes, string variant)
    { MaximumEdge = edge; MaximumEncodedBytes = encodedBytes; HttpVariant = variant; }

    public int MaximumEdge { get; }
    public int MaximumEncodedBytes { get; }
    public int MaximumDecodedBytes => MaximumEdge * MaximumEdge * 4;
    public string HttpVariant { get; }
    private static readonly DerivedImageSpecification Thumbnail = new(512, 2097152, "thumbnail");
    private static readonly DerivedImageSpecification Preview = new(1600, 12582912, "preview");
    public static DerivedImageSpecification For(DerivedImageProfile profile) => profile switch
    {
        DerivedImageProfile.Thumbnail512 => Thumbnail,
        DerivedImageProfile.Preview1600 => Preview,
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };
}
