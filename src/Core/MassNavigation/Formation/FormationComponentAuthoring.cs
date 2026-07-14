using Ludots.Core.Config;

namespace Ludots.Core.MassNavigation.Formation;

public static class FormationComponentAuthoring
{
    public const string MemberStateComponentName = nameof(FormationMemberState);
    public const string AnchorStateComponentName = nameof(FormationAnchorState);
    public const string CommandStateComponentName = nameof(FormationCommandState);
    public const string RuntimeStateComponentName = nameof(FormationRuntimeState);

    public static void Register(string modId)
    {
        ComponentRegistry.Register<FormationMemberState>(MemberStateComponentName, modId);
        ComponentRegistry.Register<FormationAnchorState>(AnchorStateComponentName, modId);
        ComponentRegistry.Register<FormationCommandState>(CommandStateComponentName, modId);
        ComponentRegistry.Register<FormationRuntimeState>(RuntimeStateComponentName, modId);
    }
}
