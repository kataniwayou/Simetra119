---
phase: quick
plan: 045
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs
  - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
  - src/SnmpCollector/Pipeline/IOidMapService.cs
  - tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs
autonomous: true

must_haves:
  truths:
    - "TenantVectorOptionsValidator.Validate() always returns ValidateOptionsResult.Success"
    - "Validator has no IOidMapService or ILogger dependencies"
    - "DI registration still compiles and resolves correctly"
    - "All tests pass: positive tests updated, negative tests replaced with unconditional-acceptance tests"
  artifacts:
    - path: "src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs"
      provides: "No-op validator returning Success unconditionally"
    - path: "tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs"
      provides: "Tests confirming unconditional acceptance"
  key_links:
    - from: "ServiceCollectionExtensions.cs"
      to: "TenantVectorOptionsValidator"
      via: "DI singleton registration"
      pattern: "AddSingleton<TenantVectorOptionsValidator>"
---

<objective>
Remove all validation rules from TenantVectorOptionsValidator, making tenant creation unconditional. The operator is responsible for correct config; data arrives only if ip/port/metric_name matches at runtime.

Purpose: Eliminate false-positive validation failures that block tenant creation when OID map is not yet loaded or config uses forward-referenced metric names.
Output: A no-op validator that always returns Success, with updated tests and cleaned-up DI wiring.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs
@src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
@src/SnmpCollector/Pipeline/IOidMapService.cs
@tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Gut validator and clean up DI and IOidMapService doc</name>
  <files>
    src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs
    src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    src/SnmpCollector/Pipeline/IOidMapService.cs
  </files>
  <action>
    **TenantVectorOptionsValidator.cs:**
    - Remove `using System.Net;` (no longer parsing IPs)
    - Remove `using SnmpCollector.Pipeline;` (no longer using IOidMapService)
    - Remove `using Microsoft.Extensions.Logging;` (no longer logging)
    - Remove the `_oidMapService` and `_logger` fields
    - Remove the constructor entirely (parameterless class)
    - Replace Validate() body with: `return ValidateOptionsResult.Success;`
    - Update XML doc to: "No-op validator for TenantVectorOptions. Tenant creation is unconditional; the operator is responsible for correct config. Data arrives only if ip/port/metric_name matches at runtime."
    - Keep `using Microsoft.Extensions.Options;` and the namespace/class declaration

    **ServiceCollectionExtensions.cs:**
    - The DI registration at lines 299-301 stays as-is. The concrete-first pattern (`AddSingleton<TenantVectorOptionsValidator>()` then factory resolve for `IValidateOptions<TenantVectorOptions>`) still works because the validator now has a parameterless constructor that DI can activate. No changes needed here, but verify it compiles.

    **IOidMapService.cs:**
    - On line 25, remove the sentence "Used by TenantVectorOptionsValidator to verify config MetricName references." from the ContainsMetricName XML doc comment. Keep the rest of the doc comment intact. The updated summary should read:
      ```
      /// <summary>
      /// Checks whether a metric name exists as a value in the current OID map.
      /// </summary>
      ```
  </action>
  <verify>
    Run `dotnet build src/SnmpCollector/SnmpCollector.csproj` -- must compile with zero errors.
  </verify>
  <done>
    TenantVectorOptionsValidator has no dependencies and always returns Success. IOidMapService doc no longer references the validator. Project compiles cleanly.
  </done>
</task>

<task type="auto">
  <name>Task 2: Update test suite for unconditional acceptance</name>
  <files>
    tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs
  </files>
  <action>
    Rewrite TenantVectorOptionsValidatorTests to reflect the no-op validator:

    **Constructor:** Remove IOidMapService mock and ILogger mock. Instantiate validator directly: `_validator = new TenantVectorOptionsValidator();`

    **Keep these positive tests (update to remove mock setup):**
    - Validate_ValidConfig_ReturnsSuccess
    - Validate_MultipleTenants_ReturnsSuccess
    - Validate_EmptyMetricsArray_ReturnsSuccess
    - Validate_CrossTenantOverlap_ReturnsSuccess
    - Validate_EmptyTenantsList_ReturnsSuccess
    - Validate_NegativePriority_ReturnsSuccess

    **Delete ALL negative/OID-map/error-format tests:**
    - Validate_InvalidIpAddress_Fails
    - Validate_EmptyIpAddress_Fails
    - Validate_PortZero_Fails
    - Validate_PortAbove65535_Fails
    - Validate_EmptyMetricName_Fails
    - Validate_MetricNameNotInOidMap_Fails
    - Validate_DuplicateMetricWithinTenant_Fails
    - Validate_MultipleErrorsCollected_ReportsAll
    - Validate_OidMapEmpty_SkipsMetricNameCheck_ReturnsSuccess
    - Validate_OidMapEmpty_StillValidatesOtherRules
    - Validate_ErrorMessages_ContainPathContext

    **Add three new tests confirming unconditional acceptance:**
    - Validate_EmptyIp_ReturnsSuccess: Set Ip="" on a metric slot, assert Succeeded
    - Validate_InvalidPort_ReturnsSuccess: Set Port=0 on a metric slot, assert Succeeded
    - Validate_EmptyMetricName_ReturnsSuccess: Set MetricName="" on a metric slot, assert Succeeded

    Remove `using NSubstitute;` and `using SnmpCollector.Pipeline;` from usings (no longer needed). Keep `using Microsoft.Extensions.Logging;` ONLY if still needed (it is not -- remove it). Keep `using Xunit;`, `using SnmpCollector.Configuration;`, `using SnmpCollector.Configuration.Validators;`.

    Update class XML doc to: "Unit tests for TenantVectorOptionsValidator. Confirms unconditional acceptance (always returns Success)."
  </action>
  <verify>
    Run `dotnet test tests/SnmpCollector.Tests/ --filter "FullyQualifiedName~TenantVectorOptionsValidator"` -- all tests pass, zero failures.
  </verify>
  <done>
    Test suite has 9 tests (6 original positive + 3 new unconditional acceptance), all passing. No mock dependencies. No negative test cases.
  </done>
</task>

</tasks>

<verification>
1. `dotnet build src/SnmpCollector/SnmpCollector.csproj` compiles with zero errors
2. `dotnet test tests/SnmpCollector.Tests/ --filter "FullyQualifiedName~TenantVectorOptionsValidator"` -- 9 tests pass
3. `dotnet test tests/SnmpCollector.Tests/` -- full test suite passes (no regressions)
</verification>

<success_criteria>
- TenantVectorOptionsValidator.Validate() returns ValidateOptionsResult.Success unconditionally
- Validator has zero constructor dependencies (no IOidMapService, no ILogger)
- DI registration unchanged and resolves correctly
- 9 tests pass confirming unconditional acceptance behavior
- Full test suite has no regressions
</success_criteria>

<output>
After completion, create `.planning/quick/045-remove-tenant-vector-validation/045-SUMMARY.md`
</output>
