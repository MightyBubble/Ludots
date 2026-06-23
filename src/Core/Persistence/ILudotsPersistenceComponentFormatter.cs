using System;

namespace Ludots.Core.Persistence
{
    public interface ILudotsPersistenceComponentFormatter
    {
        Type ComponentType { get; }
    }
}
