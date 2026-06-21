using System;
using Ludots.Core.Registry;

namespace Ludots.Core.MassCrowd.Runtime;

public static class MassCrowdFormationRegistry
{
    private static StringIntRegistry _ids = CreateRegistry();

    public static int InvalidId => 0;

    public static int Register(string formationId)
    {
        return _ids.Register(formationId);
    }

    public static int GetId(string formationId)
    {
        return _ids.GetId(formationId);
    }

    public static bool TryGetId(string formationId, out int id)
    {
        return _ids.TryGetId(formationId, out id);
    }

    public static string GetName(int formationId)
    {
        return _ids.GetName(formationId);
    }

    public static void Reset()
    {
        _ids = CreateRegistry();
    }

    private static StringIntRegistry CreateRegistry()
    {
        return new StringIntRegistry(
            capacity: 256,
            startId: 1,
            invalidId: InvalidId,
            comparer: StringComparer.Ordinal);
    }
}
