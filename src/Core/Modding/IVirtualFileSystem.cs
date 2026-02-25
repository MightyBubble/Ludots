using System.IO;

namespace Ludots.Core.Modding
{
    public interface IVirtualFileSystem
    {
        void Mount(string modId, string physicalPath);
        bool Unmount(string modId);
        Stream GetStream(string uri);
        bool TryResolveFullPath(string uri, out string fullPath);
    }
}
