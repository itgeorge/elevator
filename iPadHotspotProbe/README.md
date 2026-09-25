# iPad Hotspot Probe

Preserved networking-only smoke test proving that a physical iPad can reach a Mac over the iPhone 13 Pro hotspot through direct HTTP and Bonjour. It has a separate bundle ID (`com.itgeorge.iPadHotspotProbe`) and contains no PM3, firmware, RF, or token code.

## Reproduce

1. Confirm the Mac's iPhone-hotspot Wi-Fi address with `networksetup -getinfo Wi-Fi`. Substitute that address if it is not `172.20.10.4` in `project` source and `serve.py`/`ProbeViewController.swift`.
2. In one terminal, run the bounded listener (stop with Ctrl-C after the app report):

   ```sh
   python3 -u serve.py --host 172.20.10.4 --port 48123
   ```

   It binds only to the hotspot address, serves one health response, accepts only a small report body, and logs peer addresses.
3. In another terminal, advertise only this purpose-specific Bonjour service, for the same bounded interval:

   ```sh
   dns-sd -R iPadHotspotProbe _hotspotprobe._tcp local 48123
   ```

4. Build with Xcode 26.3, install, and launch **only** on the paired physical iPad Air 4:

   ```sh
   xcodegen generate
   xcodebuild -project iPadHotspotProbe.xcodeproj -scheme iPadHotspotProbe \
     -configuration Debug -destination 'id=00008101-001A68EE21F0001E' \
     -derivedDataPath /tmp/iPadHotspotProbeDerived -allowProvisioningUpdates build
   xcrun devicectl device install app --device 00008101-001A68EE21F0001E \
     /tmp/iPadHotspotProbeDerived/Build/Products/Debug-iphoneos/iPadHotspotProbe.app
   xcrun devicectl device process launch --device 00008101-001A68EE21F0001E \
     com.itgeorge.iPadHotspotProbe --terminate-existing
   ```

The app makes one direct `GET /health`, browses `_hotspotprobe._tcp`, then sends one result `POST /report`. It updates its screen with the HTTP response/error and Bonjour results. It does not retry. If iPadOS shows the Local Network prompt, tap **Allow**; if it is denied, go to **Settings → Privacy & Security → Local Network → iPad Hotspot Probe**, enable it, and relaunch once.

## Privacy/security settings

- `NSLocalNetworkUsageDescription` explains the temporary local-network use and causes the iPadOS Local Network permission prompt.
- `NSBonjourServices` permits browsing `_hotspotprobe._tcp`.
- `NSAppTransportSecurity/NSAllowsLocalNetworking` permits the intentional cleartext HTTP request to the private local address only; it is not a global ATS exemption.
- No camera, Bluetooth, accessory, or background capabilities are requested.

After a reproduction run, stop both terminal processes, uninstall the temporary app, and remove `/tmp/iPadHotspotProbeDerived` and the generated Xcode project. Keep this source directory as reproducible development evidence; do not commit generated build products or device-specific logs.

## Smoke-test result (2026-09-16)

- Mac Wi-Fi/en0: `172.20.10.4/28`, broadcast `172.20.10.15`, gateway `172.20.10.1`; route to `172.20.10.2` is via en0. The interface was marked constrained; `networksetup -getairportnetwork` reported no AirPort association despite the active address, so the hotspot SSID itself was not independently exposed by that command. The iPad appeared as `172.20.10.2` in the listener peer/ARP table.
- CoreDevice: iPad is a physical paired iPad Air 4, UDID `00008101-001A68EE21F0001E`; `transportType=localNetwork`, `tunnelState=connected`, `tunnelTransportProtocol=tcp`. The Mac also had active `awdl0`/`llw0`, but that is only Apple peer/CoreDevice evidence, not app-path evidence.
- CoreDevice supplied three `.coredevice.local` names, including the UDID name. Bounded `dns-sd -G v4` queries for all three returned no address, so hostname/Bonjour resolution was not independently demonstrated.
- Mac advertised `iPadHotspotProbe._hotspotprobe._tcp.local` on port 48123 and the Mac's own browse saw it. The iPad browse returned `Network.NWError ... -65555 - NoAuth`; this is a Local Network authorization failure, not evidence of hotspot isolation.
- Direct app-to-Mac HTTP path: **TCP/HTTP reached the Mac**. The Mac received `POST /report` from `172.20.10.2:63815` and returned `200 report-ok`. The app's initial `GET /health` reported `The Internet connection appears to be offline`, but the later POST to the same exact private IP/port succeeded, which is decisive proof that a normal iPad app reached the Mac listener. This also shows no client isolation for unicast TCP; multicast Bonjour remains unproven because of `NoAuth`.
- The app was installed/launched only on the physical iPad through CoreDevice, then uninstalled. The listener and Bonjour advertiser were stopped; derived data and temporary logs were removed. No simulator, PM3 hardware, firmware, RF, or token operations were used.

## Authorization follow-up result (2026-09-16)

With Local Network access allowed on the iPad and the Mac firewall request accepted, the rebuilt app completed the missing checks in one run:

- Mac received `GET /health` from `172.20.10.2:63922` and returned `HTTP 200` with `iPadHotspotProbe mac-ok`.
- iPad `NWBrowser` discovered `iPadHotspotProbe._hotspotprobe._tcp.local` and reported `resolved; TCP connection ready` for the advertised service endpoint. The app then posted that result to the same Mac listener, which returned HTTP 200.
- Final conclusion: the iPhone-hotspot path supports direct iPad-app-to-Mac TCP/HTTP and Bonjour discovery/resolution. No client isolation was observed. Remaining caveat: the CoreDevice `.coredevice.local` hostname lookup itself was still not independently resolved; that is separate from the successful app Bonjour test.
