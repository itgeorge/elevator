namespace Pm3Cli;

class Pm3CliProgram
{
    static void Main(string[] args)
    {
        // Console utility to interact with the PM3 USB device, mostly as a dev tool
        // It will use the Pm3UsbApi library to interact with the device
        // It should reuse the CommandProcessor that's currently in the TokenDumpsCli project to parse the command line arguments, but should extract to a separate Utilities project.
        // Should work in interactive mode where it prompts the user for commands.
        // Commands should cover the public API of the Pm3UsbApi library.
    }
}