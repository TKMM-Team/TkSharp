# Verification - September 12, 2026

## Native IO package

- 18 source-only regression tests, including all truncated prefixes of a synthetic file.
- 72 real fixture reads matching independently decoded snapshots, followed by native write/read round trips.
- 72 native-written files independently checked with dt's parser.
- No game files or fixture snapshots are included in the package.

These 72 fixture instances are vanilla, lower-priority, higher-priority and
expected outputs for 18 merge cases. They include repeated files, not 72
independent format samples.

## TkSharp merger

- 36 source-only graph/binary/adapter regression tests.
- 18 decoded-snapshot parity cases.
- 18 real binary merge cases using AinbFormat.
- Release builds passed with experimental AINB enabled and disabled.
- All 18 native merged graphs independently matched the Python v2 expectations
  through dt's parser, excluding regenerated GUIDs.
- TotkBits' Rust parser accepted all 72 native rewrites and 18 native merge outputs.

There are nine overlapping file/mod pairs, tested in both priority orders:
More Armor Effects + Shinobi for jump/backflip/fall, and More Armor Effects +
Fire Breath for basicattack/drawn/whistle/bow/throwweapon/throwmaterial.

Expected outcomes: 13 rebuilt merges, 3 reused inputs, and 2 whole-file whistle
fallbacks. A passing fallback test does not claim both mods survived that conflict.

## Limits

The previously reported gameplay test used Python v2 output. These native C#
results have not yet passed a new in-game test through a full TKMM profile.
Loose files, nested packs, compression, RSTB, top-of-UI priority and changed
cross-file module/blackboard relationships still need that integration pass.

TkSharp builds emit the pre-existing VYaml source-generator warning CS8785
(`InvalidOperationException: Unreachable`). It has not been suppressed here.
The standalone AinbFormat package build has no warnings.

## Published package

[AinbFormat 0.1.0-alpha.1](https://www.nuget.org/packages/AinbFormat/0.1.0-alpha.1)
was published through GitHub Actions and NuGet Trusted Publishing.
The package's source-only tests and pack step passed on Windows and Linux.

TkSharp was restored using only NuGet.org and a new package cache, without the
local feed. All 72 merger checks passed again against that public download,
including the 18 native binary cases. The temporary unpublished-package packing
guard has been removed; the experimental compile gate and opt-in remain unchanged.
Experimental TkSharp packing was also checked with a prerelease package version.
No TkSharp package was published.

The GitHub workflows emitted a non-blocking Node.js 20 deprecation warning for
checkout/setup-dotnet v4. This did not prevent tests, packing or publication.
