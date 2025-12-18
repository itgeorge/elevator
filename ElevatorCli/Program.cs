using ElevatorCli.Commands;
using ElevatorCli.IO;

namespace ElevatorCli;

class Program
{

    /*
    Usage examples (page is 0-based, block is 0..7, 32-bit words big-endian):

    Open a .bin and print the table
      elev> open "dumps/lf-t55xx-...-dump.bin"
      Loaded 12 blocks from 'dumps/lf-t55xx-...-dump.bin'.
      elev> print
      [+] Page 0
      [+] blk | hex data | binary                           | ascii
      [+] ----+----------+----------------------------------+-------
      [+]  00 | 00148040 | 00000000000101001000000001000000 | ...@
      ...

    Read a specific block
      elev> get 0 5
      FCC76705

    Set a block using hex (8 hex digits)
      elev> set 0 5 -x FCC76705

    Set a block using binary (32 bits of 0/1)
      elev> set 0 6 -b 11111100110001110110011100000101

    Save to same or new file
      elev> save
      elev> save "out.bin"

    Verify/sync mirrors (Page 0, blocks 5 and 6)
      elev> verify-mirrors
      Mirrors match
      elev> sync-mirrors 5

    Other commands
      elev> info
      elev> unload
      elev> help
      elev> exit
    */
    static void Main(string[] args)
    {
        var processor = new CommandProcessor(
            new IBlockReader[] { new BinBlockReader() },
            new IBlockWriter[] { new BinBlockWriter() }
        );

        Console.WriteLine("T55xx .bin CLI. Type 'help' for commands. Ctrl+C to exit.");
        processor.PrintHelp();
        while (true)
        {
            Console.Write("elev> ");
            var line = Console.ReadLine();
            if (line == null) break;
            var keepGoing = processor.Execute(line);
            if (!keepGoing) break;
        }
    }
}