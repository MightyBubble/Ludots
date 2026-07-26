using Arch.System;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Systems;

public sealed class OrderAdmissionGenerationEndSystem : ISystem<float>
{
    private readonly OrderAdmissionResultBuffer _results;

    public OrderAdmissionGenerationEndSystem(OrderAdmissionResultBuffer results)
    {
        _results = results ?? throw new ArgumentNullException(nameof(results));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void Update(in float dt) => _results.EndLogicStep();
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }
}
