namespace SqliteMcp;

public readonly record struct OpenResult(ConnectionEntry Entry, bool WasReused);
