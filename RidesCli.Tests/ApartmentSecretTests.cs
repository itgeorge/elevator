using System.Text;
using NUnit.Framework;
using RidesCli;

namespace RidesCli.Tests;

public class ApartmentSecretTests
{
    [Test]
    public void EnsureSecret_missingSecret_promptsOnceAndCaches()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        var input = new ScriptedRidesInput([], ["phase1-test-secret"]);
        var ensurer = new ApartmentSecretEnsurer(store, input, output);

        Assert.That(ensurer.EnsureSecret(out var secret), Is.True);
        Assert.That(store.HasSecret, Is.True);
        Assert.That(Encoding.UTF8.GetString(secret), Is.EqualTo("phase1-test-secret"));
        Assert.That(input.ReadSecretLineCallCount, Is.EqualTo(1));
        Assert.That(output.Lines, Has.Some.EqualTo("Enter apartment secret:"));
        Assert.That(output.Lines, Has.None.Contains("phase1-test-secret"));
    }

    [Test]
    public void EnsureSecret_secondCall_doesNotPromptAgain()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        var input = new ScriptedRidesInput([], ["cached-secret"]);
        var ensurer = new ApartmentSecretEnsurer(store, input, output);

        Assert.That(ensurer.EnsureSecret(out _), Is.True);
        Assert.That(ensurer.EnsureSecret(out var secret), Is.True);
        Assert.That(input.ReadSecretLineCallCount, Is.EqualTo(1));
        Assert.That(Encoding.UTF8.GetString(secret), Is.EqualTo("cached-secret"));
    }

    [Test]
    public void EnsureSecret_emptyInput_returnsErrorAndRemainsUnset()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        var input = new ScriptedRidesInput([], [""]);
        var ensurer = new ApartmentSecretEnsurer(store, input, output);

        Assert.That(ensurer.EnsureSecret(out _), Is.False);
        Assert.That(store.HasSecret, Is.False);
        Assert.That(output.Lines, Has.Some.Contains("cannot be empty"));
        Assert.That(ensurer.EnsureSecret(out _), Is.False);
        Assert.That(input.ReadSecretLineCallCount, Is.EqualTo(2));
    }

    [Test]
    public void EnsureSecret_cancelledInput_returnsErrorAndRemainsUnset()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        var input = new ScriptedRidesInput([], [null]);
        var ensurer = new ApartmentSecretEnsurer(store, input, output);

        Assert.That(ensurer.EnsureSecret(out _), Is.False);
        Assert.That(store.HasSecret, Is.False);
        Assert.That(output.Lines, Has.Some.Contains("cancelled"));
    }

    [Test]
    public void AptSecret_storesSecretWithoutEchoingInOutput()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        var input = new ScriptedRidesInput([], ["do-not-echo-this-secret"]);
        var handler = new RidesCommandHandler(new FakeRidesPm3Api(), output, new RidesConfig(), input, store);

        handler.Execute(["aptsecret"]);

        Assert.That(store.HasSecret, Is.True);
        Assert.That(output.Lines, Has.Some.EqualTo("Apartment secret stored."));
        Assert.That(output.Lines, Has.None.Contains("do-not-echo-this-secret"));
    }

    [Test]
    public void AptSecret_emptySecret_printsErrorAndDoesNotStore()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        var input = new ScriptedRidesInput([], [""]);
        var handler = new RidesCommandHandler(new FakeRidesPm3Api(), output, new RidesConfig(), input, store);

        handler.Execute(["aptsecret"]);

        Assert.That(store.HasSecret, Is.False);
        Assert.That(output.Lines, Has.Some.Contains("cannot be empty"));
        Assert.That(output.Lines, Has.None.EqualTo("Apartment secret stored."));
    }

    [Test]
    public void AptSecret_cancelledSecret_printsErrorAndDoesNotStore()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        var input = new ScriptedRidesInput([], [null]);
        var handler = new RidesCommandHandler(new FakeRidesPm3Api(), output, new RidesConfig(), input, store);

        handler.Execute(["aptsecret"]);

        Assert.That(store.HasSecret, Is.False);
        Assert.That(output.Lines, Has.Some.Contains("cancelled"));
    }
}
