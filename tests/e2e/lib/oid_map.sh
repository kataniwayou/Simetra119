#!/usr/bin/env bash
# oid_map.sh — OID mapping for v2.6 4-tenant E2E fixture (tenant-cfg12-v26-four-tenant.yaml)
#
# PURPOSE
#   Sourced by the Phase 83 interactive command interpreter. Provides per-tenant
#   per-role OID lookup so the interpreter can translate command patterns into
#   simulator HTTP calls without re-parsing the fixture YAML at runtime.
#
# KEY FORMAT
#   OID_MAP[TENANT.ROLE.N.FIELD]
#
#   TENANT : T1_P1 | T2_P1 | T1_P2 | T2_P2
#   ROLE   : E (Evaluate) | R (Resolved)
#   N      : 1-based slot index within that role for the tenant
#   FIELD  : oid      — OID suffix passed to sim_set_oid (e.g. "8.1")
#            healthy  — value representing a healthy (non-violated) reading
#            violated — value representing a violated reading
#
# USAGE
#   source "$(dirname "${BASH_SOURCE[0]}")/oid_map.sh"
#   suffix="${OID_MAP[T1_P1.E.1.oid]}"       # => "8.1"
#   val="${OID_MAP[T1_P1.E.1.violated]}"      # => "0"
#
# HOW TO ADD A NEW TENANT
#   Assign a new subtree (12, 13, ...) and add one block below (one line per OID).
#   Update TENANT_EVAL_COUNT, TENANT_RES_COUNT, and VALID_TENANTS.
#
# HOW TO ADD A NEW METRIC TO AN EXISTING TENANT
#   Append one line for the next slot index, update TENANT_EVAL_COUNT or
#   TENANT_RES_COUNT for the tenant, and register the OID in the simulator and
#   simetra-oid-metric-map.yaml.
#
# OID SUBTREE ASSIGNMENTS (1.3.6.1.4.1.47477.999.<subtree>.<slot>)
#   .8.x  => T1_P1 (Priority 1, 2E + 2R)
#   .9.x  => T2_P1 (Priority 1, 4E + 4R)
#   .10.x => T1_P2 (Priority 2, 2E + 2R)
#   .11.x => T2_P2 (Priority 2, 4E + 4R)

declare -A OID_MAP

# ---------------------------------------------------------------------------
# T1_P1: Priority 1 — 2 Evaluate, 2 Resolved — subtree 8
# ---------------------------------------------------------------------------
OID_MAP[T1_P1.E.1.oid]="8.1";   OID_MAP[T1_P1.E.1.healthy]="10"; OID_MAP[T1_P1.E.1.violated]="0"
OID_MAP[T1_P1.E.2.oid]="8.2";   OID_MAP[T1_P1.E.2.healthy]="10"; OID_MAP[T1_P1.E.2.violated]="0"
OID_MAP[T1_P1.R.1.oid]="8.3";   OID_MAP[T1_P1.R.1.healthy]="1";  OID_MAP[T1_P1.R.1.violated]="0"
OID_MAP[T1_P1.R.2.oid]="8.4";   OID_MAP[T1_P1.R.2.healthy]="1";  OID_MAP[T1_P1.R.2.violated]="0"

# ---------------------------------------------------------------------------
# T2_P1: Priority 1 — 4 Evaluate, 4 Resolved — subtree 9
# ---------------------------------------------------------------------------
OID_MAP[T2_P1.E.1.oid]="9.1";   OID_MAP[T2_P1.E.1.healthy]="10"; OID_MAP[T2_P1.E.1.violated]="0"
OID_MAP[T2_P1.E.2.oid]="9.2";   OID_MAP[T2_P1.E.2.healthy]="10"; OID_MAP[T2_P1.E.2.violated]="0"
OID_MAP[T2_P1.E.3.oid]="9.3";   OID_MAP[T2_P1.E.3.healthy]="10"; OID_MAP[T2_P1.E.3.violated]="0"
OID_MAP[T2_P1.E.4.oid]="9.4";   OID_MAP[T2_P1.E.4.healthy]="10"; OID_MAP[T2_P1.E.4.violated]="0"
OID_MAP[T2_P1.R.1.oid]="9.5";   OID_MAP[T2_P1.R.1.healthy]="1";  OID_MAP[T2_P1.R.1.violated]="0"
OID_MAP[T2_P1.R.2.oid]="9.6";   OID_MAP[T2_P1.R.2.healthy]="1";  OID_MAP[T2_P1.R.2.violated]="0"
OID_MAP[T2_P1.R.3.oid]="9.7";   OID_MAP[T2_P1.R.3.healthy]="1";  OID_MAP[T2_P1.R.3.violated]="0"
OID_MAP[T2_P1.R.4.oid]="9.8";   OID_MAP[T2_P1.R.4.healthy]="1";  OID_MAP[T2_P1.R.4.violated]="0"

# ---------------------------------------------------------------------------
# T1_P2: Priority 2 — 2 Evaluate, 2 Resolved — subtree 10
# ---------------------------------------------------------------------------
OID_MAP[T1_P2.E.1.oid]="10.1";  OID_MAP[T1_P2.E.1.healthy]="10"; OID_MAP[T1_P2.E.1.violated]="0"
OID_MAP[T1_P2.E.2.oid]="10.2";  OID_MAP[T1_P2.E.2.healthy]="10"; OID_MAP[T1_P2.E.2.violated]="0"
OID_MAP[T1_P2.R.1.oid]="10.3";  OID_MAP[T1_P2.R.1.healthy]="1";  OID_MAP[T1_P2.R.1.violated]="0"
OID_MAP[T1_P2.R.2.oid]="10.4";  OID_MAP[T1_P2.R.2.healthy]="1";  OID_MAP[T1_P2.R.2.violated]="0"

# ---------------------------------------------------------------------------
# T2_P2: Priority 2 — 4 Evaluate, 4 Resolved — subtree 11
# ---------------------------------------------------------------------------
OID_MAP[T2_P2.E.1.oid]="11.1";  OID_MAP[T2_P2.E.1.healthy]="10"; OID_MAP[T2_P2.E.1.violated]="0"
OID_MAP[T2_P2.E.2.oid]="11.2";  OID_MAP[T2_P2.E.2.healthy]="10"; OID_MAP[T2_P2.E.2.violated]="0"
OID_MAP[T2_P2.E.3.oid]="11.3";  OID_MAP[T2_P2.E.3.healthy]="10"; OID_MAP[T2_P2.E.3.violated]="0"
OID_MAP[T2_P2.E.4.oid]="11.4";  OID_MAP[T2_P2.E.4.healthy]="10"; OID_MAP[T2_P2.E.4.violated]="0"
OID_MAP[T2_P2.R.1.oid]="11.5";  OID_MAP[T2_P2.R.1.healthy]="1";  OID_MAP[T2_P2.R.1.violated]="0"
OID_MAP[T2_P2.R.2.oid]="11.6";  OID_MAP[T2_P2.R.2.healthy]="1";  OID_MAP[T2_P2.R.2.violated]="0"
OID_MAP[T2_P2.R.3.oid]="11.7";  OID_MAP[T2_P2.R.3.healthy]="1";  OID_MAP[T2_P2.R.3.violated]="0"
OID_MAP[T2_P2.R.4.oid]="11.8";  OID_MAP[T2_P2.R.4.healthy]="1";  OID_MAP[T2_P2.R.4.violated]="0"

# ---------------------------------------------------------------------------
# Tenant metadata — used by Phase 83 interpreter for command validation
# ---------------------------------------------------------------------------
declare -A TENANT_EVAL_COUNT TENANT_RES_COUNT

TENANT_EVAL_COUNT[T1_P1]=2; TENANT_RES_COUNT[T1_P1]=2
TENANT_EVAL_COUNT[T2_P1]=4; TENANT_RES_COUNT[T2_P1]=4
TENANT_EVAL_COUNT[T1_P2]=2; TENANT_RES_COUNT[T1_P2]=2
TENANT_EVAL_COUNT[T2_P2]=4; TENANT_RES_COUNT[T2_P2]=4

VALID_TENANTS="T1_P1 T2_P1 T1_P2 T2_P2"
