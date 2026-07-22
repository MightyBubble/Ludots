using Ludots.Core.Config;

namespace FormationCapabilityShowcaseMod.Runtime;

internal static class FormationCapabilityShowcaseFormationComponentAuthoring
{
    public const string MemberStateComponentName = nameof(FormationMemberState);
    public const string AnchorStateComponentName = nameof(FormationAnchorState);
    public const string RuntimeStateComponentName = nameof(FormationRuntimeState);

    public static void Register(string modId)
    {
        ComponentRegistry.Register<FormationMemberState>(MemberStateComponentName, modId);
        ComponentRegistry.Register<FormationAnchorState>(AnchorStateComponentName, modId);
        ComponentRegistry.Register<FormationRuntimeState>(RuntimeStateComponentName, modId);
    }
}
