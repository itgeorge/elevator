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

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.UnknownEncodingFamily));
        Assert.That(result.Rides, Is.Null);
        Assert.That(result.BlocksMatched, Is.True);
    }

    [Test]
    public void Resolve_matching_invalid_payload_returns_invalid()
    {
        var block = new T55Block(0xCCC70000);

        var result = RideBlockResolver.Resolve(block, block);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.InvalidBlockFormat));
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
    public void Resolve_mismatch_both_valid_prefers_block5()
    {
        var block5 = TokenBlockUtils.Encode(73, EncodingSequences.Mercury);
        var block6 = TokenBlockUtils.Encode(80, EncodingSequences.Mercury);

        var result = RideBlockResolver.Resolve(block5, block6);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(73u));
        Assert.That(result.SourceBlockNumber, Is.EqualTo(5));
        Assert.That(result.WarningMessage, Is.EqualTo("Warning: blocks 5 and 6 differ; using block 5 (73 rides)."));
    }

    [Test]
    public void Resolve_mismatch_neither_valid_known_family_returns_invalid()
    {
        var block5 = new T55Block(0xCCC70000);
        var block6 = new T55Block(0xCCC70001);

        var result = RideBlockResolver.Resolve(block5, block6);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.InvalidBlockFormat));
    }

    [Test]
    public void Resolve_mismatch_both_unknown_family_returns_unknown()
    {
        var block5 = new T55Block(0xDEAD1234);
        var block6 = new T55Block(0xBEEF5678);

        var result = RideBlockResolver.Resolve(block5, block6);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.UnknownEncodingFamily));
        Assert.That(result.SourceBlockNumber, Is.EqualTo(5));
    }

    [Test]
    public void Resolve_matching_43fe_sequence_family_returns_rides()
    {
        var block = TokenBlockUtils.EncodeByFamily(0, TokenBlockUtils.Families.Family48C7_0To127);

        var result = RideBlockResolver.Resolve(block, block);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success));
        Assert.That(result.Rides, Is.EqualTo(0u));
        Assert.That(result.BlocksMatched, Is.True);
    }

    [Test]
    public void Resolve_matching_rides_above_500_returns_invalid()
    {
        var block = TokenBlockUtils.EncodeByFamily(127, TokenBlockUtils.Families.Family384To500);

        var result = RideBlockResolver.Resolve(block, block);

        Assert.That(result.Status, Is.EqualTo(RideReadStatus.InvalidBlockFormat));
    }
}
