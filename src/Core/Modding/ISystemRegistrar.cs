using Arch.System;
using Ludots.Core.Engine;

namespace Ludots.Core.Modding;

public interface ISystemRegistrar
{
    void RegisterSystem(ISystem<float> system, SystemGroup group);
    void RegisterPresentationSystem(ISystem<float> system);
    void InsertSystemBeforeRequired<TAnchor>(ISystem<float> system, SystemGroup group)
        where TAnchor : class;
}
