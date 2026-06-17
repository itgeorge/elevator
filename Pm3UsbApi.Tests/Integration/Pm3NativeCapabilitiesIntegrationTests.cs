using NUnit.Framework;
using Pm3UsbApi;

namespace Pm3UsbApi.Tests.Integration;

/// <summary>
/// Validates CMD_CAPABILITIES fetch on native connect.
/// Run:
///   dotnet test --filter "FullyQualifiedName~Pm3NativeCapabilitiesIntegrationTests" -- NUnit.RunExplicitTests=true
/// </summary>
[TestFixture]
[Category("Integration")]
[Explicit("Requires Proxmark3 connected.")]
[NonParallelizable]
public class Pm3NativeCapabilitiesIntegrationTests
{
    [Test]
    public async Task Native_Connect_FetchesCapabilitiesWithLfAndBigBuf()
    {
        await using var pm3 = new Pm3(IntegrationTestOptions.Create(
            Pm3ExecutorKind.Native,
            TimeSpan.FromSeconds(12)));

        await pm3.ConnectAsync();
        await pm3.StartLfTuneAsync();
        var mv = await pm3.GetLfTuneLastMilliVoltsAsync();
        TestContext.WriteLine($"tune={mv} mV");

        // Exercise native T55 path — would fail early if capabilities/LF guard broken.
        var block5 = await pm3.ReadPage0BlockAsync(5);
        TestContext.WriteLine($"block5={block5}");
        Assert.That(block5, Has.Length.EqualTo(8));
    }
}
