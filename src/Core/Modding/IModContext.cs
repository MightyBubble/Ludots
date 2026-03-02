using System;
using System.IO;
using System.Threading.Tasks;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.Core.Modding
{
    public interface IModContext
    {
        string ModId { get; }
        IVirtualFileSystem VFS { get; }
        FunctionRegistry FunctionRegistry { get; }
        SystemFactoryRegistry SystemFactoryRegistry { get; }
        TriggerDecoratorRegistry TriggerDecorators { get; }
        LogChannel LogChannel { get; }

        /// <summary>
        /// Register a simple event handler. Fires for both global and map-scoped events.
        /// </summary>
        void OnEvent(EventKey eventKey, Func<ScriptContext, Task> handler);

        void Log(string message);
        void Log(LogLevel level, string message);
        Stream GetResource(string uri);
    }
}
