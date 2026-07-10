using System;

namespace Ludots.Core.Persistence
{
    public interface IPersistenceTypeResolver
    {
        Type Resolve(string assemblyQualifiedTypeName);
    }
}
