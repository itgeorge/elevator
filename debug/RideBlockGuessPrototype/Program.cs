using System.Globalization;

var confirmedC7C6Cases = KnownCaseFixtures.BuildC7C6ConfirmedCases().ToList();
var expectedUnsupportedCases = KnownCaseFixtures.BuildExpectedUnsupportedCases().ToList();
var issues = new List<string>();
var byLabel = new Dictionary<string, int>();

foreach (var testCase in confirmedC7C6Cases)
{
    byLabel[testCase.Label] = byLabel.GetValueOrDefault(testCase.Label) + 1;

    var guess = RideBlockOnlyGuesser.GuessMirrored(testCase.Block, testCase.Block);
    if (!guess.Success)
    {
        issues.Add($"MISS {testCase.Label} rides={testCase.Rides} block={testCase.Block:X8}: {guess.Error}");
        continue;
    }

    if (guess.Rides != testCase.Rides)
    {
        issues.Add($"WRONG {testCase.Label} block={testCase.Block:X8}: expected rides={testCase.Rides}, got {guess.Rides} via {guess.Method}");
    }
}

var expectedUnsupportedMisses = new List<string>();
foreach (var unsupported in expectedUnsupportedCases)
{
    byLabel[unsupported.Label] = byLabel.GetValueOrDefault(unsupported.Label) + 1;

    var guess = RideBlockOnlyGuesser.GuessMirrored(unsupported.Block, unsupported.Block);
    if (guess.Success)
    {
        issues.Add($"UNEXPECTED {unsupported.Label} block={unsupported.Block:X8}: guessed rides={guess.Rides} via {guess.Method}");
    }
    else
    {
        expectedUnsupportedMisses.Add($"EXPECTED_UNSUPPORTED {unsupported.Label} rides={unsupported.RidesLabel} block={unsupported.Block:X8}: {guess.Error}");
    }
}

Console.WriteLine("RideBlockGuessPrototype");
Console.WriteLine("Guesses ride count from mirrored blocks 5/6 only; does not reference production EncodingSequences/Families.");
Console.WriteLine("Current prototype intentionally handles only the C7/C6 class (Mercury/Venus/Mars). Earth and EBFE should stay unresolved for now.");
Console.WriteLine();
Console.WriteLine("Fixture coverage:");
foreach (var (label, count) in byLabel.OrderBy(kvp => kvp.Key))
    Console.WriteLine($"  {label,-28} {count,4} cases");
Console.WriteLine();

if (expectedUnsupportedMisses.Count > 0)
{
    Console.WriteLine("Expected unresolved samples:");
    foreach (var miss in expectedUnsupportedMisses.Take(20))
        Console.WriteLine("  " + miss);
    if (expectedUnsupportedMisses.Count > 20)
        Console.WriteLine($"  ... {expectedUnsupportedMisses.Count - 20} more expected unresolved samples");
    Console.WriteLine();
}

if (issues.Count == 0)
{
    Console.WriteLine($"PASS: {confirmedC7C6Cases.Count} C7/C6 confirmed cases decoded correctly; {expectedUnsupportedCases.Count} Earth/unknown samples stayed unresolved as expected.");
    return 0;
}

Console.WriteLine($"FAIL: {issues.Count} issue(s) found.");
foreach (var issue in issues)
    Console.WriteLine("  " + issue);
return 1;

internal static class RideBlockOnlyGuesser
{
    public static GuessResult GuessMirrored(uint block5, uint block6)
    {
        if (block5 != block6)
            return GuessResult.Fail($"mirror mismatch: block5={block5:X8}, block6={block6:X8}");

        return Guess(block5);
    }

    public static GuessResult Guess(uint block)
    {
        var candidates = new List<GuessCandidate>();
        AddC7C6ClassCandidates(block, candidates);

        var distinct = candidates
            .DistinctBy(candidate => (candidate.Rides, candidate.Method))
            .ToList();

        if (distinct.Count == 0)
            return GuessResult.Fail($"no C7/C6 prototype rule matched block {block:X8}");

        if (distinct.Count > 1)
        {
            var summary = string.Join("; ", distinct.Select(c => $"{c.Rides} via {c.Method}"));
            return GuessResult.Fail($"ambiguous block {block:X8}: {summary}");
        }

        var only = distinct[0];
        return GuessResult.Ok(only.Rides, only.Method);
    }

    private static void AddC7C6ClassCandidates(uint block, List<GuessCandidate> candidates)
    {
        var high16 = block >> 16;
        var highByte = (byte)(high16 >> 8);
        var classByte = (byte)(high16 & 0xFF);
        var payload = (ushort)(block & 0xFFFF);

        switch (classByte)
        {
            case 0xC7:
                // Segment 0 interpretation: highByte is the primary sequence byte.
                AddCandidate(block, payload, baseOffset: 0, BuildC7C6Xor(primaryHighByte: highByte, segmentIndex: 0),
                    $"C7/C6 primary=0x{highByte:X2} segment=0");

                // Segment 1 interpretation: highByte is primary^F3.
                var primaryForSegment1 = (byte)(highByte ^ 0xF3);
                AddCandidate(block, payload, baseOffset: 128, BuildC7C6Xor(primaryForSegment1, segmentIndex: 1),
                    $"C7/C6 primary=0x{primaryForSegment1:X2} segment=1");
                break;

            case 0xC6:
                // Segment 2 interpretation: highByte is the primary sequence byte.
                AddCandidate(block, payload, baseOffset: 256, BuildC7C6Xor(primaryHighByte: highByte, segmentIndex: 2),
                    $"C7/C6 primary=0x{highByte:X2} segment=2");

                // Segment 3 interpretation: highByte is primary^F3.
                var primaryForSegment3 = (byte)(highByte ^ 0xF3);
                AddCandidate(block, payload, baseOffset: 384, BuildC7C6Xor(primaryForSegment3, segmentIndex: 3),
                    $"C7/C6 primary=0x{primaryForSegment3:X2} segment=3");
                break;
        }

        void AddCandidate(uint originalBlock, ushort low16, uint baseOffset, ushort xor, string method)
        {
            if (TryDecodeBaseLow16((ushort)(low16 ^ xor), out var m))
            {
                var rides = baseOffset + m;
                if (rides <= 500)
                    candidates.Add(new GuessCandidate(originalBlock, rides, method));
            }
        }
    }

    private static ushort BuildC7C6Xor(byte primaryHighByte, int segmentIndex)
    {
        var seed = primaryHighByte ^ 0xCC;
        var lowByte = (byte)(seed + segmentIndex * 0x08);
        var highByte = (segmentIndex % 2) == 0 ? 0x00 : 0x80;
        return (ushort)((highByte << 8) | lowByte);
    }

    private static bool TryDecodeBaseLow16(ushort low16, out uint m)
    {
        m = 0;
        var hb = (uint)(low16 >> 8);
        var lb = (uint)(low16 & 0xFF);

        var o = (hb & 0xF) ^ 0x9u;
        if ((lb >> 4) != ((o ^ 0xC) & 0xF))
            return false;

        var gFromHb = ((hb >> 4) + 4) & 0x7u;
        var offset = gFromHb < 4 ? 12u : 4u;
        var g = ((lb & 0xF) + 16u - offset) & 0xFu;
        if (g != gFromHb)
            return false;

        m = (g << 4) | o;
        return m <= 127;
    }
}

internal sealed record GuessResult(bool Success, uint? Rides, string? Method, string? Error)
{
    public static GuessResult Ok(uint rides, string method) => new(true, rides, method, null);
    public static GuessResult Fail(string error) => new(false, null, null, error);
}

internal sealed record GuessCandidate(uint Block, uint Rides, string Method);
internal sealed record KnownCase(string Label, uint Rides, uint Block);
internal sealed record ExpectedUnsupportedCase(string Label, string RidesLabel, uint Block);

internal static class KnownCaseFixtures
{
    public static IEnumerable<KnownCase> BuildC7C6ConfirmedCases()
    {
        foreach (var c in BuildSequence("Mercury", [
            new FamilySpec(0, 127, 0xCCC7, 0x0000, 0),
            new FamilySpec(128, 255, 0x3FC7, 0x8008, 128),
            new FamilySpec(256, 383, 0xCCC6, 0x0010, 256),
            new FamilySpec(384, 500, 0x3FC6, 0x8018, 384),
        ])) yield return c;

        foreach (var c in BuildSequence("Venus", [
            new FamilySpec(0, 127, 0x48C7, 0x0084, 0),
            new FamilySpec(128, 255, 0xBBC7, 0x808C, 128),
            new FamilySpec(256, 383, 0x48C6, 0x0094, 256),
            new FamilySpec(384, 500, 0xBBC6, 0x809C, 384),
        ])) yield return c;

        foreach (var c in BuildSequence("Mars", [
            new FamilySpec(0, 127, 0x4EC7, 0x0082, 0),
            new FamilySpec(128, 255, 0xBDC7, 0x808A, 128),
            new FamilySpec(256, 383, 0x4EC6, 0x0092, 256),
            new FamilySpec(384, 500, 0xBDC6, 0x809A, 384),
        ])) yield return c;
    }

    public static IEnumerable<ExpectedUnsupportedCase> BuildExpectedUnsupportedCases()
    {
        foreach (var c in BuildSequence("Earth expected unsupported", [
            new FamilySpec(0, 127, 0x1812, 0x5BD4, 0),
            new FamilySpec(128, 255, 0xEB12, 0xDBDC, 128),
        ]))
        {
            yield return new ExpectedUnsupportedCase(c.Label, c.Rides.ToString(CultureInfo.InvariantCulture), c.Block);
        }

        yield return new ExpectedUnsupportedCase("Earth rejected XOR-style 256 candidate", "256", 0x18131228u);
        yield return new ExpectedUnsupportedCase("Earth rejected XOR-style 383 candidate", "383", 0x18136DDFu);
        yield return new ExpectedUnsupportedCase("Earth accepted-as-zero minus-style 256 candidate", "256", 0x18111228u);
        yield return new ExpectedUnsupportedCase("Earth accepted-as-zero minus-style 384 candidate", "384", 0xEB119220u);
        yield return new ExpectedUnsupportedCase("EBFE unresolved sample", "unknown", 0x8C134C84u);
        yield return new ExpectedUnsupportedCase("random unknown", "unknown", 0xDEAD1234u);
    }

    private static IEnumerable<KnownCase> BuildSequence(string label, IReadOnlyList<FamilySpec> families)
    {
        foreach (var family in families)
        {
            for (var rides = family.MinRides; rides <= family.MaxRides; rides++)
                yield return new KnownCase(label, rides, EncodeByFamily(rides, family));
        }
    }

    private static uint EncodeByFamily(uint rides, FamilySpec family)
    {
        if (rides < family.BaseOffset || rides > family.BaseOffset + 127)
            throw new ArgumentOutOfRangeException(nameof(rides), rides.ToString(CultureInfo.InvariantCulture));

        var m = rides - family.BaseOffset;
        var low16 = (ushort)(EncodeBaseLow16(m) ^ family.Xor);
        return (family.High16 << 16) | low16;
    }

    private static ushort EncodeBaseLow16(uint m)
    {
        var g = m >> 4;
        var o = m & 0xFu;
        var hb = (((g + 4u) & 0x7u) << 4) | (o ^ 0x9u);
        var lb = ((o ^ 0xCu) << 4) | (g + (g < 4u ? 0xCu : 0x4u));
        return (ushort)((hb << 8) | lb);
    }
}

internal sealed record FamilySpec(uint MinRides, uint MaxRides, uint High16, ushort Xor, uint BaseOffset);
