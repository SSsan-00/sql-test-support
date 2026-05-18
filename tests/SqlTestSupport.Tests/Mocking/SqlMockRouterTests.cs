using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport.Tests
{
    [TestClass]
    public sealed class SqlMockRouterTests
    {
        [TestMethod]
        public void Scalar_returns_registered_value_for_matching_select()
        {
            // SELECT 形状に一致した rule は scalar 戻り値を返す。
            var router = new SqlMockRouter();
            router
                .WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
                .ReturnsScalar("Alice");

            var value = router.Scalar<string>("""
                SELECT Name
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.AreEqual("Alice", value);
            router.VerifyAll();
        }

        [TestMethod]
        public void Scalar_returns_null_for_matching_nullable_scalar_without_configured_return()
        {
            // nullable 戻り値の scalar は戻り値指定を省略した場合 null を返す。
            var router = new SqlMockRouter();
            router.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"));

            var value = router.Scalar<int?>("""
                SELECT ParentCustomerId
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.IsNull(value);
            router.VerifyAll();
        }

        [TestMethod]
        public void Scalar_rejects_matching_non_nullable_scalar_without_configured_return()
        {
            // non-nullable 戻り値の scalar は引き続き戻り値指定を必須にする。
            var router = new SqlMockRouter();
            router.WhenSql(q => q.IsSelectFrom("dbo.Customers"));

            Assert.Throws<AssertFailedException>(() =>
                router.Scalar<int>("""
                    SELECT COUNT(1)
                    FROM dbo.Customers
                    """));
        }

        [TestMethod]
        public void ExecuteNonQuery_returns_registered_affected_rows_for_matching_update()
        {
            // UPDATE 形状に一致した rule は affected rows を返す。
            var router = new SqlMockRouter();
            router
                .WhenSql(q => q.IsUpdate("dbo.Customers") && q.WhereUses("Id"))
                .ReturnsAffectedRows(1);

            var affectedRows = router.ExecuteNonQuery("""
                UPDATE dbo.Customers
                SET Name = @Name
                WHERE Id = @Id
                """);

            Assert.AreEqual(1, affectedRows);
            router.VerifyAll();
        }

        [TestMethod]
        public void ExecuteCommand_completes_for_matching_void_command()
        {
            // 戻り値なし実行は Completes rule に一致した場合だけ成功する。
            var router = new SqlMockRouter();
            router
                .WhenSql(q => q.IsUpdate("dbo.Customers") && q.WhereUses("Id"))
                .Completes();

            router.ExecuteCommand("""
                UPDATE dbo.Customers
                SET Name = @Name
                WHERE Id = @Id
                """);

            router.VerifyAll();
        }

        [TestMethod]
        public void ExecuteCommand_rejects_rule_that_returns_affected_rows()
        {
            // void 実行には Completes rule を明示し、affected rows rule は流用しない。
            var router = new SqlMockRouter();
            router
                .WhenSql(q => q.IsUpdate("dbo.Customers"))
                .ReturnsAffectedRows(1);

            Assert.Throws<AssertFailedException>(() =>
                router.ExecuteCommand("""
                    UPDATE dbo.Customers
                    SET Name = @Name
                    WHERE Id = @Id
                    """));
        }

        [TestMethod]
        public void ExecuteCommand_default_accepts_unregistered_valid_sql()
        {
            // 既定では未登録の戻り値なし SQL を解析・履歴記録だけで通す。
            var router = new SqlMockRouter();

            router.ExecuteCommand("""
                UPDATE dbo.Customers
                SET Name = @Name
                WHERE Id = @Id
                """);

            Assert.HasCount(1, router.History);
            Assert.AreEqual(DbCallMethod.Command, router.History[0].Method);
            Assert.IsTrue(router.History[0].IsUpdate("dbo.Customers"));
        }

        [TestMethod]
        public void ExecuteCommand_strict_mode_rejects_unregistered_sql()
        {
            // strict mode を明示した場合は未登録の戻り値なし SQL も失敗する。
            var router = new SqlMockRouter(UnmatchedSqlBehavior.Strict);

            Assert.Throws<AssertFailedException>(() =>
                router.ExecuteCommand("""
                    UPDATE dbo.Customers
                    SET Name = @Name
                    WHERE Id = @Id
                    """));
        }

        [TestMethod]
        public void ExecuteCommand_validate_only_mode_accepts_unregistered_valid_sql()
        {
            // validate-only mode では未登録の戻り値なし SQL を解析・履歴記録だけで通す。
            var router = new SqlMockRouter(UnmatchedSqlBehavior.ValidateOnlyForCommands);

            router.ExecuteCommand("""
                UPDATE dbo.Customers
                SET Name = @Name
                WHERE Id = @Id
                """);

            Assert.HasCount(1, router.History);
            Assert.AreEqual(DbCallMethod.Command, router.History[0].Method);
            Assert.IsTrue(router.History[0].IsUpdate("dbo.Customers"));
        }

        [TestMethod]
        public void ExecuteCommand_validate_only_mode_still_rejects_unregistered_invalid_sql()
        {
            // validate-only mode でも SQL 構文不正は失敗する。
            var router = new SqlMockRouter(UnmatchedSqlBehavior.ValidateOnlyForCommands);

            Assert.Throws<AssertFailedException>(() => router.ExecuteCommand("SELECT FROM WHERE"));
        }

        [TestMethod]
        public void Scalar_validate_only_mode_still_rejects_unregistered_sql()
        {
            // 戻り値が必要な SQL は validate-only mode でも rule 登録必須。
            var router = new SqlMockRouter(UnmatchedSqlBehavior.ValidateOnlyForCommands);

            Assert.Throws<AssertFailedException>(() =>
                router.Scalar<int>("""
                    SELECT COUNT(1)
                    FROM dbo.Customers
                    """));
        }

        [TestMethod]
        public void Scalar_default_returns_null_for_unregistered_nullable_sql()
        {
            // 既定では未登録でも nullable scalar なら検証後に null を返す。
            var router = new SqlMockRouter();

            var value = router.Scalar<int?>("""
                SELECT ParentCustomerId
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.IsNull(value);
            Assert.HasCount(1, router.History);
            Assert.AreEqual(DbCallMethod.Scalar, router.History[0].Method);
        }

        [TestMethod]
        public void Scalar_default_returns_null_for_unregistered_reference_sql()
        {
            // reference type scalar は null 返却可能な型として扱う。
            var router = new SqlMockRouter();

            var value = router.Scalar<string?>("""
                SELECT MiddleName
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.IsNull(value);
        }

        [TestMethod]
        public void Scalar_strict_mode_rejects_unregistered_nullable_sql()
        {
            // strict mode は nullable scalar でも未登録 SQL を失敗させる。
            var router = new SqlMockRouter(UnmatchedSqlBehavior.Strict);

            Assert.Throws<AssertFailedException>(() =>
                router.Scalar<int?>("""
                    SELECT ParentCustomerId
                    FROM dbo.Customers
                    WHERE Id = @Id
                    """));
        }

        [TestMethod]
        public void Router_rejects_unregistered_non_nullable_sql()
        {
            // 既定でも non-nullable scalar は返す値を決められないため失敗する。
            var router = new SqlMockRouter();

            Assert.Throws<AssertFailedException>(() =>
                router.Scalar<int>("""
                    SELECT COUNT(1)
                    FROM dbo.Customers
                    """));
        }

        [TestMethod]
        public void Router_rejects_invalid_sql_before_matching()
        {
            // rule matching より先に SQL 構文検証を強制する。
            var router = new SqlMockRouter();
            router.WhenSql(_ => true).ReturnsScalar(1);

            Assert.Throws<AssertFailedException>(() =>
                router.Scalar<int>("SELECT FROM WHERE"));
        }

        [TestMethod]
        public void Scalar_sequence_returns_values_in_order()
        {
            // sequence は同じ rule への呼び出し順に消費される。
            var router = new SqlMockRouter();
            router
                .WhenSql(q => q.IsSelectFrom("dbo.Customers"))
                .ReturnsScalarSequence("Alice", "Bob");

            var first = router.Scalar<string>("SELECT Name FROM dbo.Customers");
            var second = router.Scalar<string>("SELECT Name FROM dbo.Customers");

            Assert.AreEqual("Alice", first);
            Assert.AreEqual("Bob", second);
        }

        [TestMethod]
        public void VerifyAll_fails_when_rule_was_not_called()
        {
            // 登録した rule が未使用なら VerifyAll で検出する。
            var router = new SqlMockRouter();
            router.WhenSql(q => q.IsSelectFrom("dbo.Customers")).ReturnsScalar("Alice");

            Assert.Throws<AssertFailedException>(() => router.VerifyAll());
        }
    }
}
