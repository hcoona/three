# NuGet reproducibility experiment 948

This non-main branch is an isolated feasibility experiment, authorized by the
owner before a later implementation Wave. It is not an implementation proposal
and must not be merged. See `protocol.md` for scope, repetitions and effects.

`packet.zip` contains the exact provider source closure, native version facts
and simulation witness from accepted source
`499e01fb4446d4d7ec65974a2481f0340ffc4457`. The packet has no publication authority.

Run on Windows with Python 3.14:

```powershell
python experiments/nuget-repro-948/campaign.py --root "$env:TEMP/repro948-manual"
```

Choose a new empty output root. The recipe downloads verified official SDKs,
builds 12 original unsigned packages across controlled configurations, and
exports comparison records, original archives and binlogs. It never publishes.
For diagnostic inspection, `inspect-debug.ps1` decodes Portable PDB document
names and PE CodeView paths using the runtime metadata reader.

The branch-specific push workflow runs this recipe on Windows Server 2025.
Results establish the tested build mechanism only; production adapter admission
and delivery remain separate future work.
