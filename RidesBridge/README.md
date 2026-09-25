# RidesBridge

## Everyday launch

For the normal local-network bridge flow, run exactly:

```sh
dotnet run --project RidesBridge/RidesBridge.csproj -- --everyday
```

Everyday mode binds wildcard IPv4 (`0.0.0.0`), selects the first bindable TCP port in the bounded deterministic range **5080 through 5179**, enables PM3 USB auto-discovery, clears/ignores `Pm3:Port` and `PM3_PORT`, and prints the selected bind plus reachable private URLs using the existing terminal display. The selected port is also used in the Bonjour TXT URL and pairing payloads.

The bindability check happens before Kestrel starts and closes its test socket. It is therefore a check-before-Kestrel TOCTOU check: another process can race the selected port, and startup may still fail. RidesBridge deliberately does not retry with a different port, because that could publish a port/URL/QR state that does not match the listener.

Wildcard binding exposes the authenticated HTTP bridge to private-network interfaces. Use everyday mode only on a trusted local network; pairing PINs expire, but the bridge should still be protected by network trust and the normal pairing flow. Bonjour is best-effort and does not replace authentication.

`--everyday` is intentionally strict: it must be the exact, single launch flag. Duplicates, attached values, unknown extra launch options, and malformed forms fail before ASP.NET argument parsing. Without the flag, the loopback default and existing ASP.NET/configuration behavior are unchanged. `--help` prints the launch summary.

## Fake PM3 launch

For iPad Wi-Fi smoke without USB hardware, run exactly:

```sh
dotnet run --project RidesBridge/RidesBridge.csproj -- --fake-pm3
```

Fake-pm3 mode reuses the everyday local-network bind semantics: wildcard IPv4 (`0.0.0.0`), the same bounded port range **5080 through 5179**, reachable private URL printing, and best-effort Bonjour. It injects a deterministic in-process `FakePm3Device` instead of opening USB or auto-discovering a serial port. `--fake-pm3` is mutually exclusive with `--everyday` and uses the same strict single-flag parsing rules.

The seeded mirror blocks are fixed for repeatable iPad decode smoke:

| Field | Value |
| --- | --- |
| Block 5 | `BBC7FD03` |
| Block 6 | `BBC7FD03` |
| Sequence | `venus` |
| Rides remaining | `180` |

These values match `EncodingSequences.Venus.Encode(180)` and decode on the iPad through the registered Venus sequence.

Select a fake PM3 profile without extra launch flags:

| Profile | Setting | Behavior |
| --- | --- | --- |
| `venus` / `default` | unset or `Bridge:FakePm3Profile` / `RIDES_FAKE_PM3_PROFILE` | Known Venus seed above |
| `no-chip` | `RIDES_FAKE_PM3_PROFILE=no-chip` (aliases: `nochip`, `no_chip`) | Empty antenna: scan returns HTTP 409 `no_chip` |
| `tune-failed` | `RIDES_FAKE_PM3_PROFILE=tune-failed` (aliases: `tunefailed`, `lf_tune_failed`) | LF tune failure: scan returns HTTP 503 `lf_tune_failed` |
| `read-failed` | `RIDES_FAKE_PM3_PROFILE=read-failed` (aliases: `readfailed`, `page0_read_failed`) | Page-0 scan read failure: scan returns HTTP 502 `page0_read_failed` |
| `unknown` | `RIDES_FAKE_PM3_PROFILE=unknown` (aliases: `unknown-mirrors`, `unknown_mirrors`) | Undecodable mirrors (`DEADBEEF`/`FACECAFE`): scan succeeds; iPad unknown path may call `/missing` once for dump assembly |
| `pm3-unavailable` | `RIDES_FAKE_PM3_PROFILE=pm3-unavailable` (aliases: `pm3_unavailable`, `unavailable`) | Disconnected PM3: scan returns HTTP 503 `pm3_unavailable` |

Example no-chip smoke:

```sh
RIDES_FAKE_PM3_PROFILE=no-chip dotnet run --project RidesBridge/RidesBridge.csproj -- --fake-pm3
```

Example tune-failed smoke:

```sh
RIDES_FAKE_PM3_PROFILE=tune-failed dotnet run --project RidesBridge/RidesBridge.csproj -- --fake-pm3
```

Example read-failed smoke:

```sh
RIDES_FAKE_PM3_PROFILE=read-failed dotnet run --project RidesBridge/RidesBridge.csproj -- --fake-pm3
```

Example unknown-mirrors smoke (Concept A unknown → `/missing` once → dump path):

```sh
RIDES_FAKE_PM3_PROFILE=unknown dotnet run --project RidesBridge/RidesBridge.csproj -- --fake-pm3
```

Example pm3-unavailable smoke (bridge up, USB disconnected):

```sh
RIDES_FAKE_PM3_PROFILE=pm3-unavailable dotnet run --project RidesBridge/RidesBridge.csproj -- --fake-pm3
```

Startup prints the selected profile briefly (for example `Fake PM3 profile: no-chip`, `Fake PM3 profile: tune-failed`, `Fake PM3 profile: read-failed`, `Fake PM3 profile: unknown`, or `Fake PM3 profile: pm3-unavailable`).

## Durable state and precedence

The default state directory remains the platform ApplicationData location under `ElevatorTokens/RidesBridge`. Everyday and fake-pm3 modes preserve it, so the bridge identity and paired-client credentials survive port changes. Explicit durable paths are honored:

1. `Bridge:DataDirectory` / `BRIDGE_DATA_DIRECTORY` controls the default directory for derived files.
2. `Bridge:PairedClientsPath` / `BRIDGE_PAIRED_CLIENTS_PATH` overrides the paired-client store path.
3. `Bridge:IdentityPath` / `BRIDGE_IDENTITY_PATH` overrides the bridge identity path.
4. Within each category, the `Bridge:*` setting wins over its environment alias.

Everyday mode owns the bind and PM3 discovery settings even when conflicting bind, fixed-port, or auto-discovery configuration is present. Fake-pm3 mode owns the bind and disables PM3 USB discovery while still honoring durable path overrides. A configured PM3 client executable path may still be used to locate the discovery script in everyday mode only. Other settings retain their normal configuration behavior.

## Tests

Focused non-hardware tests cover process-level help/error behavior, strict launch parsing, port gaps/exhaustion/range boundaries, real IPv4 socket cleanup, configuration precedence, and propagation of the selected port into Bonjour. The everyday and fake-pm3 process smoke tests start no PM3 connection. Run them with:

```sh
dotnet test RidesBridge.Tests/RidesBridge.Tests.csproj --filter FullyQualifiedName~EverydayLaunchTests
dotnet test RidesBridge.Tests/RidesBridge.Tests.csproj --filter FullyQualifiedName~FakePm3LaunchTests
```
