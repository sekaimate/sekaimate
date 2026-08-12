using Basis.Network.Core;
using Xunit;

namespace BasisNetworkCore.Tests;

public sealed class BasisNetworkStackRegistryTests
{
    [Fact]
    public void DefaultId_RemainsLiteNetLib()
    {
        Assert.Equal(BasisNetworkStackRegistry.LiteNetLibId, BasisNetworkStackRegistry.DefaultId);
    }

    [Fact]
    public void Create_RejectsUnknownStackInsteadOfFallingBack()
    {
        Configuration configuration = new() { NetworkStackId = "missing-stack" };

        Assert.Throws<KeyNotFoundException>(() => BasisNetworkStackRegistry.Create(
            configuration.NetworkStackId,
            new EventBasedNetListener(),
            configuration));
    }
}
