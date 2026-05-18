namespace SqlTestSupport
{
    // Mock router に到達した DB 実行メソッドの種類。
    public enum DbCallMethod
    {
        ExecuteNonQuery = 0,
        Scalar,
        Command
    }
}
