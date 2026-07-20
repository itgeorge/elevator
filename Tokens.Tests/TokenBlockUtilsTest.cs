using JetBrains.Annotations;
using Tokens;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tokens.Tests;

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

    const string Table43FE_0To127 =
        """
        127 48C736BF
        126 48C737AF
        125 48C7349F
        124 48C7358F
        123 48C732FF
        122 48C733EF
        121 48C730DF
        120 48C731CF
        119 48C73E3F
        118 48C73F2F
        117 48C73C1F
        116 48C73D0F
        115 48C73A7F
        114 48C73B6F
        113 48C7385F
        112 48C7394F
        111 48C726BE
        110 48C727AE
        109 48C7249E
        108 48C7258E
        107 48C722FE
        106 48C723EE
        105 48C720DE
        104 48C721CE
        103 48C72E3E
        102 48C72F2E
        101 48C72C1E
        100 48C72D0E
         99 48C72A7E
         98 48C72B6E
         97 48C7285E
         96 48C7294E
         95 48C716BD
         94 48C717AD
         93 48C7149D
         92 48C7158D
         91 48C712FD
         90 48C713ED
         89 48C710DD
         88 48C711CD
         87 48C71E3D
         86 48C71F2D
         85 48C71C1D
         84 48C71D0D
         83 48C71A7D
         82 48C71B6D
         81 48C7185D
         80 48C7194D
         79 48C706BC
         78 48C707AC
         77 48C7049C
         76 48C7058C
         75 48C702FC
         74 48C703EC
         73 48C700DC
         72 48C701CC
         71 48C70E3C
         70 48C70F2C
         69 48C70C1C
         68 48C70D0C
         67 48C70A7C
         66 48C70B6C
         65 48C7085C
         64 48C7094C
         63 48C776BB
         62 48C777AB
         61 48C7749B
         60 48C7758B
         59 48C772FB
         58 48C773EB
         57 48C770DB
         56 48C771CB
         55 48C77E3B
         54 48C77F2B
         53 48C77C1B
         52 48C77D0B
         51 48C77A7B
         50 48C77B6B
         49 48C7785B
         48 48C7794B
         47 48C766BA
         46 48C767AA
         45 48C7649A
         44 48C7658A
         43 48C762FA
         42 48C763EA
         41 48C760DA
         40 48C761CA
         39 48C76E3A
         38 48C76F2A
         37 48C76C1A
         36 48C76D0A
         35 48C76A7A
         34 48C76B6A
         33 48C7685A
         32 48C7694A
         31 48C756B9
         30 48C757A9
         29 48C75499
         28 48C75589
         27 48C752F9
         26 48C753E9
         25 48C750D9
         24 48C751C9
         23 48C75E39
         22 48C75F29
         21 48C75C19
         20 48C75D09
         19 48C75A79
         18 48C75B69
         17 48C75859
         16 48C75949
         15 48C746B8
         14 48C747A8
         13 48C74498
         12 48C74588
         11 48C742F8
         10 48C743E8
          9 48C740D8
          8 48C741C8
          7 48C74E38
          6 48C74F28
          5 48C74C18
          4 48C74D08
          3 48C74A78
          2 48C74B68
          1 48C74858
          0 48C74948
        """;

    const string Table43FE_128To180 =
        """
        180 BBC7FD03
        179 BBC7FA73
        178 BBC7FB63
        177 BBC7F853
        176 BBC7F943
        175 BBC7E6B2
        174 BBC7E7A2
        173 BBC7E492
        172 BBC7E582
        171 BBC7E2F2
        170 BBC7E3E2
        169 BBC7E0D2
        168 BBC7E1C2
        167 BBC7EE32
        166 BBC7EF22
        165 BBC7EC12
        164 BBC7ED02
        163 BBC7EA72
        162 BBC7EB62
        161 BBC7E852
        160 BBC7E942
        159 BBC7D6B1
        158 BBC7D7A1
        157 BBC7D491
        156 BBC7D581
        155 BBC7D2F1
        154 BBC7D3E1
        153 BBC7D0D1
        152 BBC7D1C1
        151 BBC7DE31
        150 BBC7DF21
        149 BBC7DC11
        148 BBC7DD01
        147 BBC7DA71
        146 BBC7DB61
        145 BBC7D851
        144 BBC7D941
        143 BBC7C6B0
        142 BBC7C7A0
        141 BBC7C490
        140 BBC7C580
        139 BBC7C2F0
        138 BBC7C3E0
        137 BBC7C0D0
        136 BBC7C1C0
        135 BBC7CE30
        134 BBC7CF20
        133 BBC7CC10
        132 BBC7CD00
        131 BBC7CA70
        130 BBC7CB60
        129 BBC7C850
        128 BBC7C940
        """;

    const string Table43FE_181To255_Predicted =
        """
        255 BBC7B6B7
        254 BBC7B7A7
        253 BBC7B497
        252 BBC7B587
        251 BBC7B2F7
        250 BBC7B3E7
        249 BBC7B0D7
        248 BBC7B1C7
        247 BBC7BE37
        246 BBC7BF27
        245 BBC7BC17
        244 BBC7BD07
        243 BBC7BA77
        242 BBC7BB67
        241 BBC7B857
        240 BBC7B947
        239 BBC7A6B6
        238 BBC7A7A6
        237 BBC7A496
        236 BBC7A586
        235 BBC7A2F6
        234 BBC7A3E6
        233 BBC7A0D6
        232 BBC7A1C6
        231 BBC7AE36
        230 BBC7AF26
        229 BBC7AC16
        228 BBC7AD06
        227 BBC7AA76
        226 BBC7AB66
        225 BBC7A856
        224 BBC7A946
        223 BBC796B5
        222 BBC797A5
        221 BBC79495
        220 BBC79585
        219 BBC792F5
        218 BBC793E5
        217 BBC790D5
        216 BBC791C5
        215 BBC79E35
        214 BBC79F25
        213 BBC79C15
        212 BBC79D05
        211 BBC79A75
        210 BBC79B65
        209 BBC79855
        208 BBC79945
        207 BBC786B4
        206 BBC787A4
        205 BBC78494
        204 BBC78584
        203 BBC782F4
        202 BBC783E4
        201 BBC780D4
        200 BBC781C4
        199 BBC78E34
        198 BBC78F24
        197 BBC78C14
        196 BBC78D04
        195 BBC78A74
        194 BBC78B64
        193 BBC78854
        192 BBC78944
        191 BBC7F6B3
        190 BBC7F7A3
        189 BBC7F493
        188 BBC7F583
        187 BBC7F2F3
        186 BBC7F3E3
        185 BBC7F0D3
        184 BBC7F1C3
        183 BBC7FE33
        182 BBC7FF23
        181 BBC7FC13
        """;

    const string TableD3FE_0To23 =
        """
        23 18120569
        22 18120479
        21 18120749
        20 18120659
        19 18120129
        18 18120039
        17 18120309
        16 18120219
        15 18121DE8
        14 18121CF8
        13 18121FC8
        12 18121ED8
        11 181219A8
        10 181218B8
         9 18121B88
         8 18121A98
         7 18121568
         6 18121478
         5 18121748
         4 18121658
         3 18121128
         2 18121038
         1 18121308
         0 18121218
        """;

    const string TableD3FE_128To255_ValidatedBoundaries =
        """
        255 EB12EDE7
        254 EB12ECF7
        128 EB129210
        """;



    [Test]
    public void EncodeByFamily_throws_when_value_below_family_range()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TokenBlockUtils.EncodeByFamily(0, TokenBlockUtils.Families.FamilyBBC7_128To255));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TokenBlockUtils.EncodeByFamily(127, TokenBlockUtils.Families.FamilyBBC7_128To255));
    }

    [Test]
    public void EncodeByFamily_throws_when_value_above_family_range()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TokenBlockUtils.EncodeByFamily(128, TokenBlockUtils.Families.Family0To127));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TokenBlockUtils.EncodeByFamily(256, TokenBlockUtils.Families.FamilyBBC7_128To255));
    }

    [Test]
    [TestCase(Table0To127, (uint)0xCCC7, (uint)0x0000, (uint)0)]
    [TestCase(Table128To255, (uint)0x3FC7, (uint)0x8008, (uint)128)]
    [TestCase(Table256To383, (uint)0xCCC6, (uint)0x0010, (uint)256)]
    [TestCase(Table384To500, (uint)0x3FC6, (uint)0x8018, (uint)384)]
    [TestCase(Table43FE_0To127, (uint)0x48C7, (uint)0x0084, (uint)0)]
    [TestCase(Table43FE_128To180, (uint)0xBBC7, (uint)0x808C, (uint)128)]
    [TestCase(TableD3FE_0To23, (uint)0x1812, (uint)0x5BD4, (uint)0)]
    [TestCase(TableD3FE_128To255_ValidatedBoundaries, (uint)0xEB12, (uint)0xDBDC, (uint)128)]
    public void EncodeByFamily_MatchesKnownValues(string countAndBlockTable, uint high16, uint xorConst, uint baseOffset)
    {
        var rows = ParseTable(countAndBlockTable);

        foreach (var (v, expected) in rows)
        {
            var got = TokenBlockUtils.EncodeByFamily((uint)v, new TokenBlockUtils.Family(high16, xorConst, baseOffset));
            Assert.That(got.Value, Is.EqualTo(expected), $"Value {v}: expected {expected:X8}, got {got.Value:X8}");
        }
    }

    [Test]
    public void Encode_MercurySequence_MatchesKnownValues()
    {
        var rows = ParseTable(string.Join("\n", Table0To127, Table128To255, Table256To383, Table384To500));

        foreach (var (ridesRemaining, block) in rows)
        {
            var got = TokenBlockUtils.Encode((uint)ridesRemaining, EncodingSequences.Mercury);
            Assert.That(got.Value, Is.EqualTo(block), $"Rides remaining {ridesRemaining}: expected {block:X8}, got {got.Value:X8}");
        }
    }

    [Test]
    public void Decode_MatchesKnownValues()
    {
        var rows = ParseTable(string.Join("\n", Table0To127, Table128To255, Table256To383, Table384To500));

        foreach (var (ridesRemaining, block) in rows)
        {
            uint got = TokenBlockUtils.Decode(new T55Block(block));
            Assert.That(got, Is.EqualTo(ridesRemaining), $"Block {block:X8}: expected {ridesRemaining}, got {got}");
        }
    }

    [Test]
    public void TryGetFamilyFromBlock_ReturnsFalse_ForUnknownHigh16()
    {
        var ok = TokenBlockUtils.Families.TryGetFamilyFromBlock(new T55Block(0xDEAD1234), out var family);

        Assert.That(ok, Is.False);
        Assert.That(family, Is.Null);
    }

    [Test]
    public void TryDecode_valid_block_returns_true()
    {
        var block = EncodingSequences.Mercury.Encode(73);

        Assert.That(TokenBlockUtils.TryDecode(block, out var rides), Is.True);
        Assert.That(rides, Is.EqualTo(73u));
    }

    [Test]
    public void TryDecode_unknown_family_returns_false()
    {
        var block = new T55Block(0xDEAD1234);

        Assert.That(TokenBlockUtils.TryDecode(block, out _), Is.False);
    }

    [Test]
    public void TryDecode_invalid_payload_returns_false()
    {
        var block = new T55Block(0xCCC70000);

        Assert.That(TokenBlockUtils.TryDecode(block, out _), Is.False);
    }

    [Test]
    public void EncodeDecode_RoundTrip_AllValues()
    {
        for (uint value = 0; value <= 500; value++)
        {
            var encoded = TokenBlockUtils.Encode(value, EncodingSequences.Mercury);
            uint decoded = TokenBlockUtils.Decode(encoded);
            Assert.That(decoded, Is.EqualTo(value),
                $"Round-trip failed for value {value}: encoded {encoded.Value:X8}, decoded {decoded}");
        }
    }

    [Test]
    public void EncodingSequences_expose_reset_image_file_names()
    {
        Assert.That(EncodingSequences.Mercury.ResetImageFileName, Is.EqualTo("default-500-rides.bin"));
        Assert.That(EncodingSequences.Venus.ResetImageFileName, Is.EqualTo("venus-0-rides.bin"));
        Assert.That(EncodingSequences.Earth.ResetImageFileName, Is.EqualTo("earth-0-rides.bin"));
        Assert.That(EncodingSequences.Mars.ResetImageFileName, Is.EqualTo("mars-0-rides.bin"));
    }

    [Test]
    public void EncodingSequences_expose_supported_ride_ranges()
    {
        Assert.That(EncodingSequences.Mercury.MinRides, Is.EqualTo(0u));
        Assert.That(EncodingSequences.Mercury.MaxRides, Is.EqualTo(500u));
        Assert.That(EncodingSequences.Venus.MinRides, Is.EqualTo(0u));
        Assert.That(EncodingSequences.Venus.MaxRides, Is.EqualTo(500u));
        Assert.That(EncodingSequences.Earth.MinRides, Is.EqualTo(0u));
        Assert.That(EncodingSequences.Earth.MaxRides, Is.EqualTo(255u));
        Assert.That(EncodingSequences.Mars.MinRides, Is.EqualTo(0u));
        Assert.That(EncodingSequences.Mars.MaxRides, Is.EqualTo(500u));
    }

    [Test]
    public void EncodingSequence_single_segment_supports_partially_known_range()
    {
        var sequence = new EncodingSequence(
            "d3-partial",
            "d3-partial.bin",
            new EncodingSequenceSegment(0, 23, TokenBlockUtils.Families.Family48C7_0To127));

        Assert.That(sequence.Segments, Has.Count.EqualTo(1));
        Assert.That(sequence.GetFamilyForRides(0), Is.EqualTo(TokenBlockUtils.Families.Family48C7_0To127));
        Assert.That(sequence.GetFamilyForRides(23), Is.EqualTo(TokenBlockUtils.Families.Family48C7_0To127));
        Assert.That(sequence.Encode(10).Value, Is.EqualTo(TokenBlockUtils.EncodeByFamily(10, TokenBlockUtils.Families.Family48C7_0To127).Value));
    }

    [Test]
    public void EncodingSequence_throws_when_no_segment_covers_rides()
    {
        var sequence = new EncodingSequence(
            "d3-partial",
            "d3-partial.bin",
            new EncodingSequenceSegment(0, 23, TokenBlockUtils.Families.Family48C7_0To127));

        Assert.Throws<ArgumentException>(() => sequence.GetFamilyForRides(24));
        Assert.Throws<ArgumentException>(() => sequence.Encode(24));
    }

    [Test]
    public void EncodingSequence_constructor_requires_at_least_one_segment()
    {
        Assert.Throws<ArgumentException>(() => new EncodingSequence("empty", "empty.bin"));
    }

    [Test]
    public void GetFamilyForRides_Venus_sequence_uses_48C7_for_low_range_and_BBC7_for_high_range()
    {
        var sequence = EncodingSequences.Venus;

        Assert.That(sequence.GetFamilyForRides(0), Is.EqualTo(TokenBlockUtils.Families.Family48C7_0To127));
        Assert.That(sequence.GetFamilyForRides(127), Is.EqualTo(TokenBlockUtils.Families.Family48C7_0To127));
        Assert.That(sequence.GetFamilyForRides(128), Is.EqualTo(TokenBlockUtils.Families.FamilyBBC7_128To255));
        Assert.That(sequence.GetFamilyForRides(255), Is.EqualTo(TokenBlockUtils.Families.FamilyBBC7_128To255));
        Assert.That(sequence.GetFamilyForRides(256), Is.EqualTo(TokenBlockUtils.Families.Family48C6_256To383));
        Assert.That(sequence.GetFamilyForRides(383), Is.EqualTo(TokenBlockUtils.Families.Family48C6_256To383));
        Assert.That(sequence.GetFamilyForRides(384), Is.EqualTo(TokenBlockUtils.Families.FamilyBBC6_384To500));
        Assert.That(sequence.GetFamilyForRides(500), Is.EqualTo(TokenBlockUtils.Families.FamilyBBC6_384To500));
    }

    [Test]
    public void GetFamilyForRides_Mars_sequence_uses_confirmed_C3_families()
    {
        var sequence = EncodingSequences.Mars;

        Assert.That(sequence.GetFamilyForRides(0), Is.EqualTo(TokenBlockUtils.Families.Family4EC7_0To127));
        Assert.That(sequence.GetFamilyForRides(127), Is.EqualTo(TokenBlockUtils.Families.Family4EC7_0To127));
        Assert.That(sequence.GetFamilyForRides(128), Is.EqualTo(TokenBlockUtils.Families.FamilyBDC7_128To255));
        Assert.That(sequence.GetFamilyForRides(255), Is.EqualTo(TokenBlockUtils.Families.FamilyBDC7_128To255));
        Assert.That(sequence.GetFamilyForRides(256), Is.EqualTo(TokenBlockUtils.Families.Family4EC6_256To383));
        Assert.That(sequence.GetFamilyForRides(383), Is.EqualTo(TokenBlockUtils.Families.Family4EC6_256To383));
        Assert.That(sequence.GetFamilyForRides(384), Is.EqualTo(TokenBlockUtils.Families.FamilyBDC6_384To500));
        Assert.That(sequence.GetFamilyForRides(500), Is.EqualTo(TokenBlockUtils.Families.FamilyBDC6_384To500));
    }

    [Test]
    public void Families_registry_contains_all_sequence_families_with_unique_high16()
    {
        var familiesByHigh16 = new Dictionary<uint, TokenBlockUtils.Family>();
        foreach (var sequence in EncodingSequences.All)
        {
            foreach (var segment in sequence.Segments)
            {
                Assert.That(
                    TokenBlockUtils.Families.All,
                    Does.Contain(segment.Family),
                    $"Sequence '{sequence.FriendlyName}' segment family 0x{segment.Family.High16:X4} missing from Families.All");

                Assert.That(
                    familiesByHigh16.TryAdd(segment.Family.High16, segment.Family),
                    Is.True,
                    $"Duplicate high16 0x{segment.Family.High16:X4} across encoding sequences");

                Assert.That(
                    TokenBlockUtils.Families.TryGetFamilyFromBlock(
                        new T55Block(segment.Family.High16 << 16),
                        out var found),
                    Is.True);
                Assert.That(found, Is.EqualTo(segment.Family));
            }
        }
    }

    [TestCase(0u, 0x4EC7494Eu)]
    [TestCase(1u, 0x4EC7485Eu)]
    [TestCase(13u, 0x4EC7449Eu)]
    [TestCase(14u, 0x4EC747AEu)]
    [TestCase(127u, 0x4EC736B9u)]
    [TestCase(128u, 0xBDC7C946u)]
    [TestCase(255u, 0xBDC7B6B1u)]
    [TestCase(256u, 0x4EC6495Eu)]
    [TestCase(383u, 0x4EC636A9u)]
    [TestCase(384u, 0xBDC6C956u)]
    [TestCase(499u, 0xBDC6BA61u)]
    [TestCase(500u, 0xBDC6BD11u)]
    public void Encode_MarsSequence_MatchesCapturedAndElevatorValidatedBlocks(uint rides, uint expectedBlock)
    {
        var got = EncodingSequences.Mars.Encode(rides);
        Assert.That(got.Value, Is.EqualTo(expectedBlock), $"Rides {rides}: expected {expectedBlock:X8}, got {got.Value:X8}");
        Assert.That(TokenBlockUtils.Decode(got), Is.EqualTo(rides));
    }

    [TestCase(127u, 0x48C736BFu)]
    [TestCase(128u, 0xBBC7C940u)]
    [TestCase(255u, 0xBBC7B6B7u)]
    [TestCase(256u, 0x48C64958u)]
    [TestCase(383u, 0x48C636AFu)]
    [TestCase(384u, 0xBBC6C950u)]
    [TestCase(499u, 0xBBC6BA67u)]
    [TestCase(500u, 0xBBC6BD17u)]
    public void Encode_VenusSequence_MatchesElevatorValidatedBlocks(uint rides, uint expectedBlock)
    {
        var got = EncodingSequences.Venus.Encode(rides);
        Assert.That(got.Value, Is.EqualTo(expectedBlock), $"Rides {rides}: expected {expectedBlock:X8}, got {got.Value:X8}");
        Assert.That(TokenBlockUtils.Decode(got), Is.EqualTo(rides));
    }

    [Test]
    public void Decode_EarthZeroBlock_returns_zero()
    {
        Assert.That(TokenBlockUtils.Decode(new T55Block(0x18121218)), Is.EqualTo(0u));
    }

    [Test]
    public void Encode_EarthSequence_zero_returns_recorded_zero_block()
    {
        Assert.That(TokenBlockUtils.Encode(0, EncodingSequences.Earth).Value, Is.EqualTo(0x18121218u));
    }

    [Test]
    public void EncodeDecode_EarthSequence_MatchesCapturedLowTable()
    {
        var rows = ParseTable(TableD3FE_0To23);

        foreach (var (ridesRemaining, block) in rows)
        {
            var encoded = TokenBlockUtils.Encode((uint)ridesRemaining, EncodingSequences.Earth);
            Assert.That(encoded.Value, Is.EqualTo(block), $"Rides remaining {ridesRemaining}: expected {block:X8}, got {encoded.Value:X8}");
            Assert.That(TokenBlockUtils.Decode(new T55Block(block)), Is.EqualTo((uint)ridesRemaining));
        }
    }

    [Test]
    public void EncodeDecode_EarthSequence_MatchesElevatorValidatedFirstTwoFamilyBoundaries()
    {
        var rows = ParseTable(TableD3FE_128To255_ValidatedBoundaries);

        foreach (var (ridesRemaining, block) in rows)
        {
            var encoded = TokenBlockUtils.Encode((uint)ridesRemaining, EncodingSequences.Earth);
            Assert.That(encoded.Value, Is.EqualTo(block), $"Rides remaining {ridesRemaining}: expected {block:X8}, got {encoded.Value:X8}");
            Assert.That(TokenBlockUtils.Decode(new T55Block(block)), Is.EqualTo((uint)ridesRemaining));
        }
    }

    [Test]
    public void EncodeDecode_RoundTrip_EarthSequence_0To255()
    {
        for (uint value = 0; value <= 255; value++)
        {
            var encoded = TokenBlockUtils.Encode(value, EncodingSequences.Earth);
            uint decoded = TokenBlockUtils.Decode(encoded);
            Assert.That(decoded, Is.EqualTo(value),
                $"Earth round-trip failed for value {value}: encoded {encoded.Value:X8}, decoded {decoded}");
        }
    }

    [Test]
    public void TryGetByFriendlyName_finds_mercury_venus_earth_and_mars_case_insensitively()
    {
        Assert.That(EncodingSequences.TryGetByFriendlyName("mercury", out var mercury), Is.True);
        Assert.That(mercury, Is.EqualTo(EncodingSequences.Mercury));

        Assert.That(EncodingSequences.TryGetByFriendlyName("VENUS", out var venus), Is.True);
        Assert.That(venus, Is.EqualTo(EncodingSequences.Venus));

        Assert.That(EncodingSequences.TryGetByFriendlyName("Earth", out var earth), Is.True);
        Assert.That(earth, Is.EqualTo(EncodingSequences.Earth));

        Assert.That(EncodingSequences.TryGetByFriendlyName("mArS", out var mars), Is.True);
        Assert.That(mars, Is.EqualTo(EncodingSequences.Mars));

        Assert.That(EncodingSequences.TryGetByFriendlyName("pluto", out _), Is.False);
    }

    [Test]
    public void TryGetSequenceFromBlock_48C7_block_returns_venus_sequence()
    {
        var ok = EncodingSequences.TryGetSequenceFromBlock(
            new T55Block(0x48C74948),
            out var sequence);

        Assert.That(ok, Is.True);
        Assert.That(sequence, Is.EqualTo(EncodingSequences.Venus));
    }

    [Test]
    public void TryGetSequenceFromBlock_BBC7_block_returns_venus_sequence()
    {
        var ok = EncodingSequences.TryGetSequenceFromBlock(
            new T55Block(0xBBC7C940),
            out var sequence);

        Assert.That(ok, Is.True);
        Assert.That(sequence, Is.EqualTo(EncodingSequences.Venus));
    }

    [Test]
    public void TryGetSequenceFromBlock_1812_block_returns_earth_sequence()
    {
        var ok = EncodingSequences.TryGetSequenceFromBlock(
            new T55Block(0x18121218),
            out var sequence);

        Assert.That(ok, Is.True);
        Assert.That(sequence, Is.EqualTo(EncodingSequences.Earth));
    }

    [Test]
    public void TryGetSequenceFromBlock_EB12_block_returns_earth_sequence()
    {
        var ok = EncodingSequences.TryGetSequenceFromBlock(
            new T55Block(0xEB129210),
            out var sequence);

        Assert.That(ok, Is.True);
        Assert.That(sequence, Is.EqualTo(EncodingSequences.Earth));
    }

    [Test]
    public void TryGetSequenceFromBlock_Mars_family_blocks_return_mars_sequence()
    {
        Assert.That(EncodingSequences.TryGetSequenceFromBlock(new T55Block(0x4EC747AE), out var from4EC7), Is.True);
        Assert.That(from4EC7, Is.EqualTo(EncodingSequences.Mars));

        Assert.That(EncodingSequences.TryGetSequenceFromBlock(new T55Block(0xBDC7C946), out var fromBDC7), Is.True);
        Assert.That(fromBDC7, Is.EqualTo(EncodingSequences.Mars));

        Assert.That(EncodingSequences.TryGetSequenceFromBlock(new T55Block(0x4EC6495E), out var from4EC6), Is.True);
        Assert.That(from4EC6, Is.EqualTo(EncodingSequences.Mars));

        Assert.That(EncodingSequences.TryGetSequenceFromBlock(new T55Block(0xBDC6C956), out var fromBDC6), Is.True);
        Assert.That(fromBDC6, Is.EqualTo(EncodingSequences.Mars));
    }

    [Test]
    public void EncodePreservingSequence_venus_token_at_zero_writes_48C7_not_CCC7()
    {
        var reference = new T55Block(0x48C74948);
        var encoded = TokenBlockUtils.EncodePreservingSequence(50, reference);

        Assert.That(encoded.Value, Is.EqualTo(TokenBlockUtils.EncodeByFamily(50, TokenBlockUtils.Families.Family48C7_0To127).Value));
        Assert.That(encoded.Value >> 16, Is.EqualTo(0x48C7u));
        Assert.That(encoded.Value, Is.Not.EqualTo(EncodingSequences.Mercury.Encode(50).Value));
    }

    [Test]
    public void EncodePreservingSequence_venus_token_crossing_128_uses_BBC7_not_3FC7()
    {
        var reference = new T55Block(0x48C736BF); // 127 rides, 48C7 family
        var encoded = TokenBlockUtils.EncodePreservingSequence(130, reference);

        Assert.That(encoded.Value, Is.EqualTo(TokenBlockUtils.EncodeByFamily(130, TokenBlockUtils.Families.FamilyBBC7_128To255).Value));
        Assert.That(encoded.Value >> 16, Is.EqualTo(0xBBC7u));
        Assert.That(encoded.Value, Is.Not.EqualTo(EncodingSequences.Mercury.Encode(130).Value));
    }

    [Test]
    public void EncodePreservingSequence_mercury_token_still_uses_CCC7_and_3FC7()
    {
        var reference = EncodingSequences.Mercury.Encode(73);
        var low = TokenBlockUtils.EncodePreservingSequence(50, reference);
        var high = TokenBlockUtils.EncodePreservingSequence(130, reference);

        Assert.That(low.Value, Is.EqualTo(EncodingSequences.Mercury.Encode(50).Value));
        Assert.That(high.Value, Is.EqualTo(EncodingSequences.Mercury.Encode(130).Value));
    }

    [Test]
    public void T55Block_ToHex_FromHex_RoundTrip()
    {
        var block = new T55Block(0xCCC7363B);
        Assert.That(block.ToHex(), Is.EqualTo("CCC7363B"));
        Assert.That(block.ToHex(addPrefix0x: true), Is.EqualTo("0xCCC7363B"));
        Assert.That(T55Block.FromHex("CCC7363B").Value, Is.EqualTo(0xCCC7363B));
        Assert.That(T55Block.FromHex("0xCCC7363B").Value, Is.EqualTo(0xCCC7363B));
    }

    [Test]
    public void T55Block_ToBin_FromBin_RoundTrip()
    {
        var block = new T55Block(0xCCC7363B);
        var bin = block.ToBin();
        Assert.That(bin.Length, Is.EqualTo(32));
        Assert.That(T55Block.FromBin(bin).Value, Is.EqualTo(0xCCC7363B));
    }


    [Test]
    public void Decode_MatchesVenusSequenceCapture()
    {
        var rows = ParseTable(string.Join("\n", Table43FE_0To127, Table43FE_128To180));

        foreach (var (ridesRemaining, block) in rows)
        {
            uint got = TokenBlockUtils.Decode(new T55Block(block));
            Assert.That(got, Is.EqualTo(ridesRemaining), $"Block {block:X8}: expected {ridesRemaining}, got {got}");
        }
    }

    [Test]
    public void EncodeByFamily_MatchesVenusSequencePredicted181To255()
    {
        var rows = ParseTable(Table43FE_181To255_Predicted);

        foreach (var (ridesRemaining, block) in rows)
        {
            var got = TokenBlockUtils.EncodeByFamily((uint)ridesRemaining, TokenBlockUtils.Families.FamilyBBC7_128To255);
            Assert.That(got.Value, Is.EqualTo(block), $"Rides remaining {ridesRemaining}: expected {block:X8}, got {got.Value:X8}");
        }
    }

    [Test]
    public void Decode_MatchesVenusSequencePredicted181To255()
    {
        var rows = ParseTable(Table43FE_181To255_Predicted);

        foreach (var (ridesRemaining, block) in rows)
        {
            uint got = TokenBlockUtils.Decode(new T55Block(block));
            Assert.That(got, Is.EqualTo(ridesRemaining), $"Block {block:X8}: expected {ridesRemaining}, got {got}");
        }
    }

    [Test]
    public void EncodeDecode_RoundTrip_VenusSequence_0To255()
    {
        for (uint value = 0; value <= 127; value++)
        {
            var encoded = TokenBlockUtils.Encode(value, EncodingSequences.Venus);
            uint decoded = TokenBlockUtils.Decode(encoded);
            Assert.That(decoded, Is.EqualTo(value),
                $"48C7 round-trip failed for value {value}: encoded {encoded.Value:X8}, decoded {decoded}");
        }

        for (uint value = 128; value <= 255; value++)
        {
            var encoded = TokenBlockUtils.Encode(value, EncodingSequences.Venus);
            uint decoded = TokenBlockUtils.Decode(encoded);
            Assert.That(decoded, Is.EqualTo(value),
                $"BBC7 round-trip failed for value {value}: encoded {encoded.Value:X8}, decoded {decoded}");
        }
    }

    [Test]
    public void EncodeDecode_RoundTrip_VenusSequence_256To500()
    {
        for (uint value = 256; value <= 500; value++)
        {
            var encoded = TokenBlockUtils.Encode(value, EncodingSequences.Venus);
            uint decoded = TokenBlockUtils.Decode(encoded);
            Assert.That(decoded, Is.EqualTo(value),
                $"Venus high-range round-trip failed for value {value}: encoded {encoded.Value:X8}, decoded {decoded}");
        }
    }

    [Test]
    public void EncodeDecode_RoundTrip_MarsSequence_0To500()
    {
        for (uint value = 0; value <= 500; value++)
        {
            var encoded = TokenBlockUtils.Encode(value, EncodingSequences.Mars);
            uint decoded = TokenBlockUtils.Decode(encoded);
            Assert.That(decoded, Is.EqualTo(value),
                $"Mars round-trip failed for value {value}: encoded {encoded.Value:X8}, decoded {decoded}");
        }
    }

    [Test]
    public void TryGetFamilyFromBlock_RecognizesVenusSequenceFamilies()
    {
        Assert.That(TokenBlockUtils.Families.TryGetFamilyFromBlock(new T55Block(0x48C74948), out var lowFamily), Is.True);
        Assert.That(lowFamily, Is.EqualTo(TokenBlockUtils.Families.Family48C7_0To127));

        Assert.That(TokenBlockUtils.Families.TryGetFamilyFromBlock(new T55Block(0xBBC7C940), out var midFamily), Is.True);
        Assert.That(midFamily, Is.EqualTo(TokenBlockUtils.Families.FamilyBBC7_128To255));

        Assert.That(TokenBlockUtils.Families.TryGetFamilyFromBlock(new T55Block(0x48C64958), out var highMidFamily), Is.True);
        Assert.That(highMidFamily, Is.EqualTo(TokenBlockUtils.Families.Family48C6_256To383));

        Assert.That(TokenBlockUtils.Families.TryGetFamilyFromBlock(new T55Block(0xBBC6C950), out var highFamily), Is.True);
        Assert.That(highFamily, Is.EqualTo(TokenBlockUtils.Families.FamilyBBC6_384To500));
    }

    [Test]
    public void TryGetSequenceFromBlock_48C6_and_BBC6_blocks_return_venus_sequence()
    {
        Assert.That(EncodingSequences.TryGetSequenceFromBlock(new T55Block(0x48C64958), out var from48C6), Is.True);
        Assert.That(from48C6, Is.EqualTo(EncodingSequences.Venus));

        Assert.That(EncodingSequences.TryGetSequenceFromBlock(new T55Block(0xBBC6BD17), out var fromBBC6), Is.True);
        Assert.That(fromBBC6, Is.EqualTo(EncodingSequences.Venus));
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
