using NUnit.Framework;
using Pm3UsbApi;

namespace Pm3UsbApi.Tests.Integration;

/// <summary>
/// Hardware validation for unsupported chip-type vs modulation classification.
/// Run:
///   dotnet test --filter "FullyQualifiedName~Pm3NativeUnsupportedChipTypeIntegrationTests" -- NUnit.RunExplicitTests=true
/// </summary>
[TestFixture]
[Category("Integration")]
[Explicit("Requires Proxmark3 with EM410x tag for non-T55 test, or ASK T55 tag for negative test.")]
[NonParallelizable]
public class Pm3NativeUnsupportedChipTypeIntegrationTests
{
    [Test]
    public async Task Native_AskToken_Read_DoesNotThrowUnsupportedChipType()
    {
        await using var pm3 = new Pm3(IntegrationTestOptions.Create(
            Pm3ExecutorKind.Native,
            TimeSpan.FromSeconds(12)));

        await pm3.ConnectAsync();
        await EstablishRfCouplingAsync(pm3);

        try
        {
            await pm3.ReadPage0BlockAsync(5);
        }
        catch (Pm3UnsupportedChipTypeException ex)
        {
            Assert.Fail($"ASK T55 token should not be classified as unsupported chip type: {ex.Message}");
        }
        catch (Pm3UnsupportedModulationException ex)
        {
            Assert.Fail($"ASK T55 token should not be classified as unsupported modulation: {ex.Message}");
        }
    }

    [Test]
    public async Task Native_Em410xTag_Read_ThrowsUnsupportedChipType_NotModulation()
    {
        await using var pm3 = new Pm3(IntegrationTestOptions.Create(
            Pm3ExecutorKind.Native,
            TimeSpan.FromSeconds(12)));

        await pm3.ConnectAsync();
        await EstablishRfCouplingAsync(pm3);

        try
        {
            await pm3.ReadPage0BlockAsync(5);
            Assert.Fail("EM410x tag should not read as T55.");
        }
        catch (Pm3UnsupportedChipTypeException ex)
        {
            TestContext.WriteLine(ex.Message);
            Assert.That(ex.ChipFamily, Is.EqualTo(Pm3LfChipFamily.NonT55Lf).Or.EqualTo(Pm3LfChipFamily.Em410x));
        }
        catch (Pm3UnsupportedModulationException ex)
        {
            Assert.Fail($"EM410x should classify as unsupported chip type, not modulation: {ex.Message}");
        }
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
            }

            await Task.Delay(500);
        }

        throw last ?? new Pm3CommandException("Failed to establish LF coupling.");
    }
}
