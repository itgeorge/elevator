namespace Pm3UsbApi;

/// <summary>
/// Base exception for Proxmark3 API errors.
/// </summary>
public class Pm3Exception : Exception
{
    /// <summary>
    /// Optional command result associated with the failure.
    /// </summary>
    public CommandResult? CommandResult { get; }

    public Pm3Exception()
    {
    }

    public Pm3Exception(string message) : base(message)
    {
    }

    public Pm3Exception(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Pm3Exception(string message, CommandResult? commandResult)
        : base(message)
    {
        CommandResult = commandResult;
    }

    public Pm3Exception(string message, CommandResult? commandResult, Exception innerException)
        : base(message, innerException)
    {
        CommandResult = commandResult;
    }
}

/// <summary>
/// Thrown when the device or pm3 client cannot be reached.
/// </summary>
public class Pm3ConnectionException : Pm3Exception
{
    public Pm3ConnectionException()
    {
    }

    public Pm3ConnectionException(string message) : base(message)
    {
    }

    public Pm3ConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Pm3ConnectionException(string message, CommandResult? commandResult)
        : base(message, commandResult)
    {
    }
}

/// <summary>
/// Thrown when a command returned error output.
/// </summary>
public class Pm3CommandException : Pm3Exception
{
    public Pm3CommandException()
    {
    }

    public Pm3CommandException(string message) : base(message)
    {
    }

    public Pm3CommandException(string message, CommandResult? commandResult)
        : base(message, commandResult)
    {
    }

    public Pm3CommandException(string message, CommandResult? commandResult, Exception innerException)
        : base(message, commandResult, innerException)
    {
    }
}

/// <summary>
/// Thrown when native detect finds a T55 config block using an unsupported modulation.
/// </summary>
public class Pm3UnsupportedModulationException : Pm3CommandException
{
    public byte Modulation { get; }
    public uint Block0 { get; }
    public string ModulationName { get; }

    public Pm3UnsupportedModulationException(byte modulation, uint block0, CommandResult? commandResult = null)
        : base(
            $"Native executor supports ASK/Manchester T55 tokens only (detected {Pm3T55ModulationNames.Name(modulation)} / 0x{modulation:X2}, block0=0x{block0:X8}). " +
            "Set PM3_EXECUTOR=process and ensure the proxmark3 client is installed.",
            commandResult)
    {
        Modulation = modulation;
        Block0 = block0;
        ModulationName = Pm3T55ModulationNames.Name(modulation);
    }
}

/// <summary>
/// Thrown when a command execution timed out.
/// </summary>
public class Pm3TimeoutException : Pm3Exception
{
    public Pm3TimeoutException()
    {
    }

    public Pm3TimeoutException(string message) : base(message)
    {
    }

    public Pm3TimeoutException(string message, CommandResult? commandResult)
        : base(message, commandResult)
    {
    }
}

/// <summary>
/// Thrown when the pm3 executable cannot be found.
/// </summary>
public class Pm3ClientNotFoundException : Pm3Exception
{
    public Pm3ClientNotFoundException()
    {
    }

    public Pm3ClientNotFoundException(string message) : base(message)
    {
    }

    public Pm3ClientNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
