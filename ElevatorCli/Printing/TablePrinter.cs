using ElevatorCli.Models;

namespace ElevatorCli.Printing;

public static class TablePrinter
{
    public static void PrintTable(T55xxImage image, TextWriter writer)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
        writer.WriteLine("[+] Page 0");
        writer.WriteLine("[+] blk | hex data | binary                           | ascii");
        writer.WriteLine("[+] ----+----------+----------------------------------+-------");

        void PrintRow(int absIndex)
        {
            uint word = image.Blocks[absIndex];
            string hex = word.ToString("X8");
            string bin = Convert.ToString(word, 2).PadLeft(32, '0');
            string ascii = WordToAscii(word);
            writer.WriteLine($"[+]  {absIndex % 8:00} | {hex} | {bin} | {ascii}");
        }

        int total = image.BlockCount;
        int page = 0;
        for (int i = 0; i < total; i++)
        {
            if (i > 0 && i % 8 == 0)
            {
                page++;
                writer.WriteLine();
                writer.WriteLine($"[+] Page {page}");
                writer.WriteLine("[+] blk | hex data | binary                           | ascii");
                writer.WriteLine("[+] ----+----------+----------------------------------+-------");
            }
            PrintRow(i);
        }
    }

    private static string WordToAscii(uint word)
    {
        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)((word >> 24) & 0xFF);
        bytes[1] = (byte)((word >> 16) & 0xFF);
        bytes[2] = (byte)((word >> 8) & 0xFF);
        bytes[3] = (byte)(word & 0xFF);

        char ToPrintable(byte b)
        {
            return b >= 32 && b <= 126 ? (char)b : '.';
        }

        return new string(new[] { ToPrintable(bytes[0]), ToPrintable(bytes[1]), ToPrintable(bytes[2]), ToPrintable(bytes[3]) });
    }
}


