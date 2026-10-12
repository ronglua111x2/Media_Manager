// Ported from Sonarr (https://github.com/Sonarr/Sonarr)
// Copyright (C) Sonarr contributors — licensed under GPL-3.0
// Modifications: replaced NLog with no-op logger.

namespace MediaManager.Sonarr.Parser;

internal sealed class ParserLog
{
    public static readonly ParserLog Instance = new();

    public void Debug(string message, params object[] args) { }
    public void Debug(Exception exception, string message, params object[] args) { }
    public void Trace(string message, params object[] args) { }
    public void Trace(object value) { }
    public void Error(Exception exception, string message, params object[] args) { }
}
