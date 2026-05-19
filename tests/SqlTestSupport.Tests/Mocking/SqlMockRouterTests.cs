using System.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport.Tests
{
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
}
