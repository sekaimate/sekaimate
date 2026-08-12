using Basis.Network.Core;
using Xunit;

namespace BasisNetworkCore.Tests;

public sealed class WebSocketFrameCodecTests
{
    public static TheoryData<WebSocketFrameKind> FrameKinds => new()
    {
        WebSocketFrameKind.Hello,
        WebSocketFrameKind.Accept,
        WebSocketFrameKind.Data,
        WebSocketFrameKind.Reject,
        WebSocketFrameKind.Disconnect,
    };

    public static TheoryData<DeliveryMethod> DeliveryMethods => new()
    {
        DeliveryMethod.Unreliable,
        DeliveryMethod.ReliableUnordered,
        DeliveryMethod.Sequenced,
        DeliveryMethod.ReliableOrdered,
        DeliveryMethod.ReliableSequenced,
    };

    [Theory]
    [MemberData(nameof(FrameKinds))]
    public void EncodeAndDecode_PreservesEveryFrameKind(WebSocketFrameKind kind)
    {
        byte[] encoded = WebSocketFrameCodec.Encode(
            kind, 12, DeliveryMethod.ReliableOrdered, new byte[] { 3, 5, 8 }, 3);

        bool decoded = WebSocketFrameCodec.TryDecode(encoded, 3, out WebSocketFrame frame, out WebSocketFrameDecodeError error);

        Assert.True(decoded);
        Assert.Equal(WebSocketFrameDecodeError.None, error);
        Assert.Equal(kind, frame.Kind);
        Assert.Equal((byte)12, frame.Channel);
        Assert.Equal(DeliveryMethod.ReliableOrdered, frame.DeliveryMethod);
        Assert.Equal(new byte[] { 3, 5, 8 }, frame.Payload);
    }

    [Theory]
    [MemberData(nameof(DeliveryMethods))]
    public void EncodeAndDecode_PreservesEveryDeliveryMethod(DeliveryMethod deliveryMethod)
    {
        byte[] encoded = WebSocketFrameCodec.Encode(
            WebSocketFrameKind.Data, 0, deliveryMethod, Array.Empty<byte>(), 0);

        Assert.True(WebSocketFrameCodec.TryDecode(encoded, 0, out WebSocketFrame frame, out _));
        Assert.Equal(deliveryMethod, frame.DeliveryMethod);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    public void EncodeAndDecode_AcceptsValidChannelBoundary(byte channel)
    {
        byte[] encoded = WebSocketFrameCodec.Encode(
            WebSocketFrameKind.Data, channel, DeliveryMethod.Unreliable, Array.Empty<byte>(), 0);

        Assert.True(WebSocketFrameCodec.TryDecode(encoded, 0, out WebSocketFrame frame, out _));
        Assert.Equal(channel, frame.Channel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TryDecode_RejectsTruncatedHeader(int length)
    {
        byte[] encoded = new byte[length];

        Assert.False(WebSocketFrameCodec.TryDecode(encoded, 0, out _, out WebSocketFrameDecodeError error));
        Assert.Equal(WebSocketFrameDecodeError.HeaderTooShort, error);
    }

    [Fact]
    public void TryDecode_RejectsUnknownFrameKind()
    {
        byte[] encoded = { 255, 0, (byte)DeliveryMethod.ReliableOrdered };

        Assert.False(WebSocketFrameCodec.TryDecode(encoded, 0, out _, out WebSocketFrameDecodeError error));
        Assert.Equal(WebSocketFrameDecodeError.UnknownFrameKind, error);
    }

    [Fact]
    public void TryDecode_RejectsUnknownDeliveryMethod()
    {
        byte[] encoded = { (byte)WebSocketFrameKind.Data, 0, 255 };

        Assert.False(WebSocketFrameCodec.TryDecode(encoded, 0, out _, out WebSocketFrameDecodeError error));
        Assert.Equal(WebSocketFrameDecodeError.UnknownDeliveryMethod, error);
    }

    [Fact]
    public void TryDecode_RejectsChannelOutsideConfiguredRange()
    {
        byte[] encoded = {
            (byte)WebSocketFrameKind.Data,
            BasisNetworkCommons.TotalChannels,
            (byte)DeliveryMethod.ReliableOrdered,
        };

        Assert.False(WebSocketFrameCodec.TryDecode(encoded, 0, out _, out WebSocketFrameDecodeError error));
        Assert.Equal(WebSocketFrameDecodeError.InvalidChannel, error);
    }

    [Fact]
    public void TryDecode_RejectsPayloadOverMaximumLength()
    {
        byte[] encoded = {
            (byte)WebSocketFrameKind.Data,
            0,
            (byte)DeliveryMethod.ReliableOrdered,
            1,
            2,
        };

        Assert.False(WebSocketFrameCodec.TryDecode(encoded, 1, out _, out WebSocketFrameDecodeError error));
        Assert.Equal(WebSocketFrameDecodeError.PayloadTooLarge, error);
    }

    [Fact]
    public void Encode_RejectsUnknownValuesAndOversizedPayload()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WebSocketFrameCodec.Encode(
            (WebSocketFrameKind)255, 0, DeliveryMethod.ReliableOrdered, Array.Empty<byte>(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebSocketFrameCodec.Encode(
            WebSocketFrameKind.Data, BasisNetworkCommons.TotalChannels, DeliveryMethod.ReliableOrdered, Array.Empty<byte>(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebSocketFrameCodec.Encode(
            WebSocketFrameKind.Data, 0, (DeliveryMethod)255, Array.Empty<byte>(), 0));
        Assert.Throws<ArgumentException>(() => WebSocketFrameCodec.Encode(
            WebSocketFrameKind.Data, 0, DeliveryMethod.ReliableOrdered, new byte[] { 1 }, 0));
    }
}
