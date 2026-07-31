namespace Shared.GameFormats.Esf;

/// <summary>A fully decoded .csc (or other ESF-family) file.</summary>
public sealed class EsfDocument
{
    public required EsfSignature Signature { get; init; }

    /// <summary>Header field of unknown purpose; observed as 0 in every sample so far.</summary>
    public uint Unknown1 { get; init; }

    /// <summary>Unix timestamp of file creation.</summary>
    public uint CreationDate { get; init; }

    public required EsfNode Root { get; init; }
}
