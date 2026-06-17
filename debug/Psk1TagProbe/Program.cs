using Pm3UsbApi;

var pm3 = new Pm3(new Pm3Options
{
    ExecutorKind = Pm3ExecutorKind.Native,
    DevicePort = Environment.GetEnvironmentVariable("PM3_DEVICE_PORT"),
    AutoConnect = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PM3_DEVICE_PORT")),
});

await pm3.ConnectAsync();
await pm3.StartLfTuneAsync();
Console.WriteLine($"tune={await pm3.GetLfTuneLastMilliVoltsAsync()} mV");

try
{
    var b5 = await pm3.ReadPage0BlockAsync(5);
    Console.WriteLine($"UNEXPECTED SUCCESS block5={b5}");
}
catch (Pm3UnsupportedModulationException ex)
{
    Console.WriteLine($"OK UnsupportedModulation: {ex.Message}");
    Console.WriteLine($"  mod={ex.ModulationName} block0=0x{ex.Block0:X8}");
}
catch (Pm3UnsupportedChipTypeException ex)
{
    Console.WriteLine($"Unexpected ChipType: {ex.Message}");
}
catch (Pm3CommandException ex)
{
    Console.WriteLine($"CommandException: {ex.Message}");
}
