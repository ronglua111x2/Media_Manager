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
    Direct,
    Flatten,
    Title,
    Ordinal,
    None
}

public enum SpecialMappingSource
{
    Rule,
    Gemini
}
