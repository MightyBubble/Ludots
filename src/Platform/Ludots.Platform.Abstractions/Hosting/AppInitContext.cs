using System.Collections.Generic;

namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// Initialization inputs handed to <see cref="IAppHost.Initialize"/> by the process entry point.
    /// AssetsRoot may be null when the host's bootstrap derives it (e.g. Raylib walks up from BaseDirectory).
    /// </summary>
    public sealed record AppInitContext(
        string BaseDirectory,
        IReadOnlyList<string> ModPaths,
        string? AssetsRoot);
}
