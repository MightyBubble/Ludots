using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;

namespace Ludots.Tests.TestCommon
{
    /// <summary>
    /// Test helper: LogicView / PresentBinding camera when seats exist; session boot camera otherwise.
    /// </summary>
    public static class AuthorityCameraAccess
    {
        public static CameraManager AuthorityCamera(this GameEngine engine) =>
            ClientLocalSeatAccess.ResolveAuthorityCamera(engine);
    }
}
