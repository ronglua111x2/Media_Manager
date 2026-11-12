namespace media_management_app.Models;

public enum SpecialMappingNamingPattern
{
    SnSnn,
    OvaDash,
    StandardS00E,
    Opaque
}

public enum SpecialMappingProposalReason
{
    /// <summary><c>S00Exx</c> in the file name.</summary>
    Direct,

    /// <summary>Old global air-date flatten. No longer produced (PARSE-09); kept for cached results.</summary>
    Flatten,

    /// <summary>The file's title matches the TMDB title.</summary>
    Title,

    /// <summary>Old whole-pack ordinal. No longer produced (PARSE-09); kept for cached results.</summary>
    Ordinal,

    None,

    /// <summary>k-th special of season n → k-th TMDB entry of season n's air-date window (exact count only).</summary>
    Window,

    /// <summary>The one unnumbered file of an exact season window took the one remaining slot.</summary>
    Inferred,

    /// <summary><c>OVA - N</c> whose TMDB S00EN title also names OVA N.</summary>
    OvaNumber
}

public enum SpecialMappingSource
{
    Rule,
    Gemini
}
