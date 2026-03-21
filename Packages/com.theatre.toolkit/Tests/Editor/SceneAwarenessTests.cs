using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Theatre.Stage;
using Theatre.Editor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class ResponseHelpersTests
    {
        [Test]
        public void ToJArray_Vector3_ProducesCorrectValues()
        {
            var arr = ResponseHelpers.ToJArray(new Vector3(1.234f, 5.678f, 9.012f));
            Assert.AreEqual(3, arr.Count);
            // 2 decimal places
            Assert.AreEqual(1.23, arr[0].ToObject<double>(), 0.001);
            Assert.AreEqual(5.68, arr[1].ToObject<double>(), 0.001);
            Assert.AreEqual(9.01, arr[2].ToObject<double>(), 0.001);
        }

        [Test]
        public void ErrorResponse_ContainsAllFields()
        {
            var json = ResponseHelpers.ErrorResponse(
                "test_error", "Something failed", "Try again");
            Assert.That(json, Does.Contain("\"code\":\"test_error\""));
            Assert.That(json, Does.Contain("\"message\":\"Something failed\""));
            Assert.That(json, Does.Contain("\"suggestion\":\"Try again\""));
        }

        [Test]
        public void ToSnakeCase_ConvertsCorrectly()
        {
            Assert.AreEqual("local_position",
                PropertySerializer.ToSnakeCase("localPosition"));
            Assert.AreEqual("is_grounded",
                PropertySerializer.ToSnakeCase("isGrounded"));
            Assert.AreEqual("active_self",
                PropertySerializer.ToSnakeCase("activeSelf"));
        }
    }

    [TestFixture]
    public class TokenBudgetTests
    {
        [Test]
        public void DefaultBudget_Is1500()
        {
            Assert.AreEqual(1500, TokenBudget.DefaultBudget);
        }

        [Test]
        public void HardCap_ClampsLargeBudget()
        {
            var budget = new TokenBudget(10000);
            Assert.AreEqual(TokenBudget.HardCap, budget.Budget);
        }

        [Test]
        public void MinimumBudget_Clamps()
        {
            var budget = new TokenBudget(10);
            Assert.AreEqual(100, budget.Budget);
        }

        [Test]
        public void WouldExceed_ReturnsTrueAtLimit()
        {
            var budget = new TokenBudget(100); // 100 tokens = 400 chars
            budget.Add(380);
            Assert.IsFalse(budget.IsExhausted);
            Assert.IsTrue(budget.WouldExceed(100)); // 480/4 = 120 > 100
        }

        [Test]
        public void EstimateTokens_FourCharsPerToken()
        {
            // 100 characters / 4 = 25 tokens
            Assert.AreEqual(25, TokenBudget.EstimateTokens(new string('x', 100)));
        }

        [Test]
        public void Add_IncrementsCharCount()
        {
            var budget = new TokenBudget(1500);
            Assert.AreEqual(0, budget.EstimatedTokens);
            budget.Add(400);
            Assert.AreEqual(100, budget.EstimatedTokens);
        }

        [Test]
        public void IsExhausted_WhenTokensAtOrAboveBudget()
        {
            var budget = new TokenBudget(100);
            budget.Add(400); // 100 tokens = exactly at budget
            Assert.IsTrue(budget.IsExhausted);
        }

        [Test]
        public void ToBudgetJObject_ContainsRequiredFields()
        {
            var budget = new TokenBudget(500);
            budget.Add(200);
            var obj = budget.ToBudgetJObject(
                truncated: true,
                reason: "budget",
                suggestion: "narrow scope");

            Assert.AreEqual(500, obj["requested"].ToObject<int>());
            Assert.AreEqual(50, obj["used"].ToObject<int>()); // 200/4
            Assert.IsTrue(obj["truncated"].ToObject<bool>());
            Assert.AreEqual("budget", obj["truncation_reason"].ToObject<string>());
            Assert.AreEqual("narrow scope", obj["suggestion"].ToObject<string>());
        }

        [Test]
        public void ToBudgetJObject_OmitsTruncationFields_WhenNotTruncated()
        {
            var budget = new TokenBudget(500);
            var obj = budget.ToBudgetJObject(truncated: false);

            Assert.IsFalse(obj["truncated"].ToObject<bool>());
            Assert.IsNull(obj["truncation_reason"]);
            Assert.IsNull(obj["suggestion"]);
        }
    }

    [TestFixture]
    public class PaginationCursorTests
    {
        [Test]
        public void Encode_Decode_RoundTrips()
        {
            var sceneName = SceneManager.GetActiveScene().name;
            var encoded = PaginationCursor.Create(
                "scene_hierarchy", "list", 50);
            var decoded = PaginationCursor.Decode(encoded, sceneName);

            Assert.IsNotNull(decoded);
            Assert.AreEqual("scene_hierarchy", decoded.Tool);
            Assert.AreEqual("list", decoded.Operation);
            Assert.AreEqual(50, decoded.Offset);
        }

        [Test]
        public void Decode_ReturnsNull_ForSceneChange()
        {
            var encoded = PaginationCursor.Create(
                "scene_hierarchy", "list", 50);
            var decoded = PaginationCursor.Decode(encoded, "DifferentScene");
            Assert.IsNull(decoded);
        }

        [Test]
        public void Decode_ReturnsNull_ForGarbage()
        {
            var decoded = PaginationCursor.Decode("not-valid-base64!!!", "scene");
            Assert.IsNull(decoded);
        }

        [Test]
        public void Decode_ReturnsNull_ForEmptyString()
        {
            var decoded = PaginationCursor.Decode("", "scene");
            Assert.IsNull(decoded);
        }

        [Test]
        public void BuildPaginationJObject_ContainsExpectedFields()
        {
            var obj = PaginationCursor.BuildPaginationJObject(
                "abc123", hasMore: true, returned: 50, total: 200);
            Assert.AreEqual("abc123", obj["cursor"].ToObject<string>());
            Assert.IsTrue(obj["has_more"].ToObject<bool>());
            Assert.AreEqual(50, obj["returned"].ToObject<int>());
            Assert.AreEqual(200, obj["total"].ToObject<int>());
        }

        [Test]
        public void BuildPaginationJObject_OmitsCursor_WhenNull()
        {
            var obj = PaginationCursor.BuildPaginationJObject(
                null, hasMore: false, returned: 10);
            Assert.IsNull(obj["cursor"]);
            Assert.AreEqual(10, obj["returned"].ToObject<int>());
        }
    }

    [TestFixture]
    public class ClusteringTests
    {
        [Test]
        public void Compute_GroupsNearbyObjects()
        {
            var entries = new System.Collections.Generic.List<HierarchyEntry>();
            // Cluster 1: three objects near (10, 0, 5)
            entries.Add(new HierarchyEntry { Position = new Vector3(10, 0, 5), Name = "A", Path = "/Enemies/A", Components = new[] { "Transform" } });
            entries.Add(new HierarchyEntry { Position = new Vector3(11, 0, 6), Name = "B", Path = "/Enemies/B", Components = new[] { "Transform" } });
            entries.Add(new HierarchyEntry { Position = new Vector3(10.5f, 0, 5.5f), Name = "C", Path = "/Enemies/C", Components = new[] { "Transform" } });

            var clusters = Clustering.Compute(entries);
            Assert.AreEqual(1, clusters.Count);
            Assert.AreEqual(3, clusters[0].Count);
        }

        [Test]
        public void Compute_ReturnsEmpty_ForSingleObject()
        {
            var entries = new System.Collections.Generic.List<HierarchyEntry>
            {
                new HierarchyEntry { Position = Vector3.zero, Name = "Solo" }
            };
            var clusters = Clustering.Compute(entries);
            Assert.AreEqual(0, clusters.Count);
        }

        [Test]
        public void GetUnclustered_ReturnsSingletons()
        {
            var entries = new System.Collections.Generic.List<HierarchyEntry>();
            // Cluster: two nearby
            entries.Add(new HierarchyEntry { Position = new Vector3(0, 0, 0), Name = "A", Path = "/A" });
            entries.Add(new HierarchyEntry { Position = new Vector3(0.1f, 0, 0), Name = "B", Path = "/B" });
            // Singleton: far away
            entries.Add(new HierarchyEntry { Position = new Vector3(100, 0, 100), Name = "Solo", Path = "/Solo" });

            var unclustered = Clustering.GetUnclustered(entries);
            Assert.AreEqual(1, unclustered.Count);
            Assert.AreEqual("Solo", unclustered[0].Name);
        }

        [Test]
        public void Compute_ReturnsEmpty_ForNullInput()
        {
            var clusters = Clustering.Compute(null);
            Assert.AreEqual(0, clusters.Count);
        }
    }

    [TestFixture]
    public class ObjectResolverTests
    {
        private GameObject _testObject;

        [SetUp]
        public void SetUp()
        {
            _testObject = new GameObject("TestResolverTarget");
        }

        [TearDown]
        public void TearDown()
        {
            if (_testObject != null)
                Object.DestroyImmediate(_testObject);
        }

        [Test]
        public void Resolve_ByPath_FindsObject()
        {
            var result = ObjectResolver.Resolve(path: "/TestResolverTarget");
            Assert.IsTrue(result.Success);
            Assert.AreEqual(_testObject, result.GameObject);
        }

        [Test]
        public void Resolve_ByInstanceId_FindsObject()
        {
            var result = ObjectResolver.Resolve(
                instanceId: _testObject.GetInstanceID());
            Assert.IsTrue(result.Success);
            Assert.AreEqual(_testObject, result.GameObject);
        }

        [Test]
        public void Resolve_NoParams_ReturnsError()
        {
            var result = ObjectResolver.Resolve();
            Assert.IsFalse(result.Success);
            Assert.AreEqual("invalid_parameter", result.ErrorCode);
        }

        [Test]
        public void Resolve_NonexistentPath_ReturnsError()
        {
            var result = ObjectResolver.Resolve(path: "/DoesNotExist");
            Assert.IsFalse(result.Success);
            Assert.AreEqual("gameobject_not_found", result.ErrorCode);
        }

        [Test]
        public void GetAllRoots_IncludesTestObject()
        {
            var roots = ObjectResolver.GetAllRoots();
            bool found = false;
            foreach (var root in roots)
            {
                if (root == _testObject)
                {
                    found = true;
                    break;
                }
            }
            Assert.IsTrue(found, "GetAllRoots should include the test GameObject");
        }
    }
}
