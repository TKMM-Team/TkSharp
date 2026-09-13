using AinbFormat;

namespace TkSharp.Merging.Mergers.Ainb;

public sealed record AinbBinaryResult(byte[] Bytes, AinbMergeReport Report);

/// <summary>Delegates format IO and verifies rebuilt output before returning it.</summary>
public sealed class AinbBinaryMerger(IAinbCodec codec)
{
    private readonly object _gate = new();

    /// <summary>Inputs must be ordered from lowest to highest priority.</summary>
    public AinbBinaryResult Merge(ReadOnlySpan<byte> vanilla, IReadOnlyList<ArraySegment<byte>> inputs,
        IReadOnlyList<string>? sourceNames = null, bool allowFallback = true)
    {
        // TkSharp merges files concurrently; an injected codec need not be reentrant.
        lock (_gate)
        {
            string[] names = sourceNames?.ToArray() ??
                Enumerable.Range(0, inputs.Count).Select(i => $"mod-{i}").ToArray();
            if (names.Length != inputs.Count)
                throw new ArgumentException("One name is required per input.", nameof(sourceNames));
            if (inputs.Count == 0)
                return new(vanilla.ToArray(), new() { Status = AinbMergeStatus.Unchanged });

            AinbBinaryResult Fallback(string reason)
            {
                if (!allowFallback)
                    throw new AinbMergeNotSupportedException(reason);
                return new(inputs[^1].ToArray(), new()
                {
                    Status = AinbMergeStatus.Fallback,
                    Reason = reason,
                    SelectedSource = names[^1]
                });
            }

            string? unsupported = null;
            AinbDocument? Read(ReadOnlySpan<byte> bytes)
            {
                try
                {
                    var document = codec.Read(bytes);
                    AinbGraphMerger.RequireSupported(document);
                    AinbGraphMerger.Validate(document);
                    return document;
                }
                catch (AinbUnsupportedException error)
                {
                    unsupported ??= error.Message;
                    return null;
                }
                catch (AinbMergeNotSupportedException error)
                {
                    unsupported ??= error.Message;
                    return null;
                }
            }

            // Inspect every input even when an earlier one is unsupported. A detected
            // malformed binary remains an error, not a reason to copy arbitrary bytes.
            var documents = inputs.Select(raw => Read(raw.AsSpan())).ToArray();
            var baseline = vanilla.IsEmpty ? null : Read(vanilla);
            if (unsupported is not null)
                return Fallback(unsupported);
            if (baseline is null)
                return Fallback("No vanilla reference; using the highest-priority complete AINB.");

            AinbGraphResult result;
            try
            {
                result = AinbGraphMerger.Merge(baseline, documents.Select(d => d!).ToArray(), names);
            }
            catch (AinbMergeNotSupportedException error)
            {
                return Fallback(error.Message);
            }

            var semantic = AinbGraphMerger.WithoutGuids(result.Document);
            for (int i = documents.Length - 1; i >= 0; i--)
            {
                if (semantic != AinbGraphMerger.WithoutGuids(documents[i]!))
                    continue;
                result.Report.Status = AinbMergeStatus.InputReused;
                result.Report.SelectedSource = names[i];
                return new(inputs[i].ToArray(), result.Report);
            }
            if (semantic == AinbGraphMerger.WithoutGuids(baseline))
            {
                result.Report.Status = AinbMergeStatus.Unchanged;
                return new(vanilla.ToArray(), result.Report);
            }

            byte[] output;
            try
            {
                output = codec.Write(result.Document);
            }
            catch (AinbUnsupportedException error)
            {
                return Fallback(error.Message);
            }

            // Keep write/read-back failures outside the fallback path. A codec bug must
            // be visible, rather than being reported as an ordinary merge conflict.
            AinbDocument check;
            try
            {
                check = codec.Read(output);
                AinbGraphMerger.RequireSupported(check);
            }
            catch (Exception error) when (error is AinbUnsupportedException or AinbMergeNotSupportedException)
            {
                throw new InvalidDataException("AINB writer introduced unsupported data.", error);
            }
            AinbGraphMerger.Validate(check);
            if (semantic != AinbGraphMerger.WithoutGuids(check))
                throw new InvalidDataException("AINB writer changed the merged graph during round-trip.");
            return new(output, result.Report);
        }
    }
}
