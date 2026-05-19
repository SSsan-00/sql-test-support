using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections;

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
            // test-facing API は低レベル例外を日本語の AssertFailedException に変換する。
            var exception = Assert.Throws<AssertFailedException>(() =>
                SqlAssertFacade.IsValidSql("SELECT FROM WHERE", "SQL の検証に失敗しました。"));

            Assert.IsTrue(exception.Message.Contains("SQL の検証に失敗しました。", StringComparison.Ordinal));
            Assert.IsTrue(exception.Message.Contains("構文解析エラー:", StringComparison.Ordinal));
            Assert.IsTrue(exception.Message.Contains("対象 SQL:", StringComparison.Ordinal));
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
            db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.SelectsColumn("Name"))
                .ReturnsResult("Alice");

            var name = db.Execute("SELECT Name FROM dbo.Customers");

            Assert.AreEqual("Alice", name);
            db.VerifyAllSqlExpectations();
        }

        [TestMethod]
        public void Mock_db_can_validate_unregistered_object_result_sql_without_mock_behavior()
        {
            // object? 戻り値の未登録 SQL は構文解析と履歴記録だけ行い null を返す。
            var db = new MockProductionDb();

            var value = db.Execute("""
                SELECT MiddleName
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.IsNull(value);
            Assert.HasCount(1, db.History);
            Assert.IsTrue(db.History[0].WhereUses("Id"));
        }

        [TestMethod]
        public void Mock_db_returns_empty_collection_for_unregistered_dictionary_result()
        {
            // 独自コレクション戻り値の未登録 SQL は空の new() を返す。
            var db = new MockProductionDb();

            var rows = db.QueryRows("""
                SELECT Id, Name
                FROM dbo.Customers
                """);

            Assert.IsEmpty(rows);
            Assert.HasCount(1, db.History);
        }

        [TestMethod]
        public void Mock_db_can_branch_get_value_by_constructed_sql_metadata()
        {
            // get_value のように内部で SQL を組み立てるメソッドも、組み立て後 SQL を解析して分岐できる。
            var db = new MockProductionDb();
            db.WhenSql(q =>
                    q.IsSelectFrom("dbo.Customers") &&
                    q.SelectsColumn("Name") &&
                    q.WhereUses("Id"))
                .ReturnsResult("Alice");

            var name = db.get_value("Name", "dbo.Customers", "Id = @Id");

            Assert.AreEqual("Alice", name);
            db.VerifyAllSqlExpectations();
        }

        private class ProductionDb
        {
            // 導入先の本番DBクラスを最小化した形。
            public virtual object? Execute(string sql, object? parameters = null)
                => throw new NotSupportedException(sql);

            public virtual CustomerRows QueryRows(string sql, object? parameters = null)
                => throw new NotSupportedException(sql);

            public virtual object? get_value(string columns, string table, string where)
            {
                var sql = $"SELECT {columns} FROM {table} WHERE {where}";
                return Execute(sql);
            }
        }

        private sealed class MockProductionDb : ProductionDb
        {
            private readonly SqlMockRouter _router = new();

            // Mock DB は router の薄い facade に留める。
            public IReadOnlyList<SqlInvocation> History => _router.History;

            public SqlMockSetup WhenSql(Func<SqlInvocation, bool> predicate)
                => _router.WhenSql(predicate);

            public void VerifyAllSqlExpectations()
                => _router.VerifyAll();

            public override object? Execute(string sql, object? parameters = null)
                => _router.ExecuteResult<object?>(sql);

            public override CustomerRows QueryRows(string sql, object? parameters = null)
                => _router.ExecuteResult<CustomerRows>(sql);
        }

        private sealed class CustomerRows : Dictionary<string, object?>
        {
        }
    }

    [TestClass]
    public sealed class SqlMockRouterTests
    {
        [TestMethod]
        public void ExecuteResult_returns_registered_value_for_matching_select()
        {
            // SELECT 形状に一致した rule は登録済みの戻り値を返す。
            var router = new SqlMockRouter();
            router
                .WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
                .ReturnsResult("Alice");

            var value = router.ExecuteResult<string>("""
                SELECT Name
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.AreEqual("Alice", value);
            router.VerifyAll();
        }

        [TestMethod]
        public void ExecuteResult_returns_null_for_matching_nullable_result_without_configured_return()
        {
            // nullable 戻り値は rule に一致しても戻り値指定を省略できる。
            var router = new SqlMockRouter();
            router.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"));

            var value = router.ExecuteResult<int?>("""
                SELECT ParentCustomerId
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.IsNull(value);
            router.VerifyAll();
        }

        [TestMethod]
        public void ExecuteResult_returns_null_for_unregistered_object_result()
        {
            // 未登録 SQL でも object? 戻り値なら構文解析後に null を返す。
            var router = new SqlMockRouter();

            var value = router.ExecuteResult<object?>("""
                SELECT MiddleName
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.IsNull(value);
            Assert.HasCount(1, router.History);
            Assert.IsTrue(router.History[0].IsSelectFrom("dbo.Customers"));
        }

        [TestMethod]
        public void ExecuteResult_returns_empty_dictionary_subclass_for_unregistered_sql()
        {
            // Dictionary 継承の独自コレクションは未登録でも空インスタンスを返す。
            var router = new SqlMockRouter();

            var rows = router.ExecuteResult<CustomerRows>("""
                SELECT Id, Name
                FROM dbo.Customers
                WHERE IsActive = @IsActive
                """);

            Assert.IsEmpty(rows);
            Assert.HasCount(1, router.History);
        }

        [TestMethod]
        public void ExecuteResult_returns_empty_dictionary_implementation_for_unregistered_sql()
        {
            // IDictionary 実装の独自コレクションも空インスタンスを返す。
            var router = new SqlMockRouter();

            var rows = router.ExecuteResult<DictionaryBackedRows>("""
                SELECT Id, Name
                FROM dbo.Customers
                """);

            Assert.AreEqual(0, rows.Count);
        }

        [TestMethod]
        public void ExecuteResult_rejects_unregistered_non_defaultable_result()
        {
            // int のように既定戻り値を安全に決められない型は登録必須にする。
            var router = new SqlMockRouter();

            var exception = Assert.Throws<AssertFailedException>(() =>
                router.ExecuteResult<int>("""
                    SELECT COUNT(1)
                    FROM dbo.Customers
                    """));

            Assert.IsTrue(exception.Message.Contains("未登録 SQL", StringComparison.Ordinal));
        }

        [TestMethod]
        public void ExecuteResult_rejects_invalid_sql_before_matching()
        {
            // rule matching より先に SQL 構文検証を強制する。
            var router = new SqlMockRouter();
            router.WhenSql(_ => true).ReturnsResult(1);

            var exception = Assert.Throws<AssertFailedException>(() =>
                router.ExecuteResult<int>("SELECT FROM WHERE"));

            Assert.IsTrue(exception.Message.Contains("SQL mock が不正な SQL を受け取りました。", StringComparison.Ordinal));
        }

        [TestMethod]
        public void ExecuteResult_sequence_returns_values_in_order()
        {
            // sequence は同じ rule への呼び出し順に消費される。
            var router = new SqlMockRouter();
            router
                .WhenSql(q => q.IsSelectFrom("dbo.Customers"))
                .ReturnsResultSequence("Alice", "Bob");

            var first = router.ExecuteResult<string>("SELECT Name FROM dbo.Customers");
            var second = router.ExecuteResult<string>("SELECT Name FROM dbo.Customers");

            Assert.AreEqual("Alice", first);
            Assert.AreEqual("Bob", second);
        }

        [TestMethod]
        public void ExecuteResult_can_match_join_order_group_and_having_metadata()
        {
            // JOIN / GROUP BY / HAVING / ORDER BY の metadata で Mock 分岐できる。
            var router = new SqlMockRouter();
            router
                .WhenSql(q =>
                    q.IsSelectFrom("dbo.Customers") &&
                    q.JoinsTable("dbo.Orders") &&
                    q.GroupsBy("Id") &&
                    q.HavingCalls("COUNT") &&
                    q.OrdersBy("Id"))
                .ReturnsResult("matched");

            var value = router.ExecuteResult<string>("""
                SELECT c.Id, COUNT(o.Id) AS OrderCount
                FROM dbo.Customers AS c
                INNER JOIN dbo.Orders AS o ON o.CustomerId = c.Id
                GROUP BY c.Id
                HAVING COUNT(o.Id) > 0
                ORDER BY c.Id
                """);

            Assert.AreEqual("matched", value);
        }

        [TestMethod]
        public void ExecuteResult_sequence_fails_when_exhausted()
        {
            // sequence は期待回数を表すため、余分な呼び出しは失敗する。
            var router = new SqlMockRouter();
            router
                .WhenSql(q => q.IsSelectFrom("dbo.Customers"))
                .ReturnsResultSequence("Alice");

            _ = router.ExecuteResult<string>("SELECT Name FROM dbo.Customers");

            var exception = Assert.Throws<AssertFailedException>(() =>
                router.ExecuteResult<string>("SELECT Name FROM dbo.Customers"));

            Assert.IsTrue(exception.Message.Contains("戻り値シーケンスを使い切りました", StringComparison.Ordinal));
        }

        [TestMethod]
        public void VerifyAll_fails_when_rule_was_not_called()
        {
            // 登録した rule が未使用なら VerifyAll で検出する。
            var router = new SqlMockRouter();
            router.WhenSql(q => q.IsSelectFrom("dbo.Customers")).ReturnsResult("Alice");

            var exception = Assert.Throws<AssertFailedException>(() => router.VerifyAll());

            Assert.IsTrue(exception.Message.Contains("呼び出されていません", StringComparison.Ordinal));
        }

        private sealed class CustomerRows : Dictionary<string, object?>
        {
        }

        private sealed class DictionaryBackedRows : IDictionary<string, object?>
        {
            private readonly Dictionary<string, object?> _inner = new();

            public object? this[string key]
            {
                get => _inner[key];
                set => _inner[key] = value;
            }

            public ICollection<string> Keys => _inner.Keys;

            public ICollection<object?> Values => _inner.Values;

            public int Count => _inner.Count;

            public bool IsReadOnly => false;

            public void Add(string key, object? value)
                => _inner.Add(key, value);

            public void Add(KeyValuePair<string, object?> item)
                => ((ICollection<KeyValuePair<string, object?>>)_inner).Add(item);

            public void Clear()
                => _inner.Clear();

            public bool Contains(KeyValuePair<string, object?> item)
                => ((ICollection<KeyValuePair<string, object?>>)_inner).Contains(item);

            public bool ContainsKey(string key)
                => _inner.ContainsKey(key);

            public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
                => ((ICollection<KeyValuePair<string, object?>>)_inner).CopyTo(array, arrayIndex);

            public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
                => _inner.GetEnumerator();

            public bool Remove(string key)
                => _inner.Remove(key);

            public bool Remove(KeyValuePair<string, object?> item)
                => ((ICollection<KeyValuePair<string, object?>>)_inner).Remove(item);

            public bool TryGetValue(string key, out object? value)
                => _inner.TryGetValue(key, out value);

            IEnumerator IEnumerable.GetEnumerator()
                => GetEnumerator();
        }
    }

    [TestClass]
    public sealed class SqlValidationServiceTests
    {
        private readonly SqlValidationService _service = new();

        [TestMethod]
        public void Analyze_accepts_valid_sql_server_2022_tsql()
        {
            // SQL Server 2022 として valid な SQL は ScriptDom AST として解析できる。
            var result = _service.Analyze("""
                SELECT Id, Name
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.IsNotNull(result.Fragment);
            Assert.IsTrue(result.OriginalSql.Contains("dbo.Customers", StringComparison.Ordinal));
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

        [TestMethod]
        public void Inspect_extracts_join_order_group_and_having_metadata()
        {
            // JOIN / ORDER BY / GROUP BY / HAVING も SQL 形状の分岐条件に使える。
            var result = _service.Inspect("""
                SELECT c.Id, COUNT(o.Id) AS OrderCount
                FROM dbo.Customers AS c
                INNER JOIN dbo.Orders AS o ON o.CustomerId = c.Id
                WHERE c.IsActive = @IsActive
                GROUP BY c.Id
                HAVING COUNT(o.Id) > 0
                ORDER BY c.Id
                """);

            CollectionAssert.Contains(result.ReferencedTables.ToList(), "dbo.Customers");
            CollectionAssert.Contains(result.JoinedTables.ToList(), "dbo.Orders");
            CollectionAssert.Contains(result.OrderByColumns.ToList(), "Id");
            CollectionAssert.Contains(result.GroupByColumns.ToList(), "Id");
            CollectionAssert.Contains(result.HavingColumns.ToList(), "Id");
            CollectionAssert.Contains(result.HavingFunctions.ToList(), "COUNT");
        }
    }
}
