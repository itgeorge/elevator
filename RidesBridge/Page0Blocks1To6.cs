namespace RidesBridge;

internal static class Page0Blocks1To6
{
    public static readonly int[] Allowlist = [1, 2, 3, 4, 5, 6];

    public static bool IsValidResponse(IReadOnlyList<Page0BlockReadResult>? blocks)
    {
        if (blocks is null || blocks.Count != Allowlist.Length)
            return false;

        for (var index = 0; index < Allowlist.Length; index++)
        {
            var expectedBlock = Allowlist[index];
            var entry = blocks[index];
            if (entry.Block != expectedBlock || !Page0MutationValidator.TryNormalizeBlockHex(entry.Value, out _))
                return false;
        }

        return true;
    }
}
