using System.Diagnostics.CodeAnalysis;
using CapabilityStandardMassNavigationLargeWorld10kMod;
using CoreInputMod;
using Ludots.Adapter.WebGpu;
using Ludots.Client.WebGpu.Runtime;
using Ludots.Core.Hosting;
using LudotsCoreMod;
using MassNavigationMod;

Console.WriteLine("[Ludots WebGPU] Starting browser-resident C# runtime.");
PreserveModEntries();

string assetsRoot = Path.GetFullPath("assets");
var modPlan = ResolvedModLoadPlan.CreateExplicit(
[
    new ResolvedModLoadEntry("LudotsCoreMod", Path.GetFullPath("mods/LudotsCoreMod")),
    new ResolvedModLoadEntry("CoreInputMod", Path.GetFullPath("mods/CoreInputMod")),
    new ResolvedModLoadEntry("MassNavigationMod", Path.GetFullPath("mods/MassNavigationMod")),
    new ResolvedModLoadEntry(
        "CapabilityStandardMassNavigationLargeWorld10kMod",
        Path.GetFullPath("mods/CapabilityStandardMassNavigationLargeWorld10kMod")),
]);

WebGpuFontAtlas textAtlas = WebGpuFontAtlas.Load(Path.GetFullPath("fonts/DroidSans.webgpu-font"));
var frameSource = new MassNavigationWebGpuFrameSource(modPlan, assetsRoot, textAtlas);
var runtime = new WebGpuBrowserRuntime("#canvas", frameSource);
runtime.Run();

[DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(LudotsCoreModEntry))]
[DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(CoreInputModEntry))]
[DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(MassNavigationModEntry))]
[DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(CapabilityStandardMassNavigationLargeWorld10kModEntry))]
static void PreserveModEntries()
{
    GC.KeepAlive(typeof(LudotsCoreModEntry).Assembly);
    GC.KeepAlive(typeof(CoreInputModEntry).Assembly);
    GC.KeepAlive(typeof(MassNavigationModEntry).Assembly);
    GC.KeepAlive(typeof(CapabilityStandardMassNavigationLargeWorld10kModEntry).Assembly);
}
