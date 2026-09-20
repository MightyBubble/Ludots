using Ludots.Contracts;

namespace Ludots.Core.Presentation.Components;

[WriteOwner(LayerOwner.Simulation)]
[ReadAllowed(LayerOwner.Simulation, LayerOwner.Presentation)]
public struct PresentationStableId
{
    public int Value;
}
