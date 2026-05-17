namespace SqlTestSupport
{
    public enum SqlStatementKind
    {
        Unknown = 0,
        Select,
        Insert,
        Update,
        Delete,
        Merge,
        Execute,
        Multiple
    }
}
