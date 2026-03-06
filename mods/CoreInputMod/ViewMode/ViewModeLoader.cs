using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CoreInputMod.ViewMode
{
    public static class ViewModeLoader
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static List<ViewModeConfig> LoadFromStream(Stream stream)
        {
            return JsonSerializer.Deserialize<List<ViewModeConfig>>(stream, Options)
                   ?? new List<ViewModeConfig>();
        }
    }
}
