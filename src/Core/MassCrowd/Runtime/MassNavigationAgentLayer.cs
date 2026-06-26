namespace Ludots.Core.MassCrowd.Runtime;

public readonly struct MassNavigationAgentLayer
{
    public MassNavigationAgentLayer(uint categoryMask, uint interactionMask)
    {
        if (categoryMask == 0u)
        {
            throw new System.InvalidOperationException("MassNavigation agent layer requires a non-empty category mask.");
        }

        if (interactionMask == 0u)
        {
            throw new System.InvalidOperationException("MassNavigation agent layer requires a non-empty interaction mask.");
        }

        CategoryMask = categoryMask;
        InteractionMask = interactionMask;
    }

    public uint CategoryMask { get; }
    public uint InteractionMask { get; }
}
