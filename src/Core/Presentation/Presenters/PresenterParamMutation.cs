using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Presentation.Presenters
{
    internal readonly record struct PresenterParamMutation(Entity Entity, int Key, ParamLane Lane, Vector4 Vector, bool Clear)
    {
        private bool IsSpecified { get; } = true;
        public bool AppliesTo(Entity entity, int key) => IsSpecified && Entity == entity && Key == key;
    }
}
