using Basis.Scripts.Device_Management.Devices.Desktop;
using System;

namespace Basis.Scripts.Device_Management.Devices.Web
{
    [Serializable]
    public sealed class BasisWebManagement : BasisDesktopManagement
    {
        public override bool IsDeviceBootable(string bootRequest)
        {
            return bootRequest == BasisConstants.Web;
        }
    }
}
