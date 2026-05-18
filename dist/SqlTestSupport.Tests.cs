using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport.Tests
{
    [TestClass]
    public sealed class AssertFacadeTests
    {
        [TestMethod]
        public void IsValidSql_does_not_throw_for_valid_sql()
        {
            // Assert facade は valid SQL を失敗扱いにしない。
            SqlAssertFacade.IsValidSql("""
                SELECT Id
                FROM dbo.Customers
                """);
        }

        [TestMethod]
        public void IsValidSql_throws_assert_failed_for_invalid_sql()
        {
            // test-facing API は低レベル例外を AssertFailedException に変換する。
            var exception = Assert.Throws<AssertFailedException>(() =>
                SqlAssertFacade.IsValidSql("SELECT FROM WHERE", "Custom SQL failed."));

            Assert.IsTrue(exception.Message.Contains("Custom SQL failed.", StringComparison.Ordinal));
            Assert.IsTrue(exception.Message.Contains("Parse errors:", StringComparison.Ordinal));
        }

        [TestMethod]
        public void NormalizeSql_returns_normalized_sql()
        {
            // 呼び出し側は正規化済み SQL を string として受け取れる。
            var normalized = SqlAssertFacade.NormalizeSql("""
                select Id
                from dbo.Customers
                """);

            Assert.IsTrue(normalized.Contains("SELECT", StringComparison.Ordinal));
        }
    }

    [TestClass]
    public sealed class MockDbIntegrationTests
    {
        [TestMethod]
        public void Mock_db_can_override_first_sql_argument_methods()
        {
            // 本番DBクラスの第一引数 SQL 実行メソッドだけを override すれば Mock 化できる。
            var db = new MockProductionDb();
            db.WhenSql(q => q.IsSelectFrom("dbo.Customers")).ReturnsScalar("Alice");
            db.WhenSql(q => q.IsUpdate("dbo.Customers")).ReturnsAffectedRows(1);

            var name = db.Scalar<string>("SELECT Name FROM dbo.Customers");
            var affectedRows = db.Execute("""
                UPDATE dbo.Customers
                SET Name = @Name
                WHERE Id = @Id
                """);

            Assert.AreEqual("Alice", name);
            Assert.AreEqual(1, affectedRows);
            db.VerifyAllSqlExpectations();
        }

        [TestMethod]
        public void Mock_db_can_override_void_sql_argument_method()
        {
            // 戻り値なし本番メソッドも第一引数 SQL を router に渡して検証できる。
            var db = new MockVoidProductionDb();
            db.WhenSql(q => q.IsUpdate("dbo.Customers") && q.WhereUses("Id")).Completes();

            db.Execute("""
                UPDATE dbo.Customers
                SET Name = @Name
                WHERE Id = @Id
                """);

            db.VerifyAllSqlExpectations();
        }

        [TestMethod]
        public void Mock_db_can_validate_unregistered_void_sql_without_mock_behavior()
        {
            // validate-only mode の Mock DB は未登録 void SQL を構文解析だけで通せる。
            var db = new MockVoidProductionDb(UnmatchedSqlBehavior.ValidateOnlyForCommands);

            db.Execute("""
                UPDATE dbo.Customers
                SET Name = @Name
                WHERE Id = @Id
                """);

            db.VerifyAllSqlExpectations();
        }

        private class ProductionDb
        {
            // 導入先の本番DBクラスを最小化した形。
            public virtual int Execute(string sql, object? parameters = null)
                => throw new NotSupportedException(sql);

            public virtual T Scalar<T>(string sql, object? parameters = null)
                => throw new NotSupportedException(sql);
        }

        private sealed class MockProductionDb : ProductionDb
        {
            private readonly SqlMockRouter _router = new();

            // Mock DB は router の薄い facade に留める。
            public SqlMockSetup WhenSql(Func<SqlInvocation, bool> predicate)
                => _router.WhenSql(predicate);

            public void VerifyAllSqlExpectations()
                => _router.VerifyAll();

            public override int Execute(string sql, object? parameters = null)
                => _router.ExecuteNonQuery(sql);

            public override T Scalar<T>(string sql, object? parameters = null)
                => _router.Scalar<T>(sql);
        }

        private class VoidProductionDb
        {
            public virtual void Execute(string sql, object? parameters = null)
                => throw new NotSupportedException(sql);
        }

        private sealed class MockVoidProductionDb : VoidProductionDb
        {
            private readonly SqlMockRouter _router;

            public MockVoidProductionDb(
                UnmatchedSqlBehavior unmatchedSqlBehavior = UnmatchedSqlBehavior.Strict)
            {
                _router = new SqlMockRouter(unmatchedSqlBehavior);
            }

            public SqlMockSetup WhenSql(Func<SqlInvocation, bool> predicate)
                => _router.WhenSql(predicate);

            public void VerifyAllSqlExpectations()
                => _router.VerifyAll();

            public override void Execute(string sql, object? parameters = null)
                => _router.ExecuteCommand(sql);
        }
    }

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
        public void ExecuteCommand_rejects_unregistered_sql_by_default()
        {
            // 既定は strict。戻り値なしでも未登録 SQL は失敗する。
            var router = new SqlMockRouter();

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
        public void Router_rejects_unregistered_sql()
        {
            // strict mode。valid でも未登録 SQL は失敗する。
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

    [TestClass]
    public sealed class SqlValidationServiceTests
    {
        private readonly SqlValidationService _service = new();

        [TestMethod]
        public void Analyze_accepts_valid_sql_server_2022_tsql()
        {
            // SQL Server 2022 として valid な SQL は fingerprint 付きで解析できる。
            var result = _service.Analyze("""
                SELECT Id, Name
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Fingerprint));
        }

        [TestMethod]
        public void Analyze_rejects_invalid_tsql()
        {
            // parse error は構造化された diagnostic を持つ例外になる。
            var exception = Assert.Throws<SqlSyntaxValidationException>(() =>
                _service.Analyze("SELECT FROM WHERE"));

            Assert.IsGreaterThan(0, exception.Diagnostics.Count);
        }

        [TestMethod]
        public void Normalize_returns_sql_only_when_round_trip_fingerprint_matches()
        {
            // 正規化後 SQL は再 parse され、元 SQL と同じ fingerprint の場合だけ返る。
            var result = _service.Normalize("""
                select Id, Name
                from dbo.Customers
                where Id = @Id
                """);

            Assert.AreEqual(result.OriginalFingerprint, result.NormalizedFingerprint);
            Assert.IsTrue(result.NormalizedSql.Contains("SELECT", StringComparison.Ordinal));
            Assert.IsTrue(result.NormalizedSql.Contains("FROM", StringComparison.Ordinal));
        }

        [TestMethod]
        public void Inspect_extracts_select_shape()
        {
            // SELECT の参照 table、選択列、WHERE 列、parameter を抽出する。
            var result = _service.Inspect("""
                SELECT Id, Name
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.AreEqual(SqlStatementKind.Select, result.StatementKind);
            CollectionAssert.Contains(result.ReferencedTables.ToList(), "dbo.Customers");
            CollectionAssert.Contains(result.SelectedColumns.ToList(), "Id");
            CollectionAssert.Contains(result.SelectedColumns.ToList(), "Name");
            CollectionAssert.Contains(result.WhereColumns.ToList(), "Id");
            CollectionAssert.Contains(result.ParameterNames.ToList(), "@Id");
        }

        [TestMethod]
        public void Inspect_extracts_update_target_and_where_columns()
        {
            // UPDATE は更新対象 table と WHERE 列を Mock 分岐に使える。
            var result = _service.Inspect("""
                UPDATE dbo.Customers
                SET Name = @Name
                WHERE Id = @Id
                """);

            Assert.AreEqual(SqlStatementKind.Update, result.StatementKind);
            CollectionAssert.Contains(result.TargetTables.ToList(), "dbo.Customers");
            CollectionAssert.Contains(result.WhereColumns.ToList(), "Id");
        }
    }
}
