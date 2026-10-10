using NUnit.Framework;
using UnityEngine;

namespace Bento.Tests
{
    [TestFixture]
    public sealed class HierarchyParentCleanupTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("BentoCleanupTestRoot");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        Transform Create(string name, Transform parent, Vector3 position, Vector3 euler, Vector3 scale)
        {
            var transform = new GameObject(name).transform;
            transform.SetParent(parent ?? _root.transform, false);
            transform.position = position;
            transform.rotation = Quaternion.Euler(euler);
            transform.localScale = scale;
            return transform;
        }

        [Test]
        public void CleaningAnEmptySkewedParentRemovesTheDeformationRisk()
        {
            Transform parent = Create("Parent", null, new Vector3(3f, 1f, -2f), new Vector3(0f, 30f, 0f), new Vector3(1f, 3f, 1f));
            Transform child = Create("Child", null, Vector3.zero, new Vector3(0f, 0f, 45f), Vector3.one);
            Assume.That(HierarchyTransformRiskAnalyzer.HasReparentDeformationRisk(child, parent), Is.True);

            Vector3 position = parent.position;
            HierarchyParentCleanupService.Clean(parent);

            Assert.IsFalse(HierarchyTransformRiskAnalyzer.HasReparentDeformationRisk(child, parent));
            Assert.That(Vector3.Distance(position, parent.position), Is.LessThan(1e-4f));
            Assert.IsTrue(HierarchyParentCleanupService.IsClean(parent));
        }

        [Test]
        public void ParentWithChildrenIsNotCleaned()
        {
            Transform parent = Create("Parent", null, Vector3.zero, new Vector3(0f, 30f, 0f), new Vector3(1f, 3f, 1f));
            Create("Existing", parent, Vector3.one, Vector3.zero, Vector3.one);
            Assert.IsNotNull(HierarchyParentCleanupService.GetBlocker(parent));
            Assert.Throws<System.InvalidOperationException>(() => HierarchyParentCleanupService.Clean(parent));
        }

        [Test]
        public void ScaledAncestorIsCompensated()
        {
            Transform ancestor = Create("Ancestor", null, Vector3.zero, Vector3.zero, new Vector3(0.01f, 0.01f, 0.01f));
            Transform parent = Create("Parent", ancestor, new Vector3(1f, 0f, 0f), Vector3.zero, Vector3.one);
            Assume.That(HierarchyParentCleanupService.IsClean(parent), Is.False);

            HierarchyParentCleanupService.Clean(parent);

            Assert.That((parent.lossyScale - Vector3.one).magnitude, Is.LessThan(1e-3f));
        }
    }
}
