using System;
using System.Globalization;
using Ludots.Core.Registry;

namespace Ludots.Core.MassNavigation.Runtime;

public static class MassNavigationProfileRegistry
{
    private const string ParameterProfileKeyPrefix = "$massnav.params:";

    private static StringIntRegistry _ids = CreateRegistry();

    public static int InvalidId => 0;

    public static int Register(string profileId)
    {
        return _ids.Register(profileId);
    }

    /// <summary>
    /// 按模板参数组合 intern 隐式档：相同 (speed, radius, heavy) 解析到同一档 id，
    /// 命名档（Register 的字符串）与参数档共用一个 intern 表；参数档 key 带
    /// 前缀，避免与 Navigation/agent_profiles.json 的命名档撞名。
    /// </summary>
    public static int InternParameters(float speedCmPerSecond, float radiusCm, bool heavy)
    {
        return _ids.Register(
            ParameterProfileKeyPrefix +
            speedCmPerSecond.ToString("R", CultureInfo.InvariantCulture) + ";" +
            radiusCm.ToString("R", CultureInfo.InvariantCulture) + ";" +
            (heavy ? "1" : "0"));
    }

    public static bool IsParameterProfile(int profileId)
    {
        return _ids.GetName(profileId).StartsWith(ParameterProfileKeyPrefix, StringComparison.Ordinal);
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
