using System;
using System.Reflection;
using Ludots.Core.Physics3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
public sealed class Physics3DArchitectureTests
{
    [Test]
    public void PublicInterface_DoesNotExposeBepuTypes()
    {
        Assembly assembly = typeof(IPhysics3DWorld).Assembly;
        foreach (Type type in assembly.GetExportedTypes())
        {
            Assert.That(IsBepuType(type), Is.False, $"Exported Physics3D type '{type}' is a Bepu type.");
            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                switch (member)
                {
                    case MethodInfo method:
                        AssertNotBepu(method.ReturnType, type, member);
                        foreach (ParameterInfo parameter in method.GetParameters())
                        {
                            AssertNotBepu(parameter.ParameterType, type, member);
                        }

                        break;
                    case PropertyInfo property:
                        AssertNotBepu(property.PropertyType, type, member);
                        break;
                    case FieldInfo field:
                        AssertNotBepu(field.FieldType, type, member);
                        break;
                }
            }
        }
    }

    private static void AssertNotBepu(Type memberType, Type declaringType, MemberInfo member)
    {
        Assert.That(
            IsBepuType(memberType),
            Is.False,
            $"Public Physics3D member '{declaringType.FullName}.{member.Name}' exposes Bepu type '{memberType}'.");
    }

    private static bool IsBepuType(Type type)
    {
        while (type.HasElementType)
        {
            type = type.GetElementType()
                ?? throw new InvalidOperationException("Physics3D member element type is missing.");
        }

        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                if (IsBepuType(argument))
                {
                    return true;
                }
            }
        }

        string? assemblyName = type.Assembly.GetName().Name;
        return assemblyName is "BepuPhysics" or "BepuUtilities";
    }
}
