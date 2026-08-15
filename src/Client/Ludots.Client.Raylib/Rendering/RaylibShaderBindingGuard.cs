using System;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    internal static class RaylibShaderBindingGuard
    {
        public static int RequireUniform(Shader shader, string name, string shaderLabel)
        {
            int location = Rl.GetShaderLocation(shader, name);
            if (location < 0)
            {
                throw new InvalidOperationException($"{shaderLabel} shader uniform '{name}' was not found.");
            }

            return location;
        }

        public static int RequireAttribute(Shader shader, string name, string shaderLabel)
        {
            int location = Rl.GetShaderLocationAttrib(shader, name);
            if (location < 0)
            {
                throw new InvalidOperationException($"{shaderLabel} shader attribute '{name}' was not found.");
            }

            return location;
        }
    }
}
