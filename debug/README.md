# PM3 debug tooling

Hardware diagnostics and capture utilities. Not required for production CLIs.

## NativeT55Probe

Low-level native T55 probing against a connected Proxmark3.

```bash
# Help
dotnet run --project debug/NativeT55Probe -- --help

# Capture block0 samples to offline fixture (re-seed Pm3T55NativeOfflineTests)
dotnet run --project debug/NativeT55Probe -- --capture --port /dev/cu.usbmodem1201

# Hardware load test (same scenario as Pm3NativeLoadTests)
dotnet run --project debug/NativeT55Probe -- --load-test --port /dev/cu.usbmodem1201
```

Prefer the integration test for repeatable load testing:

```bash
dotnet test --filter "FullyQualifiedName~Pm3NativeLoadTests" -- NUnit.RunExplicitTests=true
```

## Scripts

| Script | Purpose |
|--------|---------|
| `scripts/check-com4.ps1` | Windows: verify COM port availability |

## Logs

PM3 diagnostic logs (Slice 7) are written under `{GetTempPath()}/elevator/` when using `Pm3` / `RidesCli` / `Pm3Cli`. See `.plans/pm3-slice-7-logging.md`.
