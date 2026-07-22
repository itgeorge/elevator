using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tokens;

namespace Tokens.Tests;

[TestFixture]
public class TokenBlockUtilsTest
{
    private static readonly (EncodingSequence Sequence, uint Zero, byte Rotation)[] Registered =
    [
        (EncodingSequences.Mercury, 0xCCC749CC, 4),
        (EncodingSequences.Venus, 0x48C74948, 4),
        (EncodingSequences.Earth, 0x18121218, 4),
        (EncodingSequences.Pluto, 0x1F12121F, 4),
        (EncodingSequences.Mars, 0x4EC7494E, 4),
        (EncodingSequences.Jupiter, 0x8C124980, 0),
    ];

    [Test]
    public void Existing_rotation_four_sequences_match_the_independent_family_oracle_through_500()
    {
        foreach (var (sequence, _, _) in Registered.Where(item => item.Rotation == 4))
        {
            for (uint rides = 0; rides <= 500; rides++)
                Assert.That(sequence.Encode(rides).Value, Is.EqualTo(EncodeLegacyFamily(sequence.FriendlyName, rides)),
                    $"{sequence.FriendlyName}/{rides}");
        }
    }

    [TestCase("mercury", 0u, 0xCCC749CCu)]
    [TestCase("mercury", 127u, 0xCCC7363Bu)]
    [TestCase("mercury", 128u, 0x3FC7C9C4u)]
    [TestCase("mercury", 255u, 0x3FC7B633u)]
    [TestCase("mercury", 256u, 0xCCC649DCu)]
    [TestCase("mercury", 383u, 0xCCC6362Bu)]
    [TestCase("mercury", 384u, 0x3FC6C9D4u)]
    [TestCase("mercury", 500u, 0x3FC6BD93u)]
    [TestCase("venus", 127u, 0x48C736BFu)]
    [TestCase("venus", 128u, 0xBBC7C940u)]
    [TestCase("venus", 255u, 0xBBC7B6B7u)]
    [TestCase("venus", 256u, 0x48C64958u)]
    [TestCase("venus", 383u, 0x48C636AFu)]
    [TestCase("venus", 384u, 0xBBC6C950u)]
    [TestCase("venus", 500u, 0xBBC6BD17u)]
    [TestCase("earth", 255u, 0xEB12EDE7u)]
    [TestCase("earth", 256u, 0x18131208u)]
    [TestCase("earth", 383u, 0x18136DFFu)]
    [TestCase("earth", 384u, 0xEB139200u)]
    [TestCase("earth", 500u, 0xEB13E647u)]
    [TestCase("pluto", 255u, 0xEC12EDE0u)]
    [TestCase("pluto", 256u, 0x1F13120Fu)]
    [TestCase("pluto", 383u, 0x1F136DF8u)]
    [TestCase("pluto", 384u, 0xEC139207u)]
    [TestCase("pluto", 500u, 0xEC13E640u)]
    [TestCase("mars", 127u, 0x4EC736B9u)]
    [TestCase("mars", 128u, 0xBDC7C946u)]
    [TestCase("mars", 255u, 0xBDC7B6B1u)]
    [TestCase("mars", 256u, 0x4EC6495Eu)]
    [TestCase("mars", 383u, 0x4EC636A9u)]
    [TestCase("mars", 384u, 0xBDC6C956u)]
    [TestCase("mars", 500u, 0xBDC6BD11u)]
    public void Known_rotation_four_boundaries_are_preserved(string name, uint rides, uint expected)
    {
        Assert.That(EncodingSequences.TryGetByFriendlyName(name, out var sequence), Is.True);
        Assert.That(sequence!.Encode(rides).Value, Is.EqualTo(expected));
    }

    [TestCase(0u, 0x8C124980u)]
    [TestCase(1u, 0x8C124881u)]
    [TestCase(7u, 0x8C124E87u)]
    [TestCase(8u, 0x7F124188u)]
    [TestCase(56u, 0x7F1271B8u)]
    [TestCase(57u, 0x7F1270B9u)]
    [TestCase(127u, 0x7F1236FFu)]
    [TestCase(128u, 0x8C12C900u)]
    [TestCase(238u, 0x7F12A76Eu)]
    [TestCase(240u, 0x8C12B970u)]
    [TestCase(247u, 0x8C12BE77u)]
    [TestCase(255u, 0x7F12B67Fu)]
    [TestCase(256u, 0x8C134981u)]
    [TestCase(261u, 0x8C134C84u)]
    [TestCase(381u, 0x7F1334FCu)]
    [TestCase(384u, 0x8C13C901u)]
    [TestCase(500u, 0x8C13BD75u)]
    public void Jupiter_hardware_observations_are_encoded_and_decoded(uint rides, uint block)
    {
        Assert.That(EncodingSequences.Jupiter.Encode(rides).Value, Is.EqualTo(block));
        Assert.That(EncodingSequences.TryDecode(new T55Block(block), out var sequence, out var decoded), Is.True);
        Assert.That(sequence, Is.EqualTo(EncodingSequences.Jupiter));
        Assert.That(decoded, Is.EqualTo(rides));
    }

    [Test]
    public void Registered_sequences_round_trip_and_have_no_collisions_through_counter_limit()
    {
        foreach (var max in new uint[] { 500, 511 })
        {
            var blocks = new HashSet<uint>();
            foreach (var (sequence, zero, rotation) in Registered)
            {
                var sequenceBlocks = new HashSet<uint>();
                for (uint rides = 0; rides <= max; rides++)
                {
                    var block = EncodeCounter(zero, rotation, rides);
                    Assert.That(sequenceBlocks.Add(block), Is.True, $"self collision {sequence.FriendlyName}/{rides}");
                    Assert.That(blocks.Add(block), Is.True, $"cross collision {sequence.FriendlyName}/{rides}");
                    if (rides <= 500)
                    {
                        Assert.That(sequence.Encode(rides).Value, Is.EqualTo(block));
                        Assert.That(sequence.TryDecode(new T55Block(block), out var decoded), Is.True);
                        Assert.That(decoded, Is.EqualTo(rides));
                    }
                }
            }
        }
    }

    [Test]
    public void Counter_codec_round_trips_every_nine_bit_value_for_rotations_zero_and_four()
    {
        foreach (var rotation in new byte[] { 0, 4 })
        {
            var sequence = new EncodingSequence($"test-{rotation}", new T55Block(0x12345678), rotation, 0, 511);
            for (uint rides = 0; rides <= 511; rides++)
            {
                var block = sequence.Encode(rides);
                Assert.That(sequence.TryDecode(block, out var decoded), Is.True, $"rotation {rotation}, rides {rides}");
                Assert.That(decoded, Is.EqualTo(rides));
            }
        }
    }

    [Test]
    public void Candidate_b_and_c_are_collision_free_but_unregistered()
    {
        var registered = new HashSet<uint>(Registered.SelectMany(sequence => Enumerable.Range(0, 501)
            .Select(rides => sequence.Sequence.Encode((uint)rides).Value)));
        foreach (var (zero, anchor) in new[] { (0x8B1249F0u, 0x781266DFu), (0x891249D0u, 0x7A1222BBu) })
        {
            var candidate = new HashSet<uint>();
            for (uint rides = 0; rides <= 500; rides++)
            {
                var block = EncodeCounter(zero, 0, rides);
                Assert.That(candidate.Add(block), Is.True);
                Assert.That(registered.Contains(block), Is.False);
            }
            Assert.That(TokenBlockUtils.TryDecode(new T55Block(anchor), out _), Is.False);
            Assert.That(EncodingSequences.TryGetSequenceFromBlock(new T55Block(anchor), out _), Is.False);
        }
    }

    [Test]
    public void Decode_requires_full_counter_structure_and_registered_range()
    {
        var valid = EncodingSequences.Jupiter.Encode(8).Value;
        Assert.That(TokenBlockUtils.TryDecode(new T55Block(valid ^ 0xF3000000), out _), Is.False, "wrong F3 toggle");
        Assert.That(TokenBlockUtils.TryDecode(new T55Block(valid ^ 0x00000001), out _), Is.False, "wrong payload");
        Assert.That(TokenBlockUtils.TryDecode(new T55Block(valid ^ 0x00010000), out _), Is.False, "wrong duplicated high bit");
        Assert.That(TokenBlockUtils.TryDecode(new T55Block(EncodeCounter(0x8C124980, 0, 501)), out _), Is.False, "out of app range");
        Assert.That(TokenBlockUtils.TryDecode(new T55Block(0xDEAD1234), out _), Is.False);
    }

    [Test]
    public void Sequence_constructor_validates_rotation_and_counter_range()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodingSequence("bad", new T55Block(0), 8, 0, 1));
        Assert.Throws<ArgumentException>(() => new EncodingSequence("bad", new T55Block(0), 0, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodingSequence("bad", new T55Block(0), 0, 0, 512));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodingSequences.Jupiter.Encode(501));
    }

    [Test]
    public void Sequence_matching_is_structural_not_high_word_based()
    {
        foreach (var rides in new uint[] { 7, 8, 127, 128, 255, 256, 383, 384 })
        {
            var block = EncodingSequences.Jupiter.Encode(rides);
            Assert.That(EncodingSequences.TryDecode(block, out var sequence, out var decoded), Is.True);
            Assert.That(sequence, Is.EqualTo(EncodingSequences.Jupiter));
            Assert.That(decoded, Is.EqualTo(rides));
        }
    }

    private static uint EncodeLegacyFamily(string name, uint rides)
    {
        var (zero, families) = name switch
        {
            "mercury" => (0xCCC749CCu, new[] { (0u, 127u), (128u, 255u), (256u, 383u), (384u, 500u) }),
            "venus" => (0x48C74948u, new[] { (0u, 127u), (128u, 255u), (256u, 383u), (384u, 500u) }),
            "earth" => (0x18121218u, new[] { (0u, 127u), (128u, 255u), (256u, 383u), (384u, 500u) }),
            "pluto" => (0x1F12121Fu, new[] { (0u, 127u), (128u, 255u), (256u, 383u), (384u, 500u) }),
            "mars" => (0x4EC7494Eu, new[] { (0u, 127u), (128u, 255u), (256u, 383u), (384u, 500u) }),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        _ = families.Single(range => rides >= range.Item1 && rides <= range.Item2);
        var constants = name switch
        {
            "mercury" => new[] { (0xCCC7u, 0x0000u), (0x3FC7u, 0x8008u), (0xCCC6u, 0x0010u), (0x3FC6u, 0x8018u) },
            "venus" => new[] { (0x48C7u, 0x0084u), (0xBBC7u, 0x808Cu), (0x48C6u, 0x0094u), (0xBBC6u, 0x809Cu) },
            "earth" => new[] { (0x1812u, 0x5BD4u), (0xEB12u, 0xDBDCu), (0x1813u, 0x5BC4u), (0xEB13u, 0xDBCCu) },
            "pluto" => new[] { (0x1F12u, 0x5BD3u), (0xEC12u, 0xDBDBu), (0x1F13u, 0x5BC3u), (0xEC13u, 0xDBCBu) },
            _ => new[] { (0x4EC7u, 0x0082u), (0xBDC7u, 0x808Au), (0x4EC6u, 0x0092u), (0xBDC6u, 0x809Au) },
        };
        var index = (int)(rides / 128);
        var m = rides - (uint)(index * 128);
        var group = m >> 4;
        var offset = m & 0xF;
        var legacyLow = ((((group + 4) & 7) << 12) | ((offset ^ 9) << 8) | ((offset ^ 12) << 4) | (group + (group < 4 ? 12u : 4u)));
        return (constants[index].Item1 << 16) | (legacyLow ^ constants[index].Item2);
    }

    private static uint EncodeCounter(uint zero, byte rotation, uint rides)
    {
        var r = rides & 0xFF;
        var h = rides >> 8;
        var payload = rotation == 0 ? r : ((r << rotation) | (r >> (8 - rotation))) & 0xFF;
        payload ^= h << rotation;
        var delta = ((payload & 8) != 0 ? 0xF3000000u : 0) | (h << 16) | (r << 8) | payload;
        return zero ^ delta;
    }
}
