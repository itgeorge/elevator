# WriteBenchmark

Native USB benchmark for `set`, `add`, and `reset` ride operations.

```bash
dotnet run --project debug/WriteBenchmark/WriteBenchmark.csproj -c Release
```

## Saved results (Jun 2026, token on reader)

| Operation | Pre-optimization | Post-optimization |
|---|---|---|
| read | 1.100s | 1.100s |
| set 55 | 4.615s | 2.479s |
| add/set 60 | 4.588s | 2.471s |
| reset | 14.291s | 8.260s |

**Pre-optimization** (`benchmark-pre-set-reset-optimization.txt`): per-op PM3 calls with tune+dump on reset, individual writes/reads, detect cache cleared on every write, post-set full dump.

**Post-optimization** (`benchmark-post-set-reset-optimization.txt`): batched write+verify, no post-set dump, reset without tune/dump, detect cache kept across writes.

The benchmark program reflects the **post-optimization** code paths. Re-run after further changes and compare against these baselines.
