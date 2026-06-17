namespace Pm3UsbApi.Native.T55;

internal enum Pm3T55DetectOutcomeKind
{
    NotFound,
    Found,
    UnsupportedModulation,
}

internal readonly record struct Pm3T55DetectOutcome(
    Pm3T55DetectOutcomeKind Kind,
    Pm3T55UnsupportedModulationInfo UnsupportedInfo = default)
{
    public static Pm3T55DetectOutcome Found => new(Pm3T55DetectOutcomeKind.Found);
    public static Pm3T55DetectOutcome NotFound => new(Pm3T55DetectOutcomeKind.NotFound);

    public static Pm3T55DetectOutcome Unsupported(Pm3T55UnsupportedModulationInfo info) =>
        new(Pm3T55DetectOutcomeKind.UnsupportedModulation, info);

    public bool IsFound => Kind == Pm3T55DetectOutcomeKind.Found;
    public bool IsUnsupported => Kind == Pm3T55DetectOutcomeKind.UnsupportedModulation;
}
