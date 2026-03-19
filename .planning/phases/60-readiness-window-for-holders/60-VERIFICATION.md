---
phase: 60-readiness-window-for-holders
verified: 2026-03-19T17:18:19Z
status: passed
score: 8/8 must-haves verified
re_verification: false
---

# Phase 60: Readiness Window for Holders Verification Report

**Phase Goal:** Replace the sentinel sample in MetricSlotHolder with a readiness grace window (TimeSeriesSize x IntervalSeconds x GraceMultiplier), so tenants are not evaluated until enough time has passed for all metric slots to fill with real data, eliminating false threshold results from sentinel zeros and startup race conditions.

**Verified:** 2026-03-19T17:18:19Z
**Status:** passed
**Re-verification:** No - initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | MetricSlotHolder constructor no longer creates a sentinel sample - series starts empty | VERIFIED | _box = SeriesBox.Empty at field init; ReadSlot() returns null before first write; tests ReadSlot_BeforeAnyWrite_ReturnsNull and ReadSeries_BeforeAnyWrite_ReturnsEmpty confirm |
| 2 | Each holder has ConstructedAt and ReadinessGrace = TimeSeriesSize x IntervalSeconds x GraceMultiplier | VERIFIED | ConstructedAt property initializer at line 48; ReadinessGrace computed property at lines 54-55; test ReadinessGrace_ComputedFromProperties asserts 3 x 30 x 2.0 = 180s |
| 3 | SnapshotJob skips tenants where any holder has not passed readiness grace - returns Unresolved | VERIFIED | EvaluateTenant calls AreAllReady(tenant.Holders) as first gate (line 137); returns TierResult.Unresolved immediately if false; 3 pre-tier tests confirm |
| 4 | Staleness check unchanged: IntervalSeconds x GraceMultiplier from newest real sample timestamp | VERIFIED | HasStaleness computes graceWindow = TimeSpan.FromSeconds(holder.IntervalSeconds * holder.GraceMultiplier); logic unchanged except null-slot now returns true (post-readiness stale) |
| 5 | Threshold evaluation only runs on real samples - empty holders are skipped | VERIFIED | AreAllResolvedViolated and AreAllEvaluateViolated both guard series.Length == 0 with continue; Trap/Command null slot also continues |
| 6 | MTS-03 startup race eliminated - P1 is not ready during fill window, not falsely Healthy | VERIFIED | AreAllReady returns false for fresh unwritten holders; EvaluateTenant returns Unresolved for not-ready tenants, blocking advance gate; MTS-03 E2E uses 90s timeout absorbing grace window |
| 7 | All existing E2E scenarios pass with the new readiness logic | VERIFIED | STS-05 priming comment updated to reference readiness grace window; MTS-03 header notes 90s timeout absorbs grace window; E2E structure unchanged |
| 8 | All unit tests updated for sentinel removal | VERIFIED | ReadSlot_BeforeAnyWrite_ReturnsNull, ReadSeries_BeforeAnyWrite_ReturnsEmpty, CopyFrom comment confirms no sentinel; TenantVectorRegistryTests.Reload_NewMetric_StartsEmpty asserts slot == null; all 462 tests pass |

**Score:** 8/8 truths verified

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| src/SnmpCollector/Pipeline/MetricSlotHolder.cs | Sentinel-free with ConstructedAt, ReadinessGrace, IsReady | VERIFIED | 127 lines; _box = SeriesBox.Empty; all three new properties present and substantive |
| src/SnmpCollector/Jobs/SnapshotJob.cs | AreAllReady pre-tier gate, null-slot-as-stale | VERIFIED | 408 lines; AreAllReady private static at line 225; called at line 137 first in EvaluateTenant; null slot in HasStaleness returns true at line 250 |
| tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs | Updated for sentinel removal, new readiness tests | VERIFIED | 269 lines; 21 tests; all readiness paths covered (FreshHolder, HolderWithData, TinyGrace, CopyFromWithData) |
| tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs | Pre-tier readiness tests, sentinel tests rewritten | VERIFIED | 60 tests (up from 57); pre-tier section at lines 80-133; 4 readiness-specific tests |
| tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs | Orphaned sentinel test fixed | VERIFIED | Reload_NewMetric_StartsEmpty at line 299; asserts Assert.Null(slot) with comment confirming sentinel removal |
| tests/e2e/scenarios/33-sts-05-staleness.sh | Priming comment references readiness grace window | VERIFIED | Line 40: Waiting 20s to satisfy readiness grace window and populate fresh poll timestamps |
| tests/e2e/scenarios/40-mts-03-starvation-proof.sh | Phase 60 grace window note | VERIFIED | Lines 14-15: Phase 60 note: the 90s poll timeout (MTS-03A) naturally absorbs the readiness grace window |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| MetricSlotHolder.IsReady | SnapshotJob.AreAllReady | holder.IsReady in foreach | WIRED | AreAllReady delegates to holder.IsReady at line 229 |
| SnapshotJob.AreAllReady | EvaluateTenant pre-tier gate | if (!AreAllReady(tenant.Holders)) | WIRED | First conditional in EvaluateTenant body (line 137); returns TierResult.Unresolved if false |
| _box = SeriesBox.Empty | ReadSlot() returns null | s.Length > 0 ? last : null | WIRED | Line 104; empty series produces null cleanly |
| ReadSeries().Length > 0 | IsReady short-circuit | first clause of IsReady | WIRED | Line 63; data presence bypasses time check, enabling immediate readiness after CopyFrom |
| HasStaleness null slot | return true (stale) | if (slot is null) return true | WIRED | Lines 249-250; replaces former continue; safe post-readiness gate guarantee |
| AreAllResolvedViolated / AreAllEvaluateViolated | empty series skipped | series.Length == 0 continue | WIRED | Lines 296-297 and 348-349 respectively |

---

### Requirements Coverage

| Requirement | Status | Blocking Issue |
|-------------|--------|----------------|
| Sentinel zeros eliminated | SATISFIED | _box = SeriesBox.Empty; no WriteValue in constructor |
| Startup race (MTS-03) eliminated | SATISFIED | Not-ready tenants return Unresolved; advance gate blocks during fill window |
| Staleness detection logic unchanged | SATISFIED | HasStaleness grace window calculation unchanged; only null-slot behavior altered |
| Threshold evaluation skips empty holders | SATISFIED | series.Length == 0 guard in both AreAllResolvedViolated and AreAllEvaluateViolated |
| Config reload holders immediately ready | SATISFIED | IsReady short-circuits true when ReadSeries().Length > 0; CopyFrom transfers real data |

---

### Anti-Patterns Found

None. No TODO/FIXME, no placeholder content, no empty handlers found in any modified file.

---

### Human Verification Required

None - all success criteria are verifiable through code structure and the unit test suite (462/462 passing).

---

## Summary

Phase 60 fully achieved its goal. The sentinel initialization was cleanly removed from MetricSlotHolder - _box starts as SeriesBox.Empty and ReadSlot() returns null before any write. Three new properties (ConstructedAt, ReadinessGrace, IsReady) implement the grace window logic correctly: data presence short-circuits to ready immediately (handling config reload via CopyFrom), and elapsed time past TimeSeriesSize x IntervalSeconds x GraceMultiplier is the fallback for fresh holders with no data.

The SnapshotJob pre-tier gate is the first check in EvaluateTenant, returning TierResult.Unresolved for any tenant with a not-ready holder - this blocks the advance gate identically to a real device Unresolved result, eliminating the MTS-03 startup race. The HasStaleness null-slot change from continue to return true is safe and correct post-readiness: a null slot after the grace window means the device never responded.

All 462 unit tests pass. E2E scenario comments were updated to reflect the new readiness semantics.

---

_Verified: 2026-03-19T17:18:19Z_
_Verifier: Claude (gsd-verifier)_
