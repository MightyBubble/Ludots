using System.Collections.Generic;

namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// Declarative description of one App process (desktop client, dedicated server, editor, ...).
    /// </summary>
    public sealed record AppDescriptor(
        string AppId,
        string HostKind,
        string AdapterId,
        IReadOnlyDictionary<string, string> Properties);
}
