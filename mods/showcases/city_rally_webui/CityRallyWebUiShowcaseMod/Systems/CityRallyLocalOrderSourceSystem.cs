using System;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Diagnostics;

namespace CityRallyWebUiShowcaseMod.Systems
{
    /// <summary>
    /// 城池集结点右键路由。浏览器壳（BrowserRtsProductionShowcaseMod）的输入映射只有 moveTo；
    /// 本系统加载自身映射（选中城池 → 右键设集结点），并在更新末尾覆盖 ActiveInputOrderMapping，
    /// 使集结点路由优先于壳的通用 moveTo 路由。
    /// </summary>
    public sealed class CityRallyLocalOrderSourceSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly LocalOrderSourceHelper _helper;
        private readonly IModContext _context;
        private InputOrderMappingSystem? _mapping;

        public CityRallyLocalOrderSourceSystem(
            World world,
            Dictionary<string, object> globals,
            OrderQueue orders,
            IModContext context)
        {
            _world = world;
            _globals = globals;
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _helper = new LocalOrderSourceHelper(world, globals, orders);
        }

        public void Initialize()
        {
            EnsureInitialized();
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            EnsureInitialized();
            if (_mapping == null)
            {
                return;
            }

            Entity actor = _helper.GetControlledActor();
            if (!_world.IsAlive(actor))
            {
                return;
            }

            if (!_helper.TryBindSoleSeatActor(_mapping, actor))
            {
                return;
            }

            // 覆盖浏览器壳每帧设置的通用映射：集结点路由优先。
            _globals[CoreServiceKeys.ActiveInputOrderMapping.Name] = _mapping;
            _mapping.Update(dt);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void EnsureInitialized()
        {
            if (_mapping != null)
            {
                return;
            }

            _mapping = _helper.TryCreateMapping(_context);
            if (_mapping == null)
            {
                return;
            }

            _globals[CoreServiceKeys.ActiveInputOrderMapping.Name] = _mapping;
        }
    }
}
