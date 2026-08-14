using System.Text.Json.Serialization;

namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// Selects which map-owned terrain surface is presented to the player.
    /// This is independent from <see cref="IVisualHeightmap"/>, which remains
    /// the shared height truth for grounding, picking, and camera placement.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TerrainPresentationSource : byte
    {
        BoardTerrain = 0,
        VisualHeightmap = 1,
    }

    /// <summary>
    /// Map-owned selection of the terrain surface presented by a host.
    /// </summary>
    public sealed class TerrainPresentationBindingConfig
    {
        public TerrainPresentationSource Source { get; set; }

        public string BoardName { get; set; } = string.Empty;

        public TerrainPresentationBindingConfig Clone()
        {
            return new TerrainPresentationBindingConfig
            {
                Source = Source,
                BoardName = BoardName,
            };
        }
    }
}
