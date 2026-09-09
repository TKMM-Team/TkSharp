# AINB integration handoff

## Status

The native C# merge logic and opt-in TkSharp adapter are ready for review.
They are NOT ready to enable in a normal TKMM release: a real C# binary codec is
still required. At the user's request, completion of that IO is left to Arch.
Arch's `AinbLibrary` checkout has not been edited.

The Python v2 experiment produced the test patch that the tester reports working.
This C# port is checked against its decoded results, not substituted into that
game-tested ZIP. No native binary reader/writer is provided or implied here.
Publishing and completion of the native binary codec remain separate release steps.

## Files to review

| File | Responsibility |
| --- | --- |
| `TkSharp.Merging/Mergers/Ainb/AinbGraphMerger.cs` | Vanilla-relative changes, correspondence, priority, branch composition, index repair, validation |
| `TkSharp.Merging/Mergers/Ainb/AinbBinaryMerger.cs` | External codec boundary, original-byte reuse, reported priority fallback, read-back verification |
| `TkSharp.Merging/Mergers/Ainb/AinbMergeReport.cs` | Conflicts, matching evidence, retained/reachable addition counts |
| `TkSharp.Merging/Mergers/AinbMerger.cs` | The three `ITkMerger` entry points and TkSharp logging |
| `TkSharp.Merging/TkMerger.cs` | Explicit opt-in registration and no-vanilla handling |
| `TkSharp.Merging/TkSharp.Merging.csproj` | Compile gate and temporary contract project reference |
| `experimental/AinbModel.Contract/` | Proposed immutable typed model and `IAinbCodec`; deliberately not packable |
| `experimental/AinbMerge.Tests/` | Synthetic regression tests, adapter tests, and private-fixture parity tests |

The model project is a proposal for discussion, not a replacement for Arch's API.
Its names and ownership can change. An adapter can translate from his own types,
or these fields can be adopted into AinbLibrary once agreed. Do not publish this
temporary contract as a second competing AINB IO package.

## What the merger does

Every mod is compared with the real vanilla file. Input order is lowest priority
first, so the last changed value wins an actual conflict. An unchanged value in
a higher-priority mod does not undo a lower-priority edit.

Command names anchor their entry nodes. Remaining nodes match only by unique
reference-free contents, then unique type/name/module role. GUIDs and source
array positions are not node identities: the fixtures regenerate GUIDs and reuse
the same GUID sequence for unrelated additions. Ambiguous matches reject the
composition instead of guessing. Unique structural matching is still a heuristic.

The core assigns temporary identities, merges by those identities, then assigns
output indices and remaps all represented references. Additions from distinct
source documents stay separate. Identical parsed re-exports are idempotent.
Vanilla GUIDs are retained where possible; additions get deterministic UUIDv8 IDs.
Those IDs differ from Python's UUIDv5 IDs and are not used as matching evidence.

Properties and input records merge by type and name. Each record, including input
wiring, is atomic. Matched output-port layouts must remain unchanged. Commands
merge atomically by name; module records, including instance counts, by path and
category. Queries and ordinary plug lists use atomic priority handling.

For simultaneous nodes with unchanged properties, preserve in-place connection
replacements alongside independent appended branches. Competing replacements of
the same connection use priority. Decline that finer merge when a child list
shrinks, a retained vanilla target moves, connection metadata changes, or a target
repeats. Selectors and sequences remain atomic. This fixes the jump/backflip case:

```text
vanilla:  common + normal jump
armor:    common + armor-dependent jump
Shinobi:  common + normal jump + Shinobi branch
result:   common + armor-dependent jump + Shinobi branch
```

Disconnected additions remain in the file and are reported; keeping a node is
not proof that its behavior survives. The whistle conflict intentionally falls
back to the highest-priority entire input when reference repair fails.

## IO contract for Arch

The two operations in `IAinbCodec` are:

```csharp
AinbDocument Read(ReadOnlySpan<byte> data);
byte[] Write(AinbDocument document);
```

These are decompressed, standalone AINB bytes, not SARC or Zstandard containers.
`Read` must validate the binary, not mutate it, and return owned data. Malformed
input must throw `InvalidDataException`. Unrepresented but valid content must be
marked in `UnsupportedFeatures`, never silently discarded. The merger then keeps
the winning original binary without serializing the incomplete representation.

The proposed model covers:
- File version, name/category, both blackboard IDs, and presence of section 0x6C.
- Commands with primary/optional secondary entry indices and GUIDs.
- Nodes with kind, name, index, GUID, flags, query references, properties, and IO.
- Typed Int/Bool/Float/String/Vector3F/null-pointer values; pointer class names.
- Direct/multiple input sources, output indices, optional set-blackboard marker.
- Child/Generic/Int/String plugs, with optional generic unknown fields preserved.
- Module path/category/instance count.

Parameter flag enum values in this contract are semantic flags, NOT the binary
bit masks. The codec must map them, not write their integer value verbatim.
Node kind/flag values match the inspected format subset. Unsupported types,
flags, non-null pointer payloads, or extra plug fields must not be coerced into
the supported subset. The writer is responsible for rebuilding offsets, string
pools, section counts, alignment, and any derived on-disk tables.

The initial supported scope is intentionally narrower than the whole format:
TOTK 0x407, empty blackboards/EXB/replacement/section-0x58 data, no attachments or
XLink actions, and the represented parameter and plug types. This covers all
18 overlapping fixture cases. Python could carry some unchanged extra sections;
the draft C# contract rejects those until they have proper typed coverage.

Publishing a package is not the only remaining step. The inspected AinbLibrary
checkout still has `Ainb.FromBinary<T>` returning an unfilled model, an empty
`IAinbCommand`, no writer, and an `AinbReader._buffer` that is never assigned.
These observations are about the local checkout, not a claim about any newer work
Arch may have elsewhere. No changes to those files were made in this task.

## TkSharp hooks

Build with `EnableExperimentalAinb=true`, then explicitly configure a codec before
starting a merge:

```csharp
// codec is an actual AinbLibrary-backed IAinbCodec implementation, not bundled here.
merger.UseExperimentalAinbCodec(codec, report => { /* optional diagnostics */ });
```

Both the compile flag and codec registration are required. Default builds retain
the existing behavior. This is a review gate, not a feature toggle to expose to
end users before a codec is ready. Do not configure the codec during a running
merge. The adapter serializes calls to its codec instance; the graph core itself
has no shared state.
Packing with the experimental flag is explicitly blocked while the contract is
temporary. Ordinary builds/packages keep the experiment disabled.

No AINB-specific changelog builder was added: current TkSharp already records raw
Copy entries when no builder exists. `GetInputs` sends those entries to a registered
merger. That avoids parsing a file merely to write the original bytes as a fake
delta. A compact changelog format can be added separately if wanted.

The adapter leaves ROMFS lookup, pack collection, compression, original canonical
paths, and RSTB handling with TkSharp. AINB's existing resource-size calculator is
unchanged. The no-vanilla hook avoids `MergeCustomTarget`'s synthetic baseline:
without real vanilla, use the exact highest-priority input and log the reason.

The core and adapter consume low-to-high order. `GetInputs` preserves its incoming
changelog sequence and the existing copy path uses the last entry. The TKMM caller
must continue supplying that order; the adapter does NOT reverse it a second time.
Before release, run a full UI-level test showing the top-listed mod wins.

## Verification

From the TkSharp worktree:

```powershell
dotnet build TkSharp.Merging/TkSharp.Merging.csproj -p:EnableExperimentalAinb=false
dotnet build TkSharp.Merging/TkSharp.Merging.csproj -p:EnableExperimentalAinb=true
dotnet run --project experimental/AinbMerge.Tests -p:EnableExperimentalAinb=true
dotnet run --project experimental/AinbMerge.Tests -p:EnableExperimentalAinb=true -- --fixtures experimental/AinbMerge.Tests/fixtures
```

Private fixture snapshots are generated by the workspace's
`ainb-work/export_csharp_fixtures.py`. They are ignored and must not be committed
or included in a source handoff. Without `--fixtures`, only synthetic tests run.
The JSON translator is test-only and fails on unrepresented fields; runtime
models and merge logic do not use JSON objects or serialization for comparison.

Local verification on 2026-09-08: 49 checks passed (31 synthetic/boundary checks
and 18 private fixture cases), with no failures. Both compile configurations
build successfully. This does not certify full TKMM or native IO behavior.

The binary-boundary tests use a deliberately fake snapshot codec. They test input
ordering, fallback bytes, output checks, and both buffer overloads, NOT native
AINB parsing/writing. Fixture parity compares C# results with decoded Python-v2
binary output, including conflict and reachability counts, ignoring only GUIDs.
That is semantic-port coverage, not native binary round-trip coverage.

Both enabled and disabled builds currently emit a VYaml generator warning
(`CS8785`, `InvalidOperationException: Unreachable`) while succeeding. It is not
specific to the enabled AINB path; it has not been suppressed or fixed here.

## Before enabling it

1. Agree the model/adapter API with Arch; replace the temporary contract reference
   with the agreed AinbLibrary package or mapping layer.
2. Implement and independently test real read/write IO, including unchanged-file
   round-trips and bounds validation. Unsupported files must remain lossless.
3. Rerun the fixture suite through that real codec and compare against dt/TotkBits.
4. Test complete TKMM profiles: top-mod priority, no vanilla, pack entries, loose
   AINBs, compression, RSTB, and the currently accepted whistle fallback.
5. Retest the resulting native output in-game, including armor shockwaves,
   Shinobi jump/backflip abilities, and ordinary movement/equipment changes.

Remaining semantic risks include inferred node identity, combined branch side
effects, external module dependencies/instance counts, and blackboard-ID consumers.
Module instance records currently use priority; recomputing combined module usage
is not implemented. Non-finite parameter values are rejected. The whole-file
fallback is deliberate; it does not mean both mods' behavior was preserved.

Credits: dt-12345 for the Python AINB IO used as the reference and fixture oracle;
TotkBits authors Banan039/SolidLink95 for independent binary checks of the Python
outputs; Arch for AinbLibrary/TkSharp. No GPL parser implementation is copied into
the native merge code or proposed contract. The Python/Rust tools remain local
test dependencies, not TkSharp runtime dependencies.

## Short message for Arch

The AINB merge experiment seems to be working in testing, including the jump fix
that keeps the armor shockwave branch alongside Shinobi. I've prepared a C# port
of the merge logic and an opt-in TkSharp adapter. I haven't changed your AinbLibrary
repo. There's a small proposed typed-model/IO contract so you can see exactly what
the merger needs; it can be adapted to your API. The remaining dependency is a
working native reader/writer, then a NuGet package and full integration testing.
