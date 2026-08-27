using Basis.Scripts.Device_Management.Devices;
using NUnit.Framework;

public class BasisXRManagementTests
{
    [Test]
    public void DeInitializeBeforeInitializeCanRunMoreThanOnce()
    {
        BasisXRManagement xrManagement = new();

        Assert.DoesNotThrow(xrManagement.DeInitialize);
        Assert.DoesNotThrow(xrManagement.DeInitialize);
    }
}
