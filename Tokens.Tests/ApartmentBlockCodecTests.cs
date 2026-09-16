using System.Text;
using NUnit.Framework;
using Tokens;

namespace Tokens.Tests;

[TestFixture]
public class ApartmentBlockCodecTests
{
    private static readonly byte[] TestSecret = "phase0-test-secret"u8.ToArray();
    private static readonly T55Block Block3 = T55Block.FromHex("CCC749CC");
    private static readonly T55Block AlternateBlock3 = T55Block.FromHex("48C74948");

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(42)]
    [TestCase(127)]
    [TestCase(255)]
    public void Round_trip_encode_decode_returns_original_apt(byte apt)
    {
        var block4 = ApartmentBlockCodec.Encode(TestSecret, Block3, building: 0, apt);

        Assert.That(ApartmentBlockCodec.TryDecode(TestSecret, Block3, block4, out var payload), Is.True);
        Assert.That(payload.Building, Is.EqualTo(0));
        Assert.That(payload.Apt, Is.EqualTo(apt));
    }

    [Test]
    public void Wrong_block3_fails_decode()
    {
        var block4 = ApartmentBlockCodec.Encode(TestSecret, Block3, building: 0, apt: 17);

        Assert.That(ApartmentBlockCodec.TryDecode(TestSecret, AlternateBlock3, block4, out _), Is.False);
    }

    [Test]
    public void Wrong_secret_fails_decode()
    {
        var block4 = ApartmentBlockCodec.Encode(TestSecret, Block3, building: 0, apt: 17);
        var wrongSecret = Encoding.UTF8.GetBytes("different-secret");

        Assert.That(ApartmentBlockCodec.TryDecode(wrongSecret, Block3, block4, out _), Is.False);
    }

    [Test]
    public void Factory_mirror_block4_equals_block3_fails_decode()
    {
        Assert.That(ApartmentBlockCodec.TryDecode(TestSecret, Block3, Block3, out _), Is.False);
    }

    [Test]
    public void Junk_block4_fails_decode()
    {
        Assert.That(ApartmentBlockCodec.TryDecode(TestSecret, Block3, T55Block.FromHex("DEADBEEF"), out _), Is.False);
    }

    [Test]
    public void Same_payload_with_different_block3_produces_different_block4()
    {
        var block4A = ApartmentBlockCodec.Encode(TestSecret, Block3, building: 0, apt: 42);
        var block4B = ApartmentBlockCodec.Encode(TestSecret, AlternateBlock3, building: 0, apt: 42);

        Assert.That(block4A.Value, Is.Not.EqualTo(block4B.Value));
    }

    [Test]
    public void Different_apt_produces_different_block4()
    {
        var block4A = ApartmentBlockCodec.Encode(TestSecret, Block3, building: 0, apt: 10);
        var block4B = ApartmentBlockCodec.Encode(TestSecret, Block3, building: 0, apt: 11);

        Assert.That(block4A.Value, Is.Not.EqualTo(block4B.Value));
    }

    [Test]
    public void V1_encode_returns_building_zero_in_payload()
    {
        var block4 = ApartmentBlockCodec.Encode(TestSecret, Block3, building: 0, apt: 99);

        Assert.That(ApartmentBlockCodec.TryDecode(TestSecret, Block3, block4, out var payload), Is.True);
        Assert.That(payload.Building, Is.EqualTo(0));
        Assert.That(payload.Apt, Is.EqualTo(99));
    }
}
