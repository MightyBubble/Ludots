using System;
using System.Runtime.CompilerServices;
using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    internal static class OrderEntityReferenceContract
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsUninitialized(Entity entity) => entity == default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsRequired(Entity entity) => entity != default && entity != Entity.Null;

        public static void Validate(in Order order, string boundary)
        {
            RequireRequired(order.Actor, nameof(Order.Actor), boundary);
            RequireCanonicalOptional(order.Target, nameof(Order.Target), boundary);
            RequireCanonicalOptional(order.TargetContext, nameof(Order.TargetContext), boundary);
            RequireCanonicalOptional(order.CommandSource, nameof(Order.CommandSource), boundary);
        }

        public static void RequireRequired(Entity entity, string fieldName, string boundary)
        {
            if (entity == default)
            {
                throw new InvalidOperationException(
                    $"{boundary} received an uninitialized {fieldName}; required entity references must not use default(Entity).");
            }

            if (entity == Entity.Null)
            {
                throw new InvalidOperationException(
                    $"{boundary} requires a non-null {fieldName}.");
            }
        }

        public static void RequireCanonicalOptional(Entity entity, string fieldName, string boundary)
        {
            if (entity == default)
            {
                throw new InvalidOperationException(
                    $"{boundary} received an uninitialized {fieldName}; missing optional entity references must use Entity.Null.");
            }
        }
    }
}
