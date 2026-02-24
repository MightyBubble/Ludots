using System;
using System.Runtime.InteropServices.JavaScript;
 
namespace Ludots.Web.Interop;
 
internal static partial class EntityRenderInterop
{
    [JSImport("updateEntityPositionsInt32", "LudotsRender")]
    internal static partial void UpdateEntityPositionsInt32([JSMarshalAs<JSType.MemoryView>] Span<byte> positions, int entityCount);
}
