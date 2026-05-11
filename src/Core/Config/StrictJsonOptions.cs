using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.Core.Config
{
    public static class StrictJsonOptions
    {
        public static JsonSerializerOptions CreateCamelCase(bool includeFields = false)
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                IncludeFields = includeFields
            };
        }
    }
}
