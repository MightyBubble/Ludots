using System.IO;

namespace Ludots.Core.Modding
{
    public interface IVirtualFileSystem
    {
        void Mount(string modId, string physicalPath);
        Stream GetStream(string uri);
        bool TryResolveFullPath(string uri, out string fullPath);
    }
}
