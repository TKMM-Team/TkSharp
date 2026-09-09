namespace AinbModel.Contract;

/// <summary>Implemented by external binary IO, never by TkSharp's merge logic.</summary>
public interface IAinbCodec
{
    /// <summary>
    /// Validate and read a decompressed AINB without mutating the input.
    /// Mark every unrepresented feature in UnsupportedFeatures; never silently
    /// omit it. Malformed binaries must throw InvalidDataException.
    /// Returned records must own their data after this call returns.
    /// </summary>
    AinbDocument Read(ReadOnlySpan<byte> data);

    /// <summary>
    /// Write a supported document to owned, decompressed AINB bytes. Preserve
    /// all represented fields and reject unsupported features. SARC, Zstandard,
    /// ROMFS lookup and resource-size tables are the caller's responsibility.
    /// </summary>
    byte[] Write(AinbDocument document);
}
