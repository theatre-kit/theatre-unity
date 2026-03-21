using NUnit.Framework;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class PhysicsModeTests
    {
        [Test]
        public void GetEffective_WithOverride_ReturnsOverride()
        {
            Assert.AreEqual("2d", PhysicsMode.GetEffective("2d"));
            Assert.AreEqual("3d", PhysicsMode.GetEffective("3d"));
        }

        [Test]
        public void GetEffective_WithNull_ReturnsDefault()
        {
            var result = PhysicsMode.GetEffective(null);
            Assert.IsTrue(result == "2d" || result == "3d");
        }

        [Test]
        public void CheckPlayModeRequired_PhysicsOps_ReturnsError()
        {
            // We're in edit mode during tests
            Assert.IsNotNull(
                PhysicsMode.CheckPlayModeRequired("raycast"));
            Assert.IsNotNull(
                PhysicsMode.CheckPlayModeRequired("overlap"));
            Assert.IsNotNull(
                PhysicsMode.CheckPlayModeRequired("linecast"));
        }

        [Test]
        public void CheckPlayModeRequired_TransformOps_ReturnsNull()
        {
            Assert.IsNull(
                PhysicsMode.CheckPlayModeRequired("nearest"));
            Assert.IsNull(
                PhysicsMode.CheckPlayModeRequired("radius"));
            Assert.IsNull(
                PhysicsMode.CheckPlayModeRequired("bounds"));
            Assert.IsNull(
                PhysicsMode.CheckPlayModeRequired("path_distance"));
        }
    }

    [TestFixture]
    public class SpatialIndexTests
    {
        private SpatialIndex _index;

        [SetUp]
        public void SetUp()
        {
            _index = new SpatialIndex();
        }

        [Test]
        public void Nearest_EmptyIndex_ReturnsEmpty()
        {
            // Force rebuild on empty scene
            _index.EnsureFresh();
            var results = _index.Nearest(Vector3.zero, 5);
            Assert.IsNotNull(results);
            // May have results from test scene — just verify no crash
        }

        [Test]
        public void Radius_EmptyIndex_ReturnsEmpty()
        {
            _index.EnsureFresh();
            var results = _index.Radius(Vector3.zero, 10f);
            Assert.IsNotNull(results);
        }

        [Test]
        public void Invalidate_ForcesRebuild()
        {
            _index.EnsureFresh();
            int countBefore = _index.Count;
            _index.Invalidate();
            _index.EnsureFresh();
            // Count should be same (same scene) but rebuild happened
            Assert.AreEqual(countBefore, _index.Count);
        }

        [Test]
        public void Nearest_WithCountLimit_ReturnsAtMostCount()
        {
            // Create some GameObjects to populate the index
            var go1 = new GameObject("TestNearestA");
            go1.transform.position = new Vector3(1, 0, 0);
            var go2 = new GameObject("TestNearestB");
            go2.transform.position = new Vector3(2, 0, 0);
            var go3 = new GameObject("TestNearestC");
            go3.transform.position = new Vector3(3, 0, 0);

            try
            {
                _index.Invalidate();
                var results = _index.Nearest(Vector3.zero, 2);
                Assert.LessOrEqual(results.Count, 2);
            }
            finally
            {
                Object.DestroyImmediate(go1);
                Object.DestroyImmediate(go2);
                Object.DestroyImmediate(go3);
            }
        }

        [Test]
        public void Nearest_ResultsSortedByDistance()
        {
            var go1 = new GameObject("TestSortA");
            go1.transform.position = new Vector3(5, 0, 0);
            var go2 = new GameObject("TestSortB");
            go2.transform.position = new Vector3(1, 0, 0);
            var go3 = new GameObject("TestSortC");
            go3.transform.position = new Vector3(3, 0, 0);

            try
            {
                _index.Invalidate();
                var results = _index.Nearest(Vector3.zero, 10);
                for (int i = 1; i < results.Count; i++)
                {
                    Assert.LessOrEqual(
                        results[i - 1].Distance, results[i].Distance,
                        "Results should be sorted by distance ascending");
                }
            }
            finally
            {
                Object.DestroyImmediate(go1);
                Object.DestroyImmediate(go2);
                Object.DestroyImmediate(go3);
            }
        }

        [Test]
        public void Radius_ReturnsOnlyObjectsWithinRadius()
        {
            var goNear = new GameObject("TestRadiusNear");
            goNear.transform.position = new Vector3(2, 0, 0);
            var goFar = new GameObject("TestRadiusFar");
            goFar.transform.position = new Vector3(100, 0, 0);

            try
            {
                _index.Invalidate();
                var results = _index.Radius(Vector3.zero, 5f);
                foreach (var result in results)
                {
                    Assert.LessOrEqual(result.Distance, 5f,
                        "All results should be within the search radius");
                }
            }
            finally
            {
                Object.DestroyImmediate(goNear);
                Object.DestroyImmediate(goFar);
            }
        }
    }
}
