using System.Text.Json;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Editor.Bridge;
using NUnit.Framework;

namespace ArchitectureTests
{
    public sealed class EditorBridgeNavTileDebugPayloadTests
    {
        [Test]
        public void DetourDebugPayload_JsonSerializesTriAreaIdsAsNumericArray()
        {
            const int tileSizeCm = 6400;
            NavTile tile = DefaultGridNavTileFactory.CreateFlatTile(
                chunkX: 0,
                chunkY: 0,
                layer: 0,
                tileVersion: NavTileBinary.FormatVersion,
                tileWidthCm: tileSizeCm,
                tileHeightCm: tileSizeCm,
                tileWidthCells: 64,
                tileHeightCells: 64,
                areaId: 7);
            byte[] detourBytes = DetourNavQueryEngine.BuildFlatGridBaselineDetourTileBytes(tile, tileSizeCm, tileSizeCm);

            object payload = EditorNavTileDebugPayload.BuildFromDetourTileBytes(detourBytes);
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using JsonDocument doc = JsonDocument.Parse(json);

            JsonElement triAreaIds = doc.RootElement.GetProperty("triAreaIds");
            Assert.That(triAreaIds.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(triAreaIds.GetArrayLength(), Is.GreaterThan(0));
            Assert.That(triAreaIds[0].ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(triAreaIds[0].GetInt32(), Is.EqualTo(7));
        }
    }
}
