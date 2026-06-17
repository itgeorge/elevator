using NUnit.Framework;
using Pm3UsbApi;

namespace Pm3UsbApi.Tests.Integration;

/// <summary>
/// Native executor load test requiring a connected Proxmark3 with T5577 tag.
/// Run manually:
///   dotnet test --filter "FullyQualifiedName~Pm3NativeLoadTests" -- NUnit.RunExplicitTests=true
/// </summary>
[TestFixture]
[Category("Integration")]
[Explicit("Requires Proxmark3 connected with T5577 tag. Run: dotnet test --filter 'Category=Integration' -- NUnit.RunExplicitTests=true")]
[NonParallelizable]
public class Pm3NativeLoadTests
{
    [Test]
    public async Task Native_LoadTest_ReadTuneWriteResetLeavesFiftyRides()
    {
        await using var pm3 = new Pm3(IntegrationTestOptions.Create(
            Pm3ExecutorKind.Native,
            TimeSpan.FromSeconds(12)));

        var resetPath = NativeRideLoadTestRunner.ResolveResetImagePath(
            TestContext.CurrentContext.TestDirectory);

        var result = await NativeRideLoadTestRunner.RunAsync(
            pm3,
            resetPath,
            msg => TestContext.WriteLine(msg));

        Assert.That(result.FinalRides, Is.EqualTo(NativeRideLoadTestRunner.TargetFinalRides),
            "Load test should leave the token at 50 rides.");
        Assert.That(result.OperationCount, Is.GreaterThanOrEqualTo(30),
            "Load test should execute the full read/tune/write/reset sequence.");
        TestContext.WriteLine(
            $"Load test complete: {result.OperationCount} operations, {result.ElapsedMilliseconds}ms total.");
    }
}
