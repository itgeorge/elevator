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
    public const string Table0To127 =
        """
        127 CCC7363B
        126 CCC7372B
        125 CCC7341B
        124 CCC7350B
        123 CCC7327B
        122 CCC7336B
        121 CCC7305B
        120 CCC7314B
        119 CCC73EBB
        118 CCC73FAB
        117 CCC73C9B
        116 CCC73D8B
        115 CCC73AFB
        114 CCC73BEB
        113 CCC738DB
        112 CCC739CB
        111 CCC7263A
        110 CCC7272A
        109 CCC7241A
        108 CCC7250A
        107 CCC7227A
        106 CCC7236A
        105 CCC7205A
        104 CCC7214A
        103 CCC72EBA
        102 CCC72FAA
        101 CCC72C9A
        100 CCC72D8A
        99 CCC72AFA
        98 CCC72BEA
        97 CCC728DA
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

    const string Table128To255 =
        """
        255 3FC7B633
        254 3FC7B723
        253 3FC7B413
        252 3FC7B503
        251 3FC7B273
        250 3FC7B363
        249 3FC7B053
        248 3FC7B143
        247 3FC7BEB3
        246 3FC7BFA3
        245 3FC7BC93
        244 3FC7BD83
        243 3FC7BAF3
        242 3FC7BBE3
        241 3FC7B8D3
        240 3FC7B9C3
        239 3FC7A632
        238 3FC7A722
        237 3FC7A412
        236 3FC7A502
        235 3FC7A272
        234 3FC7A362
        233 3FC7A052
        232 3FC7A142
        231 3FC7AEB2
        230 3FC7AFA2
        229 3FC7AC92
        228 3FC7AD82
        227 3FC7AAF2
        226 3FC7ABE2
        225 3FC7A8D2
        224 3FC7A9C2
        223 3FC79631
        222 3FC79721
        221 3FC79411
        219 3FC79271
        218 3FC79361
        217 3FC79051
        215 3FC79EB1
        214 3FC79FA1
        213 3FC79C91
        212 3FC79D81
        211 3FC79AF1
        210 3FC79BE1
        209 3FC798D1
        208 3FC799C1
        207 3FC78630
        206 3FC78720
        205 3FC78410
        204 3FC78500
        203 3FC78270
        202 3FC78360
        201 3FC78050
        200 3FC78140
        199 3FC78EB0
        198 3FC78FA0
        197 3FC78C90
        196 3FC78D80
        195 3FC78AF0
        194 3FC78BE0
        193 3FC788D0
        192 3FC789C0
        191 3FC7F637
        190 3FC7F727
        189 3FC7F417
        188 3FC7F507
        187 3FC7F277
        186 3FC7F367
        185 3FC7F057
        184 3FC7F147
        183 3FC7FEB7
        182 3FC7FFA7
        181 3FC7FC97
        180 3FC7FD87
        179 3FC7FAF7
        178 3FC7FBE7
        177 3FC7F8D7
        176 3FC7F9C7
        175 3FC7E636
        174 3FC7E726
        173 3FC7E416
        172 3FC7E506
        171 3FC7E276
        170 3FC7E366
        169 3FC7E056
        168 3FC7E146
        167 3FC7EEB6
        166 3FC7EFA6
        165 3FC7EC96
        164 3FC7ED86
        163 3FC7EAF6
        162 3FC7EBE6
        161 3FC7E8D6
        160 3FC7E9C6
        159 3FC7D635
        158 3FC7D725
        157 3FC7D415
        156 3FC7D505
        155 3FC7D275
        154 3FC7D365
        153 3FC7D055
        152 3FC7D145
        151 3FC7DEB5
        150 3FC7DFA5
        148 3FC7DD85
        147 3FC7DAF5
        146 3FC7DBE5
        145 3FC7D8D5
        144 3FC7D9C5
        143 3FC7C634
        142 3FC7C724
        141 3FC7C414
        140 3FC7C504
        139 3FC7C274
        138 3FC7C364
        137 3FC7C054
        136 3FC7C144
        135 3FC7CEB4
        134 3FC7CFA4
        133 3FC7CC94
        132 3FC7CD84
        131 3FC7CAF4
        130 3FC7CBE4
        129 3FC7C8D4
        128 3FC7C9C4
        """;

    const string Table256To383 =
        """
        383 CCC6362B
        382 CCC6373B
        381 CCC6340B
        380 CCC6351B
        379 CCC6326B
        378 CCC6337B
        377 CCC6304B
        376 CCC6315B
        375 CCC63EAB
        374 CCC63FBB
        373 CCC63C8B
        372 CCC63D9B
        371 CCC63AEB
        370 CCC63BFB
        369 CCC638CB
        368 CCC639DB
        367 CCC6262A
        366 CCC6273A
        365 CCC6240A
        364 CCC6251A
        363 CCC6226A
        362 CCC6237A
        361 CCC6204A
        360 CCC6215A
        359 CCC62EAA
        358 CCC62FBA
        357 CCC62C8A
        356 CCC62D9A
        355 CCC62AEA
        354 CCC62BFA
        353 CCC628CA
        352 CCC629DA
        351 CCC61629
        350 CCC61739
        349 CCC61409
        348 CCC61519
        347 CCC61269
        346 CCC61379
        345 CCC61049
        344 CCC61159
        343 CCC61EA9
        342 CCC61FB9
        341 CCC61C89
        340 CCC61D99
        339 CCC61AE9
        338 CCC61BF9
        337 CCC618C9
        336 CCC619D9
        335 CCC60628
        334 CCC60738
        333 CCC60408
        332 CCC60518
        331 CCC60268
        330 CCC60378
        329 CCC60048
        328 CCC60158
        327 CCC60EA8
        326 CCC60FB8
        325 CCC60C88
        324 CCC60D98
        323 CCC60AE8
        322 CCC60BF8
        321 CCC608C8
        320 CCC609D8
        319 CCC6762F
        318 CCC6773F
        317 CCC6740F
        316 CCC6751F
        315 CCC6726F
        314 CCC6737F
        313 CCC6704F
        312 CCC6715F
        311 CCC67EAF
        310 CCC67FBF
        309 CCC67C8F
        308 CCC67D9F
        307 CCC67AEF
        306 CCC67BFF
        305 CCC678CF
        304 CCC679DF
        303 CCC6662E
        302 CCC6673E
        301 CCC6640E
        300 CCC6651E
        299 CCC6626E
        298 CCC6637E
        297 CCC6604E
        296 CCC6615E
        295 CCC66EAE
        294 CCC66FBE
        293 CCC66C8E
        292 CCC66D9E
        291 CCC66AEE
        290 CCC66BFE
        289 CCC668CE
        288 CCC669DE
        287 CCC6562D
        286 CCC6573D
        285 CCC6540D
        284 CCC6551D
        283 CCC6526D
        282 CCC6537D
        281 CCC6504D
        280 CCC6515D
        279 CCC65EAD
        278 CCC65FBD
        277 CCC65C8D
        276 CCC65D9D
        275 CCC65AED
        274 CCC65BFD
        273 CCC658CD
        272 CCC659DD
        271 CCC6462C
        270 CCC6473C
        269 CCC6440C
        268 CCC6451C
        267 CCC6426C
        266 CCC6437C
        265 CCC6404C
        264 CCC6415C
        263 CCC64EAC
        262 CCC64FBC
        261 CCC64C8C
        260 CCC64D9C
        259 CCC64AEC
        258 CCC64BFC
        257 CCC648CC
        256 CCC649DC
        """;

    const string Table384To500 =
        """
        500 3FC6BD93 
        499 3FC6BAE3 
        498 3FC6BBF3 
        497 3FC6B8C3 
        496 3FC6B9D3 
        495 3FC6A622
        494 3FC6A732 
        493 3FC6A402 
        492 3FC6A512
        491 3FC6A262 
        490 3FC6A372
        489 3FC6A042
        488 3FC6A152
        487 3FC6AEA2
        486 3FC6AFB2
        485 3FC6AC82
        484 3FC6AD92
        483 3FC6AAE2
        482 3FC6ABF2
        481 3FC6A8C2
        480 3FC6A9D2
        479 3FC69621
        478 3FC69731
        477 3FC69401
        476 3FC69511
        475 3FC69261
        474 3FC69371
        473 3FC69041
        472 3FC69151
        471 3FC69EA1
        470 3FC69FB1
        469 3FC69C81
        468 3FC69D91
        467 3FC69AE1
        466 3FC69BF1
        465 3FC698C1
        464 3FC699D1
        463 3FC68620
        462 3FC68730
        461 3FC68400
        460 3FC68510
        459 3FC68260
        458 3FC68370
        457 3FC68040
        456 3FC68150
        455 3FC68EA0
        454 3FC68FB0
        453 3FC68C80
        452 3FC68D90
        451 3FC68AE0
        450 3FC68BF0
        449 3FC688C0
        448 3FC689D0
        447 3FC6F627
        446 3FC6F737
        445 3FC6F407
        444 3FC6F517
        443 3FC6F267
        442 3FC6F377
        441 3FC6F047
        440 3FC6F157
        439 3FC6FEA7
        438 3FC6FFB7
        437 3FC6FC87
        436 3FC6FD97
        435 3FC6FAE7
        434 3FC6FBF7
        433 3FC6F8C7
        432 3FC6F9D7
        431 3FC6E626
        430 3FC6E736
        429 3FC6E406
        428 3FC6E516
        427 3FC6E266
        426 3FC6E376
        425 3FC6E046
        424 3FC6E156
        423 3FC6EEA6
        422 3FC6EFB6
        421 3FC6EC86
        420 3FC6ED96
        419 3FC6EAE6
        418 3FC6EBF6
        417 3FC6E8C6
        416 3FC6E9D6
        415 3FC6D625
        414 3FC6D735
        413 3FC6D405
        412 3FC6D515
        411 3FC6D265
        410 3FC6D375
        409 3FC6D045
        408 3FC6D155
        407 3FC6DEA5
        406 3FC6DFB5
        405 3FC6DC85
        404 3FC6DD95
        403 3FC6DAE5
        402 3FC6DBF5
        401 3FC6D8C5
        400 3FC6D9D5
        399 3FC6C624
        398 3FC6C734
        397 3FC6C404
        396 3FC6C514
        395 3FC6C264
        394 3FC6C374
        393 3FC6C044
        392 3FC6C154
        391 3FC6CEA4
        390 3FC6CFB4
        389 3FC6CC84
        388 3FC6CD94
        387 3FC6CAE4
        386 3FC6CBF4
        385 3FC6C8C4
        384 3FC6C9D4
        """;

    [Test]
    [TestCase(Table0To127, (uint)0xCCC7, (uint)0x0000)]
    [TestCase(Table128To255, (uint)0x3FC7, (uint)0x8008)]
    [TestCase(Table256To383, (uint)0xCCC6, (uint)0x0010)]
    [TestCase(Table384To500, (uint)0x3FC6, (uint)0x8018)]
    public void EncodeByFamily_MatchesKnownValues(string countAndBlockTable, uint high16, uint xorConst)
    {
        var rows = ParseTable(countAndBlockTable);

        foreach (var (v, expected) in rows)
        {
            uint got = TokenBlockUtils.EncodeByFamily((uint)v, new TokenBlockUtils.Family(high16, xorConst));
            Assert.That(got, Is.EqualTo(expected), $"Value {v}: expected {expected:X8}, got {got:X8}");
        }
    }

    [Test]
    public void Encode_MatchesKnownValues()
    {
        var rows = ParseTable(string.Join("\n", Table0To127, Table128To255, Table256To383, Table384To500));

        foreach (var (ridesRemaining, block) in rows)
        {
            uint got = TokenBlockUtils.Encode((uint)ridesRemaining);
            Assert.That(got, Is.EqualTo(block), $"Rides remaining {ridesRemaining}: expected {block:X8}, got {got:X8}");
        }
    }

    [Test]
    public void Decode_MatchesKnownValues()
    {
        var rows = ParseTable(string.Join("\n", Table0To127, Table128To255, Table256To383, Table384To500));

        foreach (var (ridesRemaining, block) in rows)
        {
            uint got = TokenBlockUtils.Decode(block);
            Assert.That(got, Is.EqualTo(ridesRemaining), $"Block {block:X8}: expected {ridesRemaining}, got {got}");
        }
    }

    [Test]
    public void EncodeDecode_RoundTrip_AllValues()
    {
        for (uint value = 0; value <= 500; value++)
        {
            uint encoded = TokenBlockUtils.Encode(value);
            uint decoded = TokenBlockUtils.Decode(encoded);
            Assert.That(decoded, Is.EqualTo(value),
                $"Round-trip failed for value {value}: encoded {encoded:X8}, decoded {decoded}");
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