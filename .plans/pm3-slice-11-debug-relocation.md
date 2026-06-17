# Slice 11 — Debug Tooling Relocation (S4.11)

**Status:** 🔲 Not started  
**Depends on:** None (can land early)  
**Branch:** `pm3-integration`

## Goal

Keep debug tooling; move under a dedicated `debug/` folder.

## Target layout

```
debug/
  README.md                 # how to run probe, capture, load-test via dotnet test
  NativeT55Probe/
    Program.cs
    CaptureMain.cs
    LoadTestMain.cs
    NativeT55Probe.csproj   # linked compile to NativeRideLoadTestRunner.cs
  scripts/
    check-com4.ps1
```

## Tasks

- [ ] `git mv NativeT55Probe debug/NativeT55Probe`
- [ ] `git mv scripts/check-com4.ps1 debug/scripts/check-com4.ps1`
- [ ] Remove empty `scripts/` if unused
- [ ] Add `debug/README.md` with commands:
  - `dotnet run --project debug/NativeT55Probe -- --capture`
  - `dotnet test --filter Pm3NativeLoadTests -- NUnit.RunExplicitTests=true`
- [ ] Update linked path in `NativeT55Probe.csproj` to `Pm3UsbApi.Tests/Integration/NativeRideLoadTestRunner.cs`
- [ ] Update master plan paths, `.plans` references, fixture re-capture docs
- [ ] Add to `ElevatorTokens.sln` if not already (optional)

## Done when

All probe/script paths updated; builds succeed; master plan S4.11 marked `[x]`.
