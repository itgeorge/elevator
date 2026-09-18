namespace TestFixtures.RideEncoding;

public sealed class RideEncodingFixtureDocument
{
    public int SchemaVersion { get; set; }
    public string FixtureId { get; set; } = "";
    public uint[] Boundaries { get; set; } = [];
    public RideEncodingSequenceSection[] Sequences { get; set; } = [];
    public RideEncodingRejectedEntry[] RejectedEncodings { get; set; } = [];
    public RideEncodingMalformedBlock[] MalformedBlocks { get; set; } = [];
    public RideEncodingMirrorCase[] MirrorCases { get; set; } = [];
}

public sealed class RideEncodingSequenceSection
{
    public string Name { get; set; } = "";
    public string ZeroBlock { get; set; } = "";
    public byte Rotation { get; set; }
    public uint MinRides { get; set; }
    public uint MaxRides { get; set; }
    public RideEncodingEntry[] Encodings { get; set; } = [];
}

public sealed class RideEncodingEntry
{
    public uint Rides { get; set; }
    public string Block { get; set; } = "";
}

public sealed class RideEncodingRejectedEntry
{
    public string Sequence { get; set; } = "";
    public uint Rides { get; set; }
    public string Block { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class RideEncodingMalformedBlock
{
    public string Name { get; set; } = "";
    public string Block { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class RideEncodingMirrorCase
{
    public string Name { get; set; } = "";
    public string Block5 { get; set; } = "";
    public string Block6 { get; set; } = "";
    public RideEncodingExpectedResult Expected { get; set; } = new();
}

public sealed class RideEncodingExpectedResult
{
    public string Status { get; set; } = "";
    public uint? Rides { get; set; }
    public string? SourceBlock { get; set; }
    public int? SourceBlockNumber { get; set; }
    public bool BlocksMatched { get; set; }
    public string? WarningMessage { get; set; }
}
