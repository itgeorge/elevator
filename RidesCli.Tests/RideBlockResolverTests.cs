using NUnit.Framework;
using RidesCli;
using Tokens;

namespace RidesCli.Tests;

[TestFixture]
public class RideBlockResolverTests
{
    [Test]
    public void Resolve_matching_valid_returns_rides()
    {
        var block = TokenBlockUtils.Encode(73, EncodingSequences.Mercury);

        var result = RideBlockResolver.Resolve(block, block);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(73u));
        Assert.That(result.SourceBlockNumber, Is.EqualTo(5));
        Assert.That(result.BlocksMatched, Is.True);
        Assert.That(result.WarningMessage, Is.Null);
    }

    [Test]
    public void Resolve_matching_unknown_family_returns_unknown()
    {
        var block = new T55Block(0xDEAD1234);

        var result = RideBlockResolver.Resolve(block, block);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.UnknownEncodingSequence));
        Assert.That(result.Rides, Is.Null);
        Assert.That(result.BlocksMatched, Is.True);
    }

    [Test]
    public void Resolve_matching_malformed_payload_returns_unknown_without_high_word_guessing()
    {
        var block = new T55Block(0xCCC70000);

        var result = RideBlockResolver.Resolve(block, block);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.UnknownEncodingSequence));
        Assert.That(result.Rides, Is.Null);
        Assert.That(result.BlocksMatched, Is.True);
    }

    [Test]
    public void Resolve_mismatch_only_block5_valid_uses_block5()
    {
        var block5 = TokenBlockUtils.Encode(73, EncodingSequences.Mercury);
        var block6 = new T55Block(0xCCC70000);

        var result = RideBlockResolver.Resolve(block5, block6);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(73u));
        Assert.That(result.SourceBlockNumber, Is.EqualTo(5));
        Assert.That(result.WarningMessage, Is.EqualTo("Warning: blocks 5 and 6 differ; using block 5."));
    }

    [Test]
    public void Resolve_mismatch_only_block6_valid_uses_block6()
    {
        var block5 = new T55Block(0xCCC70000);
        var block6 = TokenBlockUtils.Encode(42, EncodingSequences.Mercury);

        var result = RideBlockResolver.Resolve(block5, block6);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(42u));
        Assert.That(result.SourceBlockNumber, Is.EqualTo(6));
        Assert.That(result.WarningMessage, Is.EqualTo("Warning: blocks 5 and 6 differ; using block 6."));
    }

    [Test]
    public void Resolve_mismatch_both_valid_prefers_block6()
    {
        var block5 = TokenBlockUtils.Encode(73, EncodingSequences.Mercury);
        var block6 = TokenBlockUtils.Encode(80, EncodingSequences.Mercury);

        var result = RideBlockResolver.Resolve(block5, block6);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(80u));
        Assert.That(result.SourceBlockNumber, Is.EqualTo(6));
        Assert.That(result.WarningMessage, Is.EqualTo("Warning: blocks 5 and 6 differ; using block 6 (80 rides)."));
    }

    [Test]
    public void Resolve_mismatch_both_valid_elevator_confirmed_venus_prefers_block6()
    {
        // Hardware experiment (2026-07-20): elevator decrements from block 6 when mirrors disagree.
        var block5 = EncodingSequences.Venus.Encode(256);
        var block6 = EncodingSequences.Venus.Encode(255);

        var result = RideBlockResolver.Resolve(block5, block6);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(255u));
        Assert.That(result.SourceBlockNumber, Is.EqualTo(6));
        Assert.That(result.WarningMessage, Is.EqualTo("Warning: blocks 5 and 6 differ; using block 6 (255 rides)."));
    }

    [Test]
    public void Resolve_mismatch_neither_valid_block_returns_unknown()
    {
        var block5 = new T55Block(0xCCC70000);
        var block6 = new T55Block(0xCCC70001);

        var result = RideBlockResolver.Resolve(block5, block6);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.UnknownEncodingSequence));
    }

    [Test]
    public void Resolve_mismatch_both_unknown_family_returns_unknown()
    {
        var block5 = new T55Block(0xDEAD1234);
        var block6 = new T55Block(0xBEEF5678);

        var result = RideBlockResolver.Resolve(block5, block6);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.UnknownEncodingSequence));
        Assert.That(result.SourceBlockNumber, Is.EqualTo(5));
    }

    [Test]
    public void Resolve_matching_43fe_sequence_family_returns_rides()
    {
        var block = EncodingSequences.Venus.Encode(0);

        var result = RideBlockResolver.Resolve(block, block);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(0u));
        Assert.That(result.BlocksMatched, Is.True);
    }

    [Test]
    public void Resolve_matching_venus_high_range_block_returns_rides()
    {
        var block = EncodingSequences.Venus.Encode(500);

        var result = RideBlockResolver.Resolve(block, block);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(500u));
        Assert.That(result.BlocksMatched, Is.True);
    }

    [Test]
    public void Resolve_matching_venus_48C6_block_returns_rides()
    {
        var block = EncodingSequences.Venus.Encode(383);

        var result = RideBlockResolver.Resolve(block, block);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(383u));
        Assert.That(result.BlocksMatched, Is.True);
    }

    [TestCase(256u, 0x18131208u)]
    [TestCase(383u, 0x18136DFFu)]
    [TestCase(384u, 0xEB139200u)]
    [TestCase(500u, 0xEB13E647u)]
    public void Resolve_matching_earth_high_range_blocks_returns_rides(uint expectedRides, uint blockValue)
    {
        var block = new T55Block(blockValue);

        var result = RideBlockResolver.Resolve(block, block);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(expectedRides));
        Assert.That(result.BlocksMatched, Is.True);
    }

    [TestCase(256u, 0x1F13120Fu)]
    [TestCase(383u, 0x1F136DF8u)]
    [TestCase(384u, 0xEC139207u)]
    [TestCase(500u, 0xEC13E640u)]
    public void Resolve_matching_pluto_high_range_blocks_returns_rides(uint expectedRides, uint blockValue)
    {
        var block = new T55Block(blockValue);

        var result = RideBlockResolver.Resolve(block, block);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(expectedRides));
        Assert.That(result.BlocksMatched, Is.True);
    }

    [TestCase(0x8C124980u, 0u)]
    [TestCase(0x7F124188u, 8u)]
    [TestCase(0x8C12C900u, 128u)]
    [TestCase(0x8C134981u, 256u)]
    [TestCase(0x8C13C901u, 384u)]
    public void Resolve_matching_jupiter_blocks_returns_rides(uint blockValue, uint expectedRides)
    {
        var result = RideBlockResolver.Resolve(new T55Block(blockValue), new T55Block(blockValue));

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(expectedRides));
    }

    [TestCase(0x8B1249F0u, 0u)]
    [TestCase(0x8B1248F1u, 1u)]
    [TestCase(0x781241F8u, 8u)]
    [TestCase(0x8B12C970u, 128u)]
    [TestCase(0x8B1349F1u, 256u)]
    [TestCase(0x8B13C971u, 384u)]
    [TestCase(0x8B13BD05u, 500u)]
    public void Resolve_matching_saturn_blocks_returns_rides(uint blockValue, uint expectedRides)
    {
        var result = RideBlockResolver.Resolve(new T55Block(blockValue), new T55Block(blockValue));

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(expectedRides));
    }

    [TestCase(0x781266DFu, 47u)]
    public void Resolve_saturn_anchor_blocks_returns_rides(uint blockValue, uint expectedRides)
    {
        var result = RideBlockResolver.Resolve(new T55Block(blockValue), new T55Block(blockValue));

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(expectedRides));
    }

    [TestCase(0x891249D0u, 0u)]
    [TestCase(0x891248D1u, 1u)]
    [TestCase(0x89124ED7u, 7u)]
    [TestCase(0x7A1241D8u, 8u)]
    [TestCase(0x8912C950u, 128u)]
    [TestCase(0x891349D1u, 256u)]
    [TestCase(0x8913C951u, 384u)]
    [TestCase(0x8913BD25u, 500u)]
    public void Resolve_matching_uranus_blocks_returns_rides(uint blockValue, uint expectedRides)
    {
        var result = RideBlockResolver.Resolve(new T55Block(blockValue), new T55Block(blockValue));

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(expectedRides));
    }

    [TestCase(0x7A1222BBu, 107u)]
    public void Resolve_uranus_anchor_blocks_returns_rides(uint blockValue, uint expectedRides)
    {
        var result = RideBlockResolver.Resolve(new T55Block(blockValue), new T55Block(blockValue));

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(expectedRides));
    }

    [TestCase(0x8F1249B0u, 0u)]
    [TestCase(0x7C1236CFu, 127u)]
    [TestCase(0x8F12C930u, 128u)]
    [TestCase(0x7C12B64Fu, 255u)]
    [TestCase(0x8F1349B1u, 256u)]
    [TestCase(0x7C1336CEu, 383u)]
    [TestCase(0x8F13C931u, 384u)]
    [TestCase(0x8F13B840u, 497u)]
    [TestCase(0x8F13BD45u, 500u)]
    public void Resolve_matching_neptune_blocks_returns_rides(uint blockValue, uint expectedRides)
    {
        var result = RideBlockResolver.Resolve(new T55Block(blockValue), new T55Block(blockValue));

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(expectedRides));
        Assert.That(result.BlocksMatched, Is.True);
    }

    [Test]
    public void Resolve_matching_rides_above_500_returns_unknown()
    {
        var block = new T55Block(0x3FC6BC83);

        var result = RideBlockResolver.Resolve(block, block);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.UnknownEncodingSequence));
    }
}
