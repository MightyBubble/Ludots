using System.Text;

namespace Ludots.Client.WebGpu.Runtime
{
    /// <summary>
    /// Explicit capability inventory for the first WebGPU adapter slice.
    /// Missing capabilities are declared, never silently substituted.
    /// </summary>
    public static class WebGpuCapabilityDiagnostics
    {
        public const string AdapterId = "webgpu";

        public static string BuildStartupReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("WebGPU adapter capability report (phase-1 MVP):");
            sb.AppendLine("- Supported: window + Silk.NET.WebGPU/wgpu-native device init, clear pass, camera-driven view, basic cube instances from Core primitive buffer, console diagnostics.");
            sb.AppendLine("- Supported: Core input path via IInputBackend, UIRoot input routing, UiSurfaceHost ownership, Skia UI raster upload into WebGPU texture composite.");
            sb.AppendLine("- Not yet: skinned animation, terrain, ground overlay, global field, full material system, browser surface.");
            sb.AppendLine("- Fail-fast: missing wgpu-native, adapter, device, surface, or swapchain aborts startup with no Raylib/WebGL/OpenGL/Vulkan/Direct3D fallback.");
            return sb.ToString();
        }
    }
}
