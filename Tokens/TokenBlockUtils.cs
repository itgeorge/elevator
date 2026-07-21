namespace Tokens;

public static class TokenBlockUtils
{
    public record Family(uint High16, uint XorConst, uint BaseOffset);

    public static class Families
    {
        public static Family Family0To127 => EncodingFamilyDefinitions.Mercury0To127;
        public static Family Family128To255 => EncodingFamilyDefinitions.Mercury128To255;
        public static Family Family256To383 => EncodingFamilyDefinitions.Mercury256To383;
        public static Family Family384To500 => EncodingFamilyDefinitions.Mercury384To500;

        public static Family Family48C7_0To127 => EncodingFamilyDefinitions.Venus0To127;
        public static Family FamilyBBC7_128To255 => EncodingFamilyDefinitions.Venus128To255;
        public static Family Family48C6_256To383 => EncodingFamilyDefinitions.Venus256To383;
        public static Family FamilyBBC6_384To500 => EncodingFamilyDefinitions.Venus384To500;

        public static Family Family1812_0To127 => EncodingFamilyDefinitions.Earth0To127;
        public static Family FamilyEB12_128To255 => EncodingFamilyDefinitions.Earth128To255;

        public static Family Family1F12_0To127 => EncodingFamilyDefinitions.Pluto0To127;
        public static Family FamilyEC12_128To255 => EncodingFamilyDefinitions.Pluto128To255;

        public static Family Family4EC7_0To127 => EncodingFamilyDefinitions.Mars0To127;
        public static Family FamilyBDC7_128To255 => EncodingFamilyDefinitions.Mars128To255;
        public static Family Family4EC6_256To383 => EncodingFamilyDefinitions.Mars256To383;
        public static Family FamilyBDC6_384To500 => EncodingFamilyDefinitions.Mars384To500;

        public static IReadOnlyList<Family> All => AllFamilies;

        private static readonly Family[] AllFamilies = BuildAllFamilies();
        private static readonly Dictionary<uint, Family> FamilyByHigh16 = BuildFamilyByHigh16();

        private static Family[] BuildAllFamilies() =>
            EncodingSequences.All
                .SelectMany(sequence => sequence.Segments)
                .Select(segment => segment.Family)
                .Distinct()
                .ToArray();

        private static Dictionary<uint, Family> BuildFamilyByHigh16()
        {
            var map = new Dictionary<uint, Family>();
            foreach (var family in AllFamilies)
            {
                map[family.High16] = family;
            }

            return map;
        }

        public static bool TryGetFamilyFromBlock(T55Block block, out Family? family)
        {
            uint high16 = block.Value >> 16;
            if (FamilyByHigh16.TryGetValue(high16, out var found))
            {
                family = found;
                return true;
            }

            family = null;
            return false;
        }

        public static Family GetFamilyFromBlock(T55Block block)
        {
            if (TryGetFamilyFromBlock(block, out var family) && family is not null)
            {
                return family;
            }

            uint high16 = block.Value >> 16;
            throw new ArgumentException($"Block {block.Value:X8} uses an unknown encoding family (high 16 {high16:X4})");
        }
    }

    private static ushort EncodeBaseLow16Only(uint m)
    {
        uint g = m >> 4;
        uint o = m & 0xFu;

        uint hb = (((g + 4u) & 0x7u) << 4) | (o ^ 0x9u);
        uint lb = ((o ^ 0xCu) << 4) | (g + (g < 4u ? 0xCu : 0x4u));

        return (ushort)((hb << 8) | lb);
    }

    private static uint DecodeFromBaseBlock(uint block)
    {
        uint low16 = block & 0xFFFF;

        uint hb = low16 >> 8;
        uint lb = low16 & 0xFF;

        uint o = (hb & 0xF) ^ 0x9;

        uint lb_high = lb >> 4;
        uint lb_low = lb & 0xF;

        if (lb_high != ((o ^ 0xC) & 0xF))
        {
            throw new ArgumentException($"Invalid block format: {block:X8}");
        }

        uint g_from_hb = ((hb >> 4) + 4) & 7;

        uint offset = g_from_hb < 4 ? 12u : 4u;
        uint g = (lb_low + 16 - offset) & 0xF;

        if (g != g_from_hb)
        {
            throw new ArgumentException($"Invalid block format: {block:X8}");
        }

        return (g << 4) | o;
    }

    public static T55Block EncodeByFamily(uint value, Family family)
    {
        if (value < family.BaseOffset || value > family.BaseOffset + 127)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Value {value} is outside the valid range [{family.BaseOffset}, {family.BaseOffset + 127}] for family 0x{family.High16:X4}.");
        }

        uint m = value - family.BaseOffset;
        ushort base16 = EncodeBaseLow16Only(m);
        uint low16 = (uint)(base16 ^ (ushort)family.XorConst);
        return new T55Block((family.High16 << 16) | low16);
    }

    public static T55Block Encode(uint ridesRemaining, EncodingSequence sequence) =>
        sequence.Encode(ridesRemaining);

    public static T55Block EncodePreservingSequence(uint ridesRemaining, T55Block referenceBlock)
    {
        var sequence = EncodingSequences.GetSequenceFromBlock(referenceBlock);
        return Encode(ridesRemaining, sequence);
    }

    public static bool TryDecode(T55Block block, out uint ridesRemaining)
    {
        ridesRemaining = 0;
        try
        {
            ridesRemaining = Decode(block);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static uint Decode(T55Block block)
    {
        var family = Families.GetFamilyFromBlock(block);

        uint low16Masked = block.Value & 0xFFFFu;
        uint low16Base = low16Masked ^ family.XorConst;

        uint baseBlock = 0xCCC70000u | (low16Base & 0xFFFFu);
        uint m = DecodeFromBaseBlock(baseBlock);

        return family.BaseOffset + m;
    }
}
