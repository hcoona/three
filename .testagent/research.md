# Contract fix research

## Acceptance checklist

1. Enum JSON accepts only exact canonical camelCase declared names.
2. Comma combinations, numeric tokens/strings, unknown names, casing variants, and undefined
   values are rejected.
3. Source-generated serialization works with reflection disabled.
4. Secret value-writing Yarn `npmAuthToken` changes bind the manifest selector and change key to
   the manifest resource identity.
5. Non-secret, non-token, and remove-only Yarn changes remain outside that binding rule.
6. Yarn producers and configuration writers use the canonical token selector.
7. Every public contract enum member has an independently frozen literal number.
8. Public schemas, contract versions, enum values, and dependencies remain unchanged.

## Targets

- `ContractJson.cs` and `ContractEnumJsonConverter.cs`: strict enum wire conversion.
- `Contracts.cs`: Yarn secret token selector/resource binding.
- `YarnPhase13VerticalSliceService.cs`: canonical producer selector.
- `YarnrcPhysicalTargetWriter.cs`: canonical selector validation and writing.
- `ConfigurationPhase14VerticalSliceService.cs`: canonical ownership-state recognition.
- `ContractFreezeTests.cs` and `YarnPhase13VerticalSliceServiceTests.cs`: regressions and producer
  evidence.

The projects use .NET 10, Microsoft.Testing.Platform, and xUnit v3. Contract JSON reflection is
disabled in the Contracts test project.
