namespace SqlTestSupport
{
    // WhenSql に一致しない SQL を router がどう扱うかを表す。
    public enum UnmatchedSqlBehavior
    {
        Strict = 0,
        ValidateOnlyForCommands
    }
}
