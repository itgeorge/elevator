namespace Tokens;

public static class TokenBlockUtils
{
    public record Family(uint High16, uint XorConst);

    public static class Families
    {
        public static readonly Family Family0To127 = new(0xCCC7, 0x0000);
        public static readonly Family Family128To255 = new(0x3FC7, 0x8008);
        public static readonly Family Family256To383 = new(0xCCC6, 0x0010);
        public static readonly Family Family384To500 = new(0x3FC6, 0x8018);

        private static uint GetHigh16FromBlock(uint block)
        {
            return block >> 16;
        }

        public static bool TryGetFamilyFromBlock(T55Block block, out Family? family)
        {
            uint high16 = GetHigh16FromBlock(block.Value);
            if (high16 == Family0To127.High16)
            {
                family = Family0To127;
                return true;
            }

            if (high16 == Family128To255.High16)
            {
                family = Family128To255;
                return true;
            }

            if (high16 == Family256To383.High16)
            {
                family = Family256To383;
                return true;
            }

            if (high16 == Family384To500.High16)
            {
                family = Family384To500;
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

            uint high16 = GetHigh16FromBlock(block.Value);
            throw new ArgumentException($"Block {block.Value:X8} uses an unknown encoding family (high 16 {high16:X4})");
        }

        public static Family GetFamilyFromRides(uint ridesRemaining)
        {
            if (ridesRemaining <= 127)
            {
                return Family0To127;
            }

            if (ridesRemaining <= 255)
            {
                return Family128To255;
            }

            if (ridesRemaining <= 383)
            {
                return Family256To383;
            }

            if (ridesRemaining <= 500)
            {
                return Family384To500;
            }

            throw new ArgumentException($"Rides remaining {ridesRemaining} is out of range");
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
    
    public static T55Block EncodeByFamily(uint value, Family family)
    {
        uint m = value & 0x7Fu;
        ushort base16 = EncodeBaseLow16Only(m);
        uint low16 = (uint)(base16 ^ (ushort)family.XorConst);
        return new T55Block((family.High16 << 16) | low16);
    }

    public static T55Block Encode(uint ridesRemaining) 
    {
        return EncodeByFamily(ridesRemaining, Families.GetFamilyFromRides(ridesRemaining));
    }

    public static uint Decode(T55Block block)
    {
        var family = Families.GetFamilyFromBlock(block);

        // 1) Unmask the payload (XOR is on low16, not on the decoded number)
        uint low16Masked = block.Value & 0xFFFFu;
        uint low16Base = low16Masked ^ family.XorConst;

        // 2) Build a "base-format" block and decode m in [0..127]
        // DecodeFromBlock only relies on low16, but we keep the header consistent.
        uint baseBlock = 0xCCC70000u | (low16Base & 0xFFFFu);
        uint m = DecodeFromBaseBlock(baseBlock); // 0..127

        // 3) Expand to full range based on which family we're in
        uint baseOffset =
            family == Families.Family0To127 ? 0u :
            family == Families.Family128To255 ? 128u :
            family == Families.Family256To383 ? 256u :
            family == Families.Family384To500 ? 384u :
            throw new ArgumentException($"Unknown family for block {block.Value:X8}");

        uint ridesRemaining = baseOffset + m;

        return ridesRemaining;
    }
}
