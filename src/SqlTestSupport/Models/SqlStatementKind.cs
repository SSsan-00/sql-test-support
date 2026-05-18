namespace SqlTestSupport
{
    // Mock 分岐で使う先頭 SQL statement の大まかな分類。
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
