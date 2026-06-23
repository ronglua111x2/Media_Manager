// Ported from Sonarr (https://github.com/Sonarr/Sonarr)
// Copyright (C) Sonarr contributors — licensed under GPL-3.0

namespace MediaManager.Sonarr.Parser;

public class InvalidDateException : Exception
{
    public InvalidDateException(string message, params object[] args)
        : base(string.Format(message, args))
    {
    }

    public InvalidDateException(string message)
        : base(message)
    {
    }
}
