using System.Text;

namespace RidesCli;

/// <summary>
/// Reads input from Console.
/// </summary>
public sealed class ConsoleRidesInput : IRidesInput
{
    public string? ReadLine() => Console.ReadLine();

    public string? ReadSecretLine()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return buffer.ToString();
                case ConsoleKey.Backspace:
                    if (buffer.Length > 0)
                        buffer.Length--;
                    break;
                case ConsoleKey.Escape:
                    Console.WriteLine();
                    return null;
                default:
                    if (!char.IsControl(key.KeyChar))
                        buffer.Append(key.KeyChar);
                    break;
            }
        }
    }
}
