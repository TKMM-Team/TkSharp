using AinbModel.Contract;

namespace TkSharp.Merging.Mergers.Ainb;

public sealed record AinbBinaryResult(byte[] Bytes, AinbMergeReport Report);

/// <summary>Binary boundary only; all format IO is delegated to the supplied codec.</summary>
public sealed class AinbBinaryMerger(IAinbCodec codec)
{
    private readonly object _gate = new();

    public AinbBinaryResult Merge(ReadOnlySpan<byte> vanilla, IReadOnlyList<ArraySegment<byte>> inputs,
        IReadOnlyList<string>? sourceNames = null, bool allowFallback = true)
    {
        // TkSharp merges files concurrently; a codec instance need not be reentrant.
        lock (_gate) {
            string[] names = sourceNames?.ToArray() ?? Enumerable.Range(0, inputs.Count).Select(i => $"mod-{i}").ToArray();
            if (names.Length != inputs.Count) throw new ArgumentException("One name is required per input.", nameof(sourceNames));
            if (inputs.Count == 0) return new(vanilla.ToArray(), new() { Status = AinbMergeStatus.Unchanged });
            var documents = inputs.Select(raw => codec.Read(raw.AsSpan())).ToArray();
            foreach (var document in documents) {
                if (document.UnsupportedFeatures == AinbUnsupportedFeatures.None && document.Version == 0x407)
                    AinbGraphMerger.Validate(document);
            }
            AinbBinaryResult Fallback(string reason) {
                if (!allowFallback) throw new AinbMergeNotSupportedException(reason);
                return new(inputs[^1].ToArray(), new() {
                    Status = AinbMergeStatus.Fallback, Reason = reason, SelectedSource = names[^1]
                });
            }
            // A modded input is never substituted for the actual vanilla baseline.
            if (vanilla.IsEmpty) return Fallback("No vanilla reference; using the highest-priority complete AINB.");
            var baseline = codec.Read(vanilla);
            try {
                var result = AinbGraphMerger.Merge(baseline, documents, names);
                var semantic = AinbGraphMerger.WithoutGuids(result.Document);
                for (int i = documents.Length - 1; i >= 0; i--) {
                    if (semantic != AinbGraphMerger.WithoutGuids(documents[i])) continue;
                    result.Report.Status = AinbMergeStatus.InputReused;
                    result.Report.SelectedSource = names[i];
                    return new(inputs[i].ToArray(), result.Report);
                }
                if (semantic == AinbGraphMerger.WithoutGuids(baseline)) {
                    result.Report.Status = AinbMergeStatus.Unchanged;
                    return new(vanilla.ToArray(), result.Report);
                }
                byte[] output = codec.Write(result.Document);
                var check = codec.Read(output);
                try { AinbGraphMerger.RequireSupported(check); }
                catch (AinbMergeNotSupportedException error) {
                    throw new InvalidDataException("AINB writer introduced unsupported data.", error);
                }
                AinbGraphMerger.Validate(check);
                if (semantic != AinbGraphMerger.WithoutGuids(check))
                    throw new InvalidDataException("AINB writer changed the merged graph during round-trip.");
                return new(output, result.Report);
            }
            catch (AinbMergeNotSupportedException error) {
                return Fallback(error.Message);
            }
        }
    }
}
