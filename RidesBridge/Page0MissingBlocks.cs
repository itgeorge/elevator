namespace RidesBridge;

internal static class Page0MissingBlocks
{
    public static readonly int[] Allowlist = [0, 1, 2, 3, 7];

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
