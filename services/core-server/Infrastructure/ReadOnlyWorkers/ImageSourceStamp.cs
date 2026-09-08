namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

internal readonly record struct ImageSourceStamp(
    ulong Device, ulong FileId, long Length, DateTimeOffset ModifiedAt,
    long ChangeSeconds, uint ChangeNanoseconds, uint ModifiedNanoseconds, bool IsDirectory)
{
    public bool SameIdentity(ImageSourceStamp other) => Device == other.Device && FileId == other.FileId;
}
