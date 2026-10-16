# Unknown token dumps — 2026-10-16

Collected read-only from the paired physical iPad app container. Raw bytes are unmodified. See `manifest.json` for hashes, original source filenames, timestamps, all eight big-endian blocks, and per-mirror decode checks. `cases/` contains one `.bin` per unique SHA-256, grouped by blocks 1–4.

## Collection scope

Archived 12 files (12 unique byte dumps), by source-file modification date (UTC): 2 on 2026-09-18, 10 on 2026-10-16. A dump-store filename can be reused/overwritten by repeated scans; its current bytes and source modification timestamp do not reveal individual scan/read history or prove these were distinct reads. These are source-file inventory counts, not ride counts.

| Case | Original source file(s) | SHA-256 (prefix) | Block 5 candidate | Block 6 candidate | Assessment |
|---|---|---|---|---|---|
| `0BFE002A–F1003A6A–B3D15920–B3D15920` | `elevator-t55xx-00148040-0BFE002A-F1003A6A-B3D15920-B3D15920-1E121FCE-1E121FCE-FB9AF557--rides-UNKNOWN.bin` | `d6d7af3f2163346a` | none | none | `no_known_mirror_decode` |
| `23FE002A–EFF2531A–C6041232–C6041232` | `elevator-t55xx-00148040-23FE002A-EFF2531A-C6041232-C6041232-A4124F37-A4124F37-072D5FDE--rides-UNKNOWN.bin` | `df8df1adaa3ea281` | none | none | `no_known_mirror_decode` |
| `23FE007B–D88C980F–A1046931–A1046931` | `elevator-t55xx-00148040-23FE007B-D88C980F-A1046931-A1046931-3E1229AF-3E1229AF-00000000--rides-UNKNOWN.bin` | `699f4d0d0fa00a2b` | none | none | `no_known_mirror_decode` |
| `43FE0062–5BA494A3–2CF11806–2CF11806` | `elevator-t55xx-5BA494A3-43FE0062-5BA494A3-2CF11806-2CF11806-1D12265E-D7B5C88C-96788C03--rides-UNKNOWN.bin` | `0ec981829532362c` | none | none | `mirror_disagreement_no_known_decode` |
| `43FE0062–5BA494A3–5BA494A3–2CF11806` | `elevator-t55xx-5BA494A3-43FE0062-5BA494A3-5BA494A3-2CF11806-1D12265E-1D12265E-96788C03--rides-UNKNOWN.bin` | `53010c086f02334d` | none | none | `no_known_mirror_decode` |
| `4BFE004B–A97E3A0F–8DD1E7EE–8DD1E7EE` | `elevator-t55xx-00148040-4BFE004B-A97E3A0F-8DD1E7EE-8DD1E7EE-0912F667-0912F667-00000000--rides-UNKNOWN.bin` | `b51b51e1176d436e` | none | none | `no_known_mirror_decode` |
| `4BFE0062–5BA4A323–D6D1C733–D6D1C733` | `elevator-t55xx-00148040-4BFE0062-5BA4A323-D6D1C733-D6D1C733-C1125711-C1125711-00000000--rides-UNKNOWN.bin` | `3a6c1969a949641b` | none | none | `no_known_mirror_decode` |
| `53FE004A–83E813BF–D7D1C723–D7D1C723` | `elevator-t55xx-00148040-53FE004A-83E813BF-D7D1C723-D7D1C723-5813794B-5813794B-AF2E8EC1--rides-UNKNOWN.bin` | `b1517a766b5922e3` | none | none | `no_known_mirror_decode` |
| `7BFE002A–F100CA69–620402F1–620402F1` | `elevator-t55xx-00148040-7BFE002A-F100CA69-620402F1-620402F1-F0C7F520-F0C7F520-B7659F13--rides-UNKNOWN.bin` | `44041cf4638e6b20` | none | none | `no_known_mirror_decode` |
| `D3FE002A–F1003C51–B2D15921–B2D15921` | `elevator-t55xx-00148040-D3FE002A-F1003C51-B2D15921-B2D15921-0EC7C25E-0EC7C25E-2F9A57AD--rides-UNKNOWN.bin` | `238fd2314db4f703` | none | none | `no_known_mirror_decode` |
| `DBFE0031–20C601D7–96D14937–96D14937` | `elevator-t55xx-00148040-DBFE0031-20C601D7-96D14937-96D14937-27120BF0-27120BF0-00000000--rides-UNKNOWN.bin` | `fdfbbcbd2504c91a` | none | none | `no_known_mirror_decode` |
| `FF800000–0034A800–00000000–00000000` | `elevator-t55xx-00148041-FF800000-0034A800-00000000-00000000-00000000-00000000-00000000--rides-UNKNOWN.bin` | `ded4b0c0b14609db` | none | none | `no_known_mirror_decode` |

## Interpretation and limits

A successful registry match is only a current decoder candidate for that one mirror block, not proof of the token's true ride count or sequence. A non-match means only that the current registered RideSequence/RideCounterCodec did not validate the block. Differing block 5/6 values are a mirror disagreement; preserve and investigate both instead of assuming either is correct. Equal but undecodable mirrors are not evidence by themselves of a new encoding: corruption, incomplete/partial reads, other token families, or an unsupported sequence remain possibilities. No ride counts are inferred where the decoder does not match.

## Follow-up experimental protocol

1. Select a known token and record its identity blocks and independently established ride count; keep the physical iPad/app and token identity fixed.
2. Capture page 0 at a documented baseline count, then again after exactly one known ride (and optionally additional explicitly counted increments). Keep each raw eight-block, 32-byte dump and SHA-256; do not overwrite earlier evidence.
3. Record capture time, count evidence, reader/app version, token identity blocks 1–4, and both block 5 and 6 values for every capture. Repeat the pair to distinguish reproducible transitions from read noise.
4. Compare per-block deltas across the paired captures; test registry candidates against both raw mirrors and all paired deltas. Track disagreement/corruption separately from a repeatable encoding hypothesis.
5. Change one experimental variable at a time; preserve raw originals and hashes before analysis. Do not write/modify tokens during this collection or use these dumps as a write recipe without separate authorization and validation.

## Provenance

The archive was copied from the app container using `xcrun devicectl device copy from` (read-only receive operation). The source-side modification date is the `devicectl device info files` metadata timestamp. Host staging-file mtime is recorded separately; file transfer may preserve that timestamp, so it is not an independent transfer or capture time. Neither timestamp is asserted to be the actual token-read time. The manifest records app version/build and device model/OS where available.
