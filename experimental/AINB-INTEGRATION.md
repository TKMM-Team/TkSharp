# Experimental AINB Merging

## Status and ownership

`AinbFormat 0.1.0-alpha.1` now supplies native C# IO and the typed model.
TkSharp contains the merge rules, diagnostics and adapter only. The temporary
`AinbModel.Contract` project has been removed. Arch's AinbLibrary is untouched.

[AinbFormat 0.1.0-alpha.1](https://www.nuget.org/packages/AinbFormat/0.1.0-alpha.1) is published on NuGet. The feature remains
compile-gated and explicitly opt-in. This is an experimental merger for a
supported subset, not general support for every AINB in the game.

The earlier in-game test used the Python v2 output. The native C# implementation
now agrees with those reference graphs in binary integration tests, but has not
itself been tested in-game through a complete TKMM profile.

## The basic rule

Use the actual vanilla file from the caller's ROMFS as the common baseline.
Process mods from lowest to highest priority. Do not compare a mod only against
the previously processed mod: a high-priority mod usually carries lots of
unchanged vanilla data, and that data must not undo another mod's edits.

For a value, record, or atomic list:

```text
choose(vanilla, accumulated, incoming):
    if incoming equals vanilla:
        return accumulated
    if incoming equals accumulated:
        return accumulated
    if accumulated equals vanilla:
        return incoming
    record a conflict
    return incoming
```

Missing entries participate in the same rule. This handles additions, removals,
edit/edit conflicts, and edit/delete conflicts. A conflict selects the incoming
higher-priority change; it does not mean the entire higher-priority file wins.

## Finding corresponding nodes

AINB indices are file-local positions. GUIDs in the supplied mods were not
reliable enough to use as cross-mod identity.

For each mod independently:

1. Match command roots by command name, including secondary roots when both exist.
   The target nodes must have the same type, name and module/non-module role.
2. Among unmatched nodes, match unique local contents without GUIDs or outgoing
   node references. Keep parameter names, types, defaults and source-output slots.
3. Match remaining nodes only when their type/name/module role is unique on both sides.
4. If a vanilla node is still unmatched but that role exists in the mod, stop
   rather than guessing between ambiguous or replaced nodes.
5. Give unmatched mod nodes their own internal keys. Identical semantic re-exports
   reuse their existing addition keys; an equal GUID alone never shares a key.

A matched node's output layout must stay unchanged. Remapping changed typed output
slots is not implemented. Root-role changes and ambiguous matches cause whole-file
fallback, not a guessed correspondence.

## What merges independently

- Nodes: by the correspondence above; added nodes from different mods stay distinct.
- Commands: by command name; each command record is atomic.
- Properties: by parameter type and name; each value/flags record is atomic.
- Inputs: by parameter type and name; default, flags and sources are one atomic record.
- Outputs: unchanged for matched nodes; included in full for new nodes.
- Query lists: atomic, not appended or unioned.
- Module declarations: by path and category; the whole declaration, including
  instance count, is atomic.
- Blackboard ID and parent ID: one atomic pair. Only empty blackboards are supported.

Duplicate keys are not guessed at. A parameter's float, bool or int type is part
of its identity; this is not an AINB blackboard merger.

Module instance counts are not recomputed from combined runtime module usage.
Cross-file compatibility of changed blackboard IDs is also not established.
Both remain explicit review/testing limits.

## Connection lists and the jump fix

Most connection lists are atomic by connection type. Selector ordering and
sequential execution are meaningful, so concatenating their branches would
invent behavior.

There is one narrower rule for a Simultaneous node's Child list. It is eligible
only if both mods keep its vanilla properties unchanged, including its execution
policies. Each list must satisfy all of these checks:

- No vanilla slot is removed; the list is at least as long as vanilla.
- Targets are unique within the list.
- A retained vanilla target stays in its original slot.
- A replaced slot keeps its other connection metadata.
- Appended targets do not reuse any vanilla child target.

Then merge replacements per vanilla slot and combine distinct appended targets.
If the result would duplicate a target, abandon this special rule and use the
ordinary atomic-list rule. A conflict in one replaced slot does not discard
independent appended branches.

```text
vanilla: [A, B]
armor:   [A, Shockwave]       # Shockwave may lead onward to B
shinobi: [A, B, ShinobiTail]

combined: [A, Shockwave, ShinobiTail]
```

This was the missing behavior behind the armor jump/backflip fix. It does not
make arbitrary conflicting control flow composable.

## Full merge flow

```text
merge(vanilla_bytes, inputs_low_to_high):
    if no inputs:
        return vanilla_bytes unchanged

    read inputs through the external codec
    read vanilla if present
    if a parse detects malformed data:
        fail with an error
    if any file uses unsupported structures:
        return the exact highest-priority input, with a fallback reason
    if vanilla is absent:
        return the exact highest-priority input, with a fallback reason

    accumulated = vanilla graph
    for each mod from low to high:
        establish vanilla-to-mod node correspondence
        assign internal keys to added nodes
        remap that mod's references onto those keys
        apply vanilla-relative changes with choose()
        apply the guarded Simultaneous-child rule where eligible

    assign contiguous final node indices
    repair command roots, queries, input sources and connection targets
    preserve reference sentinels -1 and 32767
    keep vanilla node GUIDs where possible
    give additions deterministic noncolliding GUIDs
    validate all graph references and report unreachable additions

    if a merge rule cannot establish a valid supported graph:
        return the exact highest-priority input, with a fallback reason

    if the result equals an input semantically:
        reuse that input's original bytes
    otherwise if it equals vanilla:
        reuse vanilla's original bytes
    otherwise:
        write through the external codec
        read the written file back
        validate and compare graph contents, ignoring GUIDs and type-group layout
        fail if the writer changed the graph
        return the rebuilt bytes
```

`allowFallback: false` makes unsupported merges fail explicitly instead.
A codec may stop at a recognized unsupported feature; fallback is lossless byte
selection, **not** proof that such an input is fully valid.

Reachability follows command roots, connections, queries and input dependencies.
A retained or reachable node is not proof that its gameplay behavior will execute.
Unreachable additions are reported, not silently removed.

## TkSharp integration

Build with `EnableExperimentalAinb=true`, then configure before starting a merge:

```csharp
merger.UseExperimentalAinbCodec(new AinbFormat.AinbCodec());
```

Default builds are unchanged. The experimental configuration now references a
published NuGet dependency; its temporary unpublished-package guard has been removed.
When packing an experimental TkSharp build, use a prerelease `PackageVersion`:
a stable package should not depend on an alpha package. This PR does not change
TkSharp's upstream release version.

No separate changelog builder was added. Existing raw Copy entries reach the
registered merger. ROMFS lookup, SARC collection, Zstandard compression, canonical
paths and resource-size handling remain with TkSharp. Filenames are not changed.

The core and adapter expect low-to-high inputs. TkSharp's existing last-copy-wins
path follows the same order; the adapter must not reverse it a second time.
A full TKMM UI test confirming the top-listed mod wins is still required.

## Verification

Source-only tests:

```powershell
dotnet run --project experimental/AinbMerge.Tests -p:EnableExperimentalAinb=true
```

After restoring the published package from NuGet.org, the private
fixture suite also reads `base/low/high/expected.ainb` and the independently
decoded JSON files:

```powershell
dotnet run --project experimental/AinbMerge.Tests -p:EnableExperimentalAinb=true -- --fixtures path/to/private/fixtures
```

Current results are recorded in [AINB-VERIFICATION.md](AINB-VERIFICATION.md).
No ROMFS files or private fixture data belong in this PR or either NuGet package.

## Review map

- `AinbGraphMerger.cs`: correspondence, three-way changes, branch rules and index repair.
- `AinbBinaryMerger.cs`: codec boundary, byte reuse, fallback and read-back checking.
- `AinbMergeReport.cs`: conflicts and matching/reachability diagnostics.
- `AinbMerger.cs`: ITkMerger adapter and logging.
- `TkMerger.cs`: registration and real-vanilla handling.
- `experimental/AinbMerge.Tests/`: synthetic, adapter and private-fixture tests.

Thanks to dt-12345 for the Python parser used as an oracle, the TotkBits
contributors for independent Rust parsing, and ArchLeaders for TkSharp.
The external parsers are development tools, not runtime dependencies.
