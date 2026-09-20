namespace Ludots.Platform.Abstractions
{
    public enum StaticMeshAdapterSyncOpKind : byte
    {
        Create = 0,
        Update = 1,
        Remove = 2,
        Resync = 3,
    }
}
