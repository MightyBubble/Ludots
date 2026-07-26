using System;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class OrderAdmissionEntityIntakeEndSystem : ISystem<float>
    {
        private readonly OrderAdmissionResultBuffer _results;

        public OrderAdmissionEntityIntakeEndSystem(OrderAdmissionResultBuffer results)
        {
            _results = results ?? throw new ArgumentNullException(nameof(results));
        }

        public void Initialize() { }

        public void Update(in float dt) => _results.EndEntityIntake();

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
