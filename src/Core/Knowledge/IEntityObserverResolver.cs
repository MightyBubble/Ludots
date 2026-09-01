using Arch.Core;

namespace Ludots.Core.Knowledge
{
    public interface IEntityObserverResolver
    {
        Entity ResolveObserver(World world, Entity source);
    }
}
