using System;
using Arch.Core;

namespace Ludots.Core.EntityCollections;

public interface IEntityCollectionSource
{
    ReadOnlySpan<Entity> Read();
    uint Revision { get; }
    bool Contains(Entity entity);
}
