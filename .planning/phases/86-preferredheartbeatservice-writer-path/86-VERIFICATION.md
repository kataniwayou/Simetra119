---
phase: 86-preferredheartbeatservice-writer-path
verified: 2026-03-26T00:00:00Z
status: passed
score: 16/16 must-haves verified
---

# Phase 86: PreferredHeartbeatService Writer Path - Verification Report

**Phase Goal:** The preferred pod stamps the heartbeat lease only after it is genuinely ready, giving non-preferred pods an accurate presence signal that does not trigger premature yield
**Verified:** 2026-03-26T00:00:00Z
**Status:** passed
**Re-verification:** No - initial verification

## Goal Achievement

### Observable Truths - Plan 86-01

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Preferred pod creates the heartbeat lease on first tick after ApplicationStarted fires | VERIFIED | WriteHeartbeatLeaseAsync called inside `if (_preferredLeaderService.IsPreferredPod && _isSchedulerReady)`; test 9 confirms Create called on tick 1 |
| 2 | Preferred pod renews the heartbeat lease on subsequent ticks with updated renewTime | VERIFIED | Replace path uses _cachedResourceVersion; RenewTime = now on every tick; test 10 confirms Replace on tick 2 with rv-100 |
| 3 | Non-preferred pod silently skips the write path with no log output | VERIFIED | No log statement in the else branch; test 12 confirms no Create/Replace calls on non-preferred job |
| 4 | Writer path is a no-op before ApplicationStarted fires, even on preferred pod | VERIFIED | Gate is _isSchedulerReady set only by lifetime.ApplicationStarted.Register; test 11 confirms skip when schedulerReady: false |
| 5 | Write-before-read ordering: preferred pod stamps then reads its own stamp | VERIFIED | WriteHeartbeatLeaseAsync called before ReadAndUpdateStampFreshnessAsync in Execute (lines 79-85 of job file) |
| 6 | Transient write errors are logged as Warning and do not crash the job | VERIFIED | catch (Exception ex) in WriteHeartbeatLeaseAsync calls LogWarning; test 16 confirms job does not throw and liveness is stamped |
| 7 | TTL expiry handles shutdown - no explicit lease delete added | VERIFIED | No DeleteNamespacedLeaseAsync call anywhere in PreferredHeartbeatJob.cs; no changes to GracefulShutdownService |

### Observable Truths - Plan 86-02

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 8 | Existing reader tests (8 tests) still pass after constructor signature change | VERIFIED | Tests 1-8 pass; total 17/17 pass per dotnet test run |
| 9 | Writer path creates heartbeat lease when preferred + ready | VERIFIED | Test 9: Execute_PreferredAndReady_CreatesHeartbeatLease - Received(1) on Create with correct HolderIdentity, RenewTime, AcquireTime, DurationSeconds |
| 10 | Writer path skips silently when not preferred | VERIFIED | Test 12: Execute_NotPreferred_SkipsWrite - DidNotReceive on Create and Replace |
| 11 | Writer path skips silently when not ready (ApplicationStarted has not fired) | VERIFIED | Test 11: Execute_PreferredButNotReady_SkipsWrite - DidNotReceive on Create and Replace |
| 12 | 409 Conflict on create triggers read-then-replace fallback | VERIFIED | Test 13: Execute_CreateConflict409_FallsBackToReadThenReplace - Replace called with rv-existing |
| 13 | 409 Conflict on replace invalidates cached resourceVersion | VERIFIED | Test 14: Execute_ReplaceConflict409_InvalidatesCache - tick 3 calls Create again (not Replace) |
| 14 | 404 on replace invalidates cached resourceVersion | VERIFIED | Test 15: Execute_Replace404_InvalidatesCache - tick 3 calls Create again |
| 15 | Transient write errors are caught and do not crash Execute | VERIFIED | Test 16: Execute_WriteTransientError_LogsWarningAndContinuesRead - no throw, liveness stamped, reader ran |
| 16 | Liveness stamp fires regardless of writer errors | VERIFIED | finally { _liveness.Stamp(jobKey); } in Execute; confirmed by tests 4, 8, 16 |

**Score:** 16/16 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/SnmpCollector/Jobs/PreferredHeartbeatJob.cs` | Writer path, readiness gate, write-before-read Execute | VERIFIED | 263 lines; contains WriteHeartbeatLeaseAsync, _isSchedulerReady, _cachedResourceVersion, _podIdentity; no stub patterns |
| `tests/SnmpCollector.Tests/Jobs/PreferredHeartbeatJobTests.cs` | 8 reader + 9 writer tests; updated constructor | VERIFIED | 17 tests confirmed by dotnet test; file spans ~960 lines; substantive assertions throughout |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| PreferredHeartbeatJob constructor | IHostApplicationLifetime.ApplicationStarted | lifetime.ApplicationStarted.Register() | WIRED | Line 68: `lifetime.ApplicationStarted.Register(() => _isSchedulerReady = true);` |
| PreferredHeartbeatJob.Execute | WriteHeartbeatLeaseAsync | if (_preferredLeaderService.IsPreferredPod && _isSchedulerReady) | WIRED | Lines 79-82: conditional call confirmed; no log on skip |
| WriteHeartbeatLeaseAsync | CreateNamespacedLeaseAsync | Create branch when _cachedResourceVersion is null | WIRED | Lines 147-153: create call, caches created.Metadata.ResourceVersion |
| WriteHeartbeatLeaseAsync | ReplaceNamespacedLeaseAsync | Replace branch with _cachedResourceVersion | WIRED | Lines 187-193: replace call, updates _cachedResourceVersion |
| V1LeaseSpec.HolderIdentity | PodIdentityOptions.PodIdentity | _podIdentity field resolved in constructor | WIRED | Line 62: `_podIdentity = podIdentityOptions.Value.PodIdentity ?? Environment.MachineName;`; used on line 119 |
| PreferredHeartbeatJobTests constructor | PreferredHeartbeatJob new signature | IOptions<PodIdentityOptions>, IHostApplicationLifetime mocks | WIRED | Lines 72-90; MakeLifetime and MakePreferredJob helpers cover all wiring |

### Requirements Coverage

| Requirement | Status | Evidence |
|-------------|--------|----------|
| HB-01 | SATISFIED | Lease stamped with HolderIdentity = _podIdentity and RenewTime = now; verified by test 9 (Arg.Is on HolderIdentity, RenewTime, DurationSeconds) |
| HB-02 | SATISFIED | Stamping gated by _isSchedulerReady (ApplicationStarted) AND IsPreferredPod; verified by tests 11 (not-ready skip) and 12 (non-preferred skip) |
| HB-03 | SATISFIED | No DeleteNamespacedLeaseAsync anywhere in job file; TTL expiry is the sole shutdown mechanism; test 17 confirms AcquireTime only on create (renewal semantics correct) |

### Anti-Patterns Found

None. No TODOs, FIXMEs, placeholders, empty returns, or stub patterns found in either key file.

### Human Verification Required

None. All behaviors are verifiable at unit-test level:

- Readiness gate: verified via pre-cancelled CancellationTokenSource (fires synchronously in constructor)
- Non-preferred skip: verified via PHYSICAL_HOSTNAME env var not matching PreferredNode
- 409/404 conflict recovery: verified via NSubstitute HttpOperationException mocks
- AcquireTime only on create: verified via Arg.Do<V1Lease> capture in test 17

E2E confirmation (preferred pod heartbeat visible in cluster) is deferred to Phase 89 per the phase roadmap.

### Gaps Summary

No gaps. All 16 must-have truths are verified against actual code. The implementation matches the plan exactly:

- WriteHeartbeatLeaseAsync implements create-or-replace with cached resourceVersion as specified
- IHostApplicationLifetime.ApplicationStarted.Register sets volatile bool _isSchedulerReady
- Execute orders write before read with silent skip for non-preferred pods
- 9 new writer-path tests (tests 9-17) plus 8 passing reader tests = 17 total, all green
- Build: 0 errors, 0 warnings

---

_Verified: 2026-03-26_
_Verifier: Claude (gsd-verifier)_
