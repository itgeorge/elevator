# ReadOnlyLfElevatorDiagnostics

Read-only diagnostics for the LF elevator-token investigation.

This project is deliberately separate from production code. Its normal modes are offline-only:

- print the allow-listed read/capture command plan;
- run parser self-tests against synthetic output; and
- parse a text capture supplied by the parent hardware investigation.

It also has one explicit hardware opt-in probe, `--probe-t55`. That mode uses only `Pm3UsbApi.Pm3` to connect, execute the fixed raw command `lf search`, and call `EnsureT55SessionActiveAsync`. It prints the raw search output and either the T55 session result or the detection exception. It performs no writes, dumps, reads, configuration, tuning, password operations, or arbitrary user commands. The probe uses the process executor (the native executor does not support raw CLI text), and every connection/command/cancellation timeout is hard-coded to 15 seconds.

```sh
dotnet run --project debug/ReadOnlyLfElevatorDiagnostics
dotnet run --project debug/ReadOnlyLfElevatorDiagnostics -- --plan
dotnet run --project debug/ReadOnlyLfElevatorDiagnostics -- --parse /path/to/captured-output.txt
# Explicit hardware opt-in; do not run unless a PM3 is intentionally available:
dotnet run --project debug/ReadOnlyLfElevatorDiagnostics -- --probe-t55
dotnet run --project debug/ReadOnlyLfElevatorDiagnostics -- --probe-t55 --port /dev/ttyACM0
```

The parser accepts ordinary PM3 text output and reports T55 detection, block 0–7 values, page-0 dump rows, and LF tune peak voltage.

`lf tune` is intentionally absent from the noninteractive plan and from all suggested process-client batches: `pm3 -c "lf tune"` waits for Enter. If a hardware operator needs that measurement, run it manually in an interactive PM3 session and save the resulting text for `--parse`. The offline modes never run PM3 or access hardware; only the explicit `--probe-t55` mode does so.
