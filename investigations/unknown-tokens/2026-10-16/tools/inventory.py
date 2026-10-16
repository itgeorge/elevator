#!/usr/bin/env python3
"""Archive and inventory copied 32-byte page-0 dumps; standard library only."""
import argparse
import datetime as dt
import hashlib
import json
import shutil
from pathlib import Path

SEQUENCES = {
    "mercury": (0xCCC749CC, 4), "venus": (0x48C74948, 4),
    "earth": (0x18121218, 4), "pluto": (0x1F12121F, 4),
    "mars": (0x4EC7494E, 4), "jupiter": (0x8C124980, 0),
    "saturn": (0x8B1249F0, 0), "uranus": (0x891249D0, 0),
    "neptune": (0x8F1249B0, 0), "charon": (0xC0121244, 0),
    "nix": (0x0DC7C70D, 4),
}

def decode(block):
    matches = []
    for name, (zero, rotation) in SEQUENCES.items():
        delta = block ^ zero
        high = (delta >> 16) & 0xff
        if high > 1:
            continue
        rides = (high << 8) | ((delta >> 8) & 0xff)
        if rides > 500:
            continue
        low = rides & 0xff
        high_byte = (rides >> 8) & 0xff
        payload = ((low << rotation) | (low >> (8 - rotation))) & 0xff if rotation else low
        payload ^= (high_byte << rotation) & 0xff
        first = 0xF3 if payload & 8 else 0
        expected = (first << 24) | (high_byte << 16) | (low << 8) | payload
        if expected == delta and (zero ^ expected) == block:
            matches.append({"sequence": name, "rides": rides})
    return matches

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--source", type=Path, help="copied UnknownDumps directory")
    p.add_argument("--listing", type=Path, help="devicectl files --json-output result")
    p.add_argument("--apps-info", type=Path, help="devicectl apps --json-output result")
    p.add_argument("--device-info", type=Path, help="devicectl details --json-output result")
    p.add_argument("--out", type=Path, default=Path(__file__).resolve().parents[1])
    p.add_argument("--self-test", action="store_true", help="run known RideSequence decoder vector and exit")
    a = p.parse_args()
    if a.self_test:
        matches = decode(0x8F1349B1)
        assert matches == [{"sequence": "neptune", "rides": 256}], matches
        print("PASS: neptune encode(256) vector 0x8F1349B1 decodes to neptune/256")
        return
    if not a.source or not a.listing:
        p.error("--source and --listing are required unless --self-test is used")
    source = a.source.resolve()
    listing = json.loads(a.listing.read_text())
    apps_info = json.loads(a.apps_info.read_text()) if a.apps_info else {}
    device_info = json.loads(a.device_info.read_text()) if a.device_info else {}
    app = next(iter(apps_info.get("result", {}).get("apps", [])), {})
    device = device_info.get("result", {})
    by_name = {item["name"]: item for item in listing["result"]["files"]}
    out = a.out.resolve()
    records, groups = [], {}
    files = sorted(source.glob("*.bin"))
    if not files:
        raise SystemExit(f"No .bin source files in {source}")
    for path in files:
        raw = path.read_bytes()
        if len(raw) != 32:
            raise SystemExit(f"Expected exactly 32 bytes, got {len(raw)}: {path}")
        words = [int.from_bytes(raw[i:i+4], "big") for i in range(0, 32, 4)]
        digest = hashlib.sha256(raw).hexdigest()
        identity = "-".join(f"{w:08X}" for w in words[1:5])
        group = groups.setdefault(identity, {})
        if digest not in group:
            dest = out / "cases" / identity / f"sha256-{digest}.bin"
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, dest)
            group[digest] = dest.relative_to(out).as_posix()
        entry = by_name.get(path.name)
        remote_mtime = entry.get("metadata", {}).get("lastModDate") if entry else None
        decoded5, decoded6 = decode(words[5]), decode(words[6])
        if words[5] != words[6]:
            classification = "mirror_disagreement" if decoded5 or decoded6 else "mirror_disagreement_no_known_decode"
        elif decoded5:
            classification = "known_mirror_decode_candidate"
        else:
            classification = "no_known_mirror_decode"
        records.append({
            "source_filename": path.name,
            "source_relative_path": entry.get("relativePath", path.name) if entry else path.name,
            "source_device_mtime_utc": remote_mtime,
            "source_metadata_size_bytes": entry.get("metadata", {}).get("size") if entry else None,
            "staging_file_mtime_utc": dt.datetime.fromtimestamp(path.stat().st_mtime, dt.timezone.utc).isoformat(),
            "staging_file_mtime_matches_device_mtime": bool(remote_mtime and dt.datetime.fromtimestamp(path.stat().st_mtime, dt.timezone.utc) == dt.datetime.fromisoformat(remote_mtime.replace("Z", "+00:00"))),
            "staging_file_mtime_is_not_capture_time": True,
            "sha256": digest,
            "bytes": len(raw),
            "blocks_big_endian_hex": [f"{w:08X}" for w in words],
            "identity_blocks_1_to_4": [f"{w:08X}" for w in words[1:5]],
            "archive_path": group[digest],
            "block5_decode_candidates": decoded5,
            "block6_decode_candidates": decoded6,
            "mirrors_equal": words[5] == words[6],
            "classification": classification,
        })
    manifest = {
        "schema_version": 1,
        "collection_date_local": "2026-10-16",
        "device_identifier": listing["result"].get("deviceIdentifier"),
        "bundle_identifier": listing["result"].get("domainIdentifier"),
        "app_version": app.get("version"),
        "app_build": app.get("bundleVersion"),
        "device_model": device.get("hardwareProperties", {}).get("marketingName"),
        "device_os_version": device.get("deviceProperties", {}).get("osVersionNumber"),
        "devicectl_version": listing.get("info", {}).get("version"),
        "source_domain": listing["result"].get("domain"),
        "source_directory": "Library/Application Support/RidesTablet/UnknownDumps",
        "source_file_count": len(records),
        "source_mtime_date_counts_utc": {date: sum(r["source_device_mtime_utc"].startswith(date) for r in records) for date in sorted({r["source_device_mtime_utc"][:10] for r in records if r["source_device_mtime_utc"]})},
        "unique_byte_dumps": len({r["sha256"] for r in records}),
        "archive_case_count": sum(len(g) for g in groups.values()),
        "identity_group_count": len(groups),
        "deduplication": "Archive one byte-for-byte .bin per SHA-256; every original source filename remains represented in records.",
        "records": records,
    }
    (out / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    date_breakdown = ", ".join(f"{count} on {date}" for date, count in manifest["source_mtime_date_counts_utc"].items()) or "source modification date unavailable"
    lines = ["# Unknown token dumps — 2026-10-16", "", "Collected read-only from the paired physical iPad app container. Raw bytes are unmodified. See `manifest.json` for hashes, original source filenames, timestamps, all eight big-endian blocks, and per-mirror decode checks. `cases/` contains one `.bin` per unique SHA-256, grouped by blocks 1–4.", "", "## Collection scope", "", f"Archived {len(records)} files ({len({r['sha256'] for r in records})} unique byte dumps), by source-file modification date (UTC): {date_breakdown}. A dump-store filename can be reused/overwritten by repeated scans; its current bytes and source modification timestamp do not reveal individual scan/read history or prove these were distinct reads. These are source-file inventory counts, not ride counts.", "", "| Case | Original source file(s) | SHA-256 (prefix) | Block 5 candidate | Block 6 candidate | Assessment |", "|---|---|---|---|---|---|"]
    by_case = {}
    for r in records:
        key = (tuple(r["identity_blocks_1_to_4"]), r["sha256"])
        by_case.setdefault(key, []).append(r)
    for (ident, digest), rows in sorted(by_case.items()):
        r = rows[0]
        sources = ", ".join(x["source_filename"] for x in rows)
        def candidate(matches):
            return ", ".join(f"{x['sequence']} / {x['rides']}" for x in matches) or "none"
        lines.append(f"| `{ident[0]}–{ident[1]}–{ident[2]}–{ident[3]}` | `{sources}` | `{digest[:16]}` | {candidate(r['block5_decode_candidates'])} | {candidate(r['block6_decode_candidates'])} | `{r['classification']}` |")
    lines += ["", "## Interpretation and limits", "", "A successful registry match is only a current decoder candidate for that one mirror block, not proof of the token's true ride count or sequence. A non-match means only that the current registered RideSequence/RideCounterCodec did not validate the block. Differing block 5/6 values are a mirror disagreement; preserve and investigate both instead of assuming either is correct. Equal but undecodable mirrors are not evidence by themselves of a new encoding: corruption, incomplete/partial reads, other token families, or an unsupported sequence remain possibilities. No ride counts are inferred where the decoder does not match.", "", "## Follow-up experimental protocol", "", "1. Select a known token and record its identity blocks and independently established ride count; keep the physical iPad/app and token identity fixed.", "2. Capture page 0 at a documented baseline count, then again after exactly one known ride (and optionally additional explicitly counted increments). Keep each raw eight-block, 32-byte dump and SHA-256; do not overwrite earlier evidence.", "3. Record capture time, count evidence, reader/app version, token identity blocks 1–4, and both block 5 and 6 values for every capture. Repeat the pair to distinguish reproducible transitions from read noise.", "4. Compare per-block deltas across the paired captures; test registry candidates against both raw mirrors and all paired deltas. Track disagreement/corruption separately from a repeatable encoding hypothesis.", "5. Change one experimental variable at a time; preserve raw originals and hashes before analysis. Do not write/modify tokens during this collection or use these dumps as a write recipe without separate authorization and validation.", "", "## Provenance", "", "The archive was copied from the app container using `xcrun devicectl device copy from` (read-only receive operation). The source-side modification date is the `devicectl device info files` metadata timestamp. Host staging-file mtime is recorded separately; file transfer may preserve that timestamp, so it is not an independent transfer or capture time. Neither timestamp is asserted to be the actual token-read time. The manifest records app version/build and device model/OS where available.", ""]
    (out / "README.md").write_text("\n".join(lines))
    print(f"sources={len(records)} unique_sha256={manifest['unique_byte_dumps']} archived_cases={manifest['archive_case_count']} identity_groups={len(groups)} out={out}")

if __name__ == "__main__":
    main()
