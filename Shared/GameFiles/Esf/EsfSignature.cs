namespace Shared.GameFormats.Esf;

/// <summary>
/// Identifies which variant of the ESF binary envelope a file uses. Total War's .csc
/// (Composite Scene) files are just ESF data wearing a different extension - RPFM's ESF
/// editor opens them directly. See EsfReader.cs for the decode logic.
/// </summary>
public enum EsfSignature
{
    /// <summary>Older format: string tables use u16 length prefixes. Magic: CA AB 00 00.</summary>
    Caab,

    /// <summary>Newer format: string tables use u32 length prefixes. Magic: CB AB 00 00.</summary>
    Cbab,

    /// <summary>Rare, unsupported. Magic: CE AB 00 00.</summary>
    Ceab,

    /// <summary>Rare, unsupported. Magic: CF AB 00 00.</summary>
    Cfab,
}
