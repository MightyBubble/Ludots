using System;
using System.Collections.Generic;
using Ludots.Core.Modding;

namespace Ludots.Core.Scripting
{
    // Function returns a value (e.g. bool for condition, or int/string for data)
    public delegate object ScriptFunctionDelegate(ScriptContext context);

    public class FunctionRegistry
    {
        private readonly Dictionary<string, ScriptFunctionDelegate> _functions = new Dictionary<string, ScriptFunctionDelegate>();
        private readonly Dictionary<string, string> _registrationSource = new Dictionary<string, string>();
        private RegistrationConflictReport _conflictReport;

        public void SetConflictReport(RegistrationConflictReport report)
        {
            _conflictReport = report;
        }

        public void Register(string id, ScriptFunctionDelegate func, string modId = null)
        {
#if DEBUG
            if (_functions.ContainsKey(id))
            {
                string existingMod = _registrationSource.TryGetValue(id, out var em) ? em : "(core)";
                string newMod = modId ?? "(core)";
                Console.WriteLine($"[FunctionRegistry] WARNING: Function '{id}' registered by '{existingMod}', overwritten by '{newMod}' (last-wins).");
                _conflictReport?.Add("FunctionRegistry", id, existingMod, newMod);
            }
#endif
            _functions[id] = func;
            _registrationSource[id] = modId ?? "(core)";
        }

        public ScriptFunctionDelegate Get(string id)
        {
             if (_functions.TryGetValue(id, out var func))
                return func;
            return null;
        }
    }
}
