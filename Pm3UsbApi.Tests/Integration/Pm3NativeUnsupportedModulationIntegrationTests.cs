using NUnit.Framework;
using Pm3UsbApi;

namespace Pm3UsbApi.Tests.Integration;

/// <summary>
/// Negative hardware validation: ASK elevator tokens must not be classified as unsupported modulation.
/// Run:
///   dotnet test --filter "FullyQualifiedName~Pm3NativeUnsupportedModulationIntegrationTests" -- NUnit.RunExplicitTests=true
/// </summary>
[TestFixture]
[Category("Integration")]
[Explicit("Requires Proxmark3 connected with ASK T5577 tag.")]
[NonParallelizable]
public class Pm3NativeUnsupportedModulationIntegrationTests
{
    [Test]
    public async Task Native_AskToken_DetectAndRead_DoesNotThrowUnsupportedModulation()
    {
        await using var pm3 = new Pm3(IntegrationTestOptions.Create(
            Pm3ExecutorKind.Native,
            TimeSpan.FromSeconds(12)));

        await pm3.ConnectAsync();
        await EstablishRfCouplingAsync(pm3);

        Pm3UnsupportedModulationException? unsupported = null;
        try
        {
            await pm3.ReadPage0BlockAsync(5);
        }
        catch (Pm3UnsupportedModulationException ex)
        {
            unsupported = ex;
        }

        Assert.That(unsupported, Is.Null, "ASK token should not be reported as unsupported modulation.");
    }

    private static async Task EstablishRfCouplingAsync(Pm3 pm3)
    {
        Pm3CommandException? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await pm3.StartLfTuneAsync();
                var mv = await pm3.GetLfTuneLastMilliVoltsAsync();
                TestContext.WriteLine($"RF tune attempt {attempt}: {mv} mV");
                if (mv > 1000)
                    return;
            }
            catch (Pm3CommandException ex)
            {
                last = ex;
                TestContext.WriteLine($"RF tune attempt {attempt} failed: {ex.Message}");
            }

            await Task.Delay(500);
        }

        throw last ?? new Pm3CommandException("Failed to establish LF coupling before unsupported-modulation test.");
    }
}
