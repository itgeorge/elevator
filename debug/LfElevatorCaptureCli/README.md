# LfElevatorCaptureCli

Separate debug tooling for **authorized, passive** LF elevator transaction captures. It never performs tag writes, password operations, LF configuration, tuning, block reads/dumps, cloning, simulation, or reset operations.

## Operator workflow

From the repository root, with the PM3 client installed:

```sh
dotnet run --project debug/LfElevatorCaptureCli -- red
# Move PM3 + the authorized fob to the elevator reader.
# After the elevator accepts it, press Enter in this CLI to stop.
dotnet run --project debug/LfElevatorCaptureCli -- black
```

Each invocation creates `debug/lf-elevator-captures/<UTC>-<sanitized-label>/` containing:

- `manifest.json`: label, UTC times, parameters, command allow-list, event history, and each window's status;
- `transcript.log`: generated PM3 command/output transcript; and
- retained `.pm3` graph traces, named with the sanitized label, window number, and UTC timestamp.

The default ring retains the newest eight completed windows. Use `--keep 0` to retain every completed window, or change `--keep N` for another ring size. A manifest records windows evicted from the ring. Pressing Enter is graceful: it requests the loop stop but does not cancel an active window; that window must finish `lf sniff`, `data save`, file discovery, and verification first.

## Noninteractive/scripted operation

Stdin is never assumed to be available. Use a finite bound when piping/redirecting stdin:

```sh
dotnet run --project debug/LfElevatorCaptureCli -- --windows 12 --no-prompt --keep 12 red
# or
dotnet run --project debug/LfElevatorCaptureCli -- --duration-seconds 30 --no-prompt --keep 0 red
```

Useful options include `--output DIR`, `--port PORT`, `--pm3-path PATH`, `--samples N`, `--timeout-seconds N`, and `--dry-run`. Run `--help` for the complete list. Labels are reduced to ASCII letters/digits/`-`/`_`; path separators and shell-like punctuation become `_`.

## Safety and PM3 behavior

The CLI uses the existing `Pm3` process executor, but does not expose its arbitrary-command escape hatch. The only generated device command is:

```text
lf sniff -s N; data save -f "<run-directory>/<capture-name>"
```

Connection verification sends only `hw version`. `data save` is a local graph-buffer save performed by the PM3 client. No command text is accepted from the operator.

The installed Proxmark3 source/help was inspected without opening hardware. In client `cmdlf.c` (`CmdLFSniff`), `lf sniff -@` prints “Press <Enter> to exit”, then loops `lf_sniff(...)` until the PM3 client's own `kbd_enter_pressed()` sees Enter. The `-c` process mode does not provide a separate application stop API, and sharing terminal stdin between the PM3 child and this CLI is race-prone. Therefore this tool deliberately does **not** use `-@`; it repeats bounded `lf sniff -s N` windows and watches Enter itself. A completed window is saved before the next starts; Enter/Ctrl-C stops between windows or cancels the current process safely.

The client source also shows that a configured LF trigger threshold can make a bounded sniff wait for a device response without the normal short timeout. Because this tool is forbidden from issuing `lf config`, it cannot change that state. The process executor timeout then terminates that window before `data save`, so the manifest will show a failed/missing-file window. Operators should use a PM3 setup with a suitable pre-existing passive LF configuration; this limitation is intentional and documented rather than bypassed with a configuration command. If such a window hangs, use Ctrl-C for immediate cancellation; Enter deliberately waits for the current batch so it cannot discard a transaction at the save boundary.

`data save` appends `.pm3` and may add a collision suffix according to the installed client. The CLI creates unique base names and verifies the saved file exists. It first checks the expected `<base>.pm3` path, then discovers PM3's collision form such as `<base>-001.pm3` if necessary.

## Offline smoke tests

These commands do not start PM3 and do not access hardware:

```sh
dotnet run --project debug/LfElevatorCaptureCli -- --help
dotnet run --project debug/LfElevatorCaptureCli -- --self-test
dotnet run --project debug/LfElevatorCaptureCli -- --dry-run --windows 1 --output /tmp/lf-capture-smoke red/black
```
