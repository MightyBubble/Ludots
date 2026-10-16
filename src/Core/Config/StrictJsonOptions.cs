using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Ludots.Core.Config
{
    /// <remarks>
    /// 每个实例自带解析器：System.Text.Json 会让设置相同的选项共用一份类型元数据缓存，
    /// 只要进程里有一个静态选项活着，Mod 类型就会被这份缓存留住，collectible ALC 卸不掉。
    /// </remarks>
    public static class StrictJsonOptions
    {
        public static JsonSerializerOptions CreateCamelCase(bool includeFields = false)
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                IncludeFields = includeFields,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            };
        }

        public static JsonSerializerOptions CreateExact(bool includeFields = false)
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                IncludeFields = includeFields,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            };
        }
    }
}
