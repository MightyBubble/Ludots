using System.IO;

using Ludots.Platform.Abstractions;

namespace Ludots.Core.Modding
{
    public interface IVirtualFileSystem : IRenderAssetPathResolver
    {
        void Mount(string modId, string physicalPath);
        bool Unmount(string modId);
        Stream GetStream(string uri);
        bool TryResolveFullPath(string uri, out string fullPath);
    }
}
