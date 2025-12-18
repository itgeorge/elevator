using ElevatorCli;
using JetBrains.Annotations;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ElevatorCli.Tests;

[TestFixture]
[TestOf(typeof(TokenBlockUtils))]
public class TokenBlockUtilsTest
{
    // Your table: "value block"
    public const string Table = 
        """
        96  CCC729CA
        95  CCC71639
        94  CCC71729
        93  CCC71419
        92  CCC71509
        91  CCC71279
        90  CCC71369
        89  CCC71059
        88  CCC71149
        87  CCC71EB9
        86  CCC71FA9
        85  CCC71C99
        84  CCC71D89
        83  CCC71AF9
        82  CCC71BE9
        81  CCC718D9
        80  CCC719C9
        79  CCC70638
        78  CCC70728
        77  CCC70418
        76  CCC70508
        75  CCC70278
        74  CCC70368
        73  CCC70058
        72  CCC70148
        71  CCC70EB8
        70  CCC70FA8
        69  CCC70C98
        68  CCC70D88
        67  CCC70AF8
        66  CCC70BE8
        65  CCC708D8
        64  CCC709C8
        63  CCC7763F
        62  CCC7772F
        61  CCC7741F
        60  CCC7750F
        59  CCC7727F
        58  CCC7736F
        57  CCC7705F
        56  CCC7714F
        55  CCC77EBF
        54  CCC77FAF
        53  CCC77C9F
        52  CCC77D8F
        51  CCC77AFF
        50  CCC77BEF
        49  CCC778DF
        48  CCC779CF
        47  CCC7663E
        46  CCC7672E
        45  CCC7641E
        44  CCC7650E
        43  CCC7627E
        42  CCC7636E
        41  CCC7605E
        40  CCC7614E
        39  CCC76EBE
        38  CCC76FAE
        37  CCC76C9E
        36  CCC76D8E
        35  CCC76AFE
        34  CCC76BEE
        33  CCC768DE
        32  CCC769CE
        31  CCC7563D
        30  CCC7572D
        29  CCC7541D
        28  CCC7550D
        27  CCC7527D
        26  CCC7536D
        25  CCC7505D
        24  CCC7514D
        23  CCC75EBD
        22  CCC75FAD
        21  CCC75C9D
        20  CCC75D8D
        19  CCC75AFD
        18  CCC75BED
        17  CCC758DD
        16  CCC759CD
        15  CCC7463C
        14  CCC7472C
        13  CCC7441C
        12  CCC7450C
        11  CCC7427C
        10  CCC7436C
        9   CCC7405C
        8   CCC7414C
        7   CCC74EBC
        6   CCC74FAC
        5   CCC74C9C
        4   CCC74D8C
        3   CCC74AFC
        2   CCC74BEC
        1   CCC748DC
        0   CCC749CC
        """;
    
    [Test]
    public void EncodeForBlock_MatchesKnownValues()
    {
        var rows = ParseTable(Table);

        foreach (var (v, expected) in rows)
        {
            uint got = TokenBlockUtils.EncodeForBlock((uint)v);
            Assert.That(got, Is.EqualTo(expected), $"Value {v}: expected {expected:X8}, got {got:X8}");
        }
    }

    [Test]
    public void DecodeFromBlock_MatchesKnownValues()
    {
        var rows = ParseTable(Table);

        foreach (var (expected, block) in rows)
        {
            uint got = TokenBlockUtils.DecodeFromBlock(block);
            Assert.That(got, Is.EqualTo((uint)expected), $"Block {block:X8}: expected {expected}, got {got}");
        }
    }

    [Test]
    public void EncodeDecode_RoundTrip_AllValues()
    {
        for (uint value = 0; value <= 96; value++)
        {
            uint encoded = TokenBlockUtils.EncodeForBlock(value);
            uint decoded = TokenBlockUtils.DecodeFromBlock(encoded);
            Assert.That(decoded, Is.EqualTo(value), $"Round-trip failed for value {value}: encoded {encoded:X8}, decoded {decoded}");
        }
    }
    
    static List<(int v, uint expected)> ParseTable(string table)
    {
        var rows = new List<(int v, uint expected)>();

        var lines = table.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            // Split on whitespace
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                throw new FormatException($"Could not parse value from: '{line}'");

            // hex block word
            if (!uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint expected))
                throw new FormatException($"Could not parse hex block from: '{line}'");

            rows.Add((v, expected));
        }

        return rows;
    }
}