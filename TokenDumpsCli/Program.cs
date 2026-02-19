using TokenDumpsCli.Commands;
using TokenDumpsCli.IO;

namespace TokenDumpsCli;

class Program
{
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
