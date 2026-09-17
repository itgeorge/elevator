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

## Durable state and precedence

The default state directory remains the platform ApplicationData location under `ElevatorTokens/RidesBridge`. Everyday mode preserves it, so the bridge identity and paired-client credentials survive port changes. Explicit durable paths are honored:

1. `Bridge:DataDirectory` / `BRIDGE_DATA_DIRECTORY` controls the default directory for derived files.
2. `Bridge:PairedClientsPath` / `BRIDGE_PAIRED_CLIENTS_PATH` overrides the paired-client store path.
3. `Bridge:IdentityPath` / `BRIDGE_IDENTITY_PATH` overrides the bridge identity path.
4. Within each category, the `Bridge:*` setting wins over its environment alias.

Everyday mode owns the bind and PM3 discovery settings even when conflicting bind, fixed-port, or auto-discovery configuration is present. A configured PM3 client executable path may still be used to locate the discovery script. Other settings retain their normal configuration behavior.

## Tests

Focused non-hardware tests cover process-level help/error behavior, strict launch parsing, port gaps/exhaustion/range boundaries, real IPv4 socket cleanup, configuration precedence, and propagation of the selected port into Bonjour. The everyday process smoke test starts no PM3 connection. Run them with:

```sh
dotnet test RidesBridge.Tests/RidesBridge.Tests.csproj --filter FullyQualifiedName~EverydayLaunchTests
```
