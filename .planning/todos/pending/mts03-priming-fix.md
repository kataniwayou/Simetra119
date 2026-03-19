---
title: Fix MTS-03 starvation test — add priming period before P2 absence assertion
area: e2e-tests
created: 2026-03-19
files:
  - tests/e2e/scenarios/40-mts-03-starvation-proof.sh
---

### Problem

MTS-03C fails because P2 gets evaluated during the first SnapshotJob cycle after `command_trigger` is set. The evaluate metric (TimeSeriesSize=3) needs 3 poll cycles (30s) to fill all slots with violated values. During the fill window, P1 is Healthy → gate advances → P2 evaluated.

### Solution

Add priming period: set `command_trigger`, wait 45s (3×10s polls + margin) for time series to fill, THEN baseline and start starvation assertion. Same pattern as STS-05/STS-06 staleness priming.
