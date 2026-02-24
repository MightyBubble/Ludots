using System.IO;
using Ludots.Core.Scripting;

namespace Ludots.Core.Modding
{
    public interface IModContext
    {
        string ModId { get; }
        IVirtualFileSystem VFS { get; }
        FunctionRegistry FunctionRegistry { get; }
        TriggerManager TriggerManager { get; }
        
        void Log(string message);
        Stream GetResource(string uri);
    }
}
