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
        /// Register a simple event handler. For non-map events (e.g. custom mod events).
        /// </summary>
        void OnEvent(EventKey eventKey, Func<ScriptContext, Task> handler);

        // Keep TriggerManager during transition (Phase 2a → 2c)
        [Obsolete("Use SystemFactoryRegistry, TriggerDecorators, or OnEvent instead. Will be removed in Phase 2c.")]
        TriggerManager TriggerManager { get; }

        void Log(string message);
        void Log(LogLevel level, string message);
        Stream GetResource(string uri);
    }
}
