using LiveSkillWorkbenchMod.Contracts;
using LiveSkillWorkbenchMod.Runtime;
using Ludots.Core.Scripting;

namespace LiveSkillWorkbenchMod;

/// <summary>
/// Capability-local service keys for other Mods to inject/consume the workbench host.
/// Formal extension point for #618+; do not add these to CoreServiceKeys.
/// </summary>
public static class LiveSkillWorkbenchServiceKeys
{
	/// <summary>Published Live Skill Workbench runtime / document host.</summary>
	public static readonly ServiceKey<LiveSkillWorkbenchRuntime> Runtime =
		new("LiveSkillWorkbenchMod.Runtime");

	/// <summary>
	/// Optional bootstrap document source. When present at GameStart, the Mod loads it explicitly
	/// before installing the browser/DataPlane.
	/// </summary>
	public static readonly ServiceKey<ILiveSkillWorkbenchDocumentSource> DocumentSource =
		new("LiveSkillWorkbenchMod.DocumentSource");
}
