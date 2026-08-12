using Basis.Network.Core;
using Xunit;

namespace BasisNetworkCore.Tests;

public sealed class WebSocketAcceptPayloadCodecTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(0x01020304, 1, 2, 3, 4)]
    [InlineData(int.MaxValue, 127, 255, 255, 255)]
    public void Encode_UsesFourByteBigEndianPeerId(
        int peerId,
        byte first,
        byte second,
        byte third,
        byte fourth)
    {
        Assert.Equal(
            new[] { first, second, third, fourth },
            WebSocketAcceptPayloadCodec.Encode(peerId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(int.MaxValue)]
    public void TryDecode_RoundTripsNonNegativePeerId(int peerId)
    {
        Assert.True(WebSocketAcceptPayloadCodec.TryDecode(
            WebSocketAcceptPayloadCodec.Encode(peerId),
            out int decodedPeerId));
        Assert.Equal(peerId, decodedPeerId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00")]
    [InlineData("000000")]
    [InlineData("0000000000")]
    [InlineData("80000000")]
    [InlineData("FFFFFFFF")]
    public void TryDecode_RejectsMissingMalformedOrNegativePeerId(string payloadHex)
    {
        byte[] payload = Convert.FromHexString(payloadHex);

        Assert.False(WebSocketAcceptPayloadCodec.TryDecode(payload, out _));
    }

    [Fact]
    public void Encode_RejectsNegativePeerId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WebSocketAcceptPayloadCodec.Encode(-1));
    }
}
