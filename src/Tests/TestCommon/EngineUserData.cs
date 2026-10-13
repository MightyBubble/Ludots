using System;
using System.IO;

namespace Ludots.Tests
{
    internal static class EngineUserData
    {
        public static string CreateIsolatedRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "ludots-user-data", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
