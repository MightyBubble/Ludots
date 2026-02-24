using System.IO;
using Ludots.Core.Scripting;

namespace Ludots.Core.Modding
{
    public class ModContext : IModContext
    {
        public string ModId { get; }
        public IVirtualFileSystem VFS { get; }
        public FunctionRegistry FunctionRegistry { get; }
        public TriggerManager TriggerManager { get; }

        public ModContext(string modId, IVirtualFileSystem vfs, FunctionRegistry fr, TriggerManager tm)
        {
            ModId = modId;
            VFS = vfs;
            FunctionRegistry = fr;
            TriggerManager = tm;
        }

        public void Log(string message)
        {
            System.Console.WriteLine($"[{ModId}] {message}");
        }

        public Stream GetResource(string uri)
        {
            return VFS.GetStream(uri);
        }
    }
}
