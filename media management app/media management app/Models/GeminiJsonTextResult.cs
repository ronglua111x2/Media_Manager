namespace media_management_app.Models;

/// <param name="FinishReason">Gemini's finishReason for the answer (STOP when complete; MAX_TOKENS, SAFETY, ... when cut short).</param>
public sealed record GeminiJsonTextResult(string Model, string Json, string? FinishReason = null);
