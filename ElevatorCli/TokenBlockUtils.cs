using System.Globalization;

namespace ElevatorCli;

static class TokenBlockUtils
{
    public static List<(int v, uint expected)> ParseTable(string table)
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

    public static uint EncodeForBlock(uint ridesRemaining)
    {
        uint g = ridesRemaining >> 4; // group 0..6 (for your dataset)
        uint o = ridesRemaining & 0xF; // offset 0..15

        uint hb = (((g + 4u) & 0x7u) << 4) | (o ^ 0x9u);
        uint lb = ((o ^ 0xCu) << 4) | (g + (g < 4u ? 0xCu : 0x4u));

        uint low16 = (hb << 8) | lb;
        return 0xCCC70000u | low16;
    }

    public static uint DecodeFromBlock(uint block)
    {
        // Extract low 16 bits
        uint low16 = block & 0xFFFF;

        // Extract hb (high byte) and lb (low byte)
        uint hb = low16 >> 8;
        uint lb = low16 & 0xFF;

        // Extract o from hb: hb = (((g + 4) & 7) << 4) | (o ^ 9)
        // So o = (hb & 0xF) ^ 9
        uint o = (hb & 0xF) ^ 0x9;

        // Extract g info from lb: lb = ((o ^ 12) << 4) | (g + offset)
        // where offset = g < 4 ? 12 : 4
        uint lb_high = lb >> 4; // (o ^ 12)
        uint lb_low = lb & 0xF;  // (g + offset)

        // Verify o: lb_high should equal (o ^ 12)
        if (lb_high != ((o ^ 0xC) & 0xF))
        {
            throw new ArgumentException($"Invalid block format: {block:X8}");
        }

        // Now solve for g. We have: lb_low = g + offset
        // where offset = g < 4 ? 12 : 4
        // We also have g info from hb: ((g + 4) & 7) = hb >> 4
        uint g_from_hb = ((hb >> 4) + 4) & 7; // Reverse of ((g + 4) & 7)

        // Use g from hb to determine offset and solve for g
        uint offset = g_from_hb < 4 ? 12u : 4u;
        uint g = (lb_low + 16 - offset) & 0xF; // Add 16 to handle wraparound

        // Verify g matches what we got from hb
        if (g != g_from_hb)
        {
            throw new ArgumentException($"Invalid block format: {block:X8}");
        }

        // Combine g and o to get rides remaining
        return (g << 4) | o;
    }
}