using System;
using Ludots.Core.Registry;

namespace Ludots.Core.MassCrowd.Runtime;

public static class MassCrowdProfileRegistry
{
    private static StringIntRegistry _ids = CreateRegistry();

    public static int InvalidId => 0;

    public static int Register(string profileId)
    {
        return _ids.Register(profileId);
    }

    public static int GetId(string profileId)
    {
        return _ids.GetId(profileId);
    }

    public static bool TryGetId(string profileId, out int id)
    {
        return _ids.TryGetId(profileId, out id);
    }

    public static string GetName(int profileId)
    {
        return _ids.GetName(profileId);
    }

    public static void Reset()
    {
        _ids = CreateRegistry();
    }

    private static StringIntRegistry CreateRegistry()
    {
        return new StringIntRegistry(
            capacity: 64,
            startId: 1,
            invalidId: InvalidId,
            comparer: StringComparer.Ordinal);
    }
}
