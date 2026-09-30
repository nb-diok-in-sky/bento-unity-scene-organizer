using NUnit.Framework;

namespace Bento.Tests
{
    [TestFixture]
    public sealed class PrefabLayerMatcherTests
    {
        static readonly string[] EnglishLayers =
            { "Default", "TransparentFX", "Ignore Raycast", "Water", "UI", "Foliage", "Rocks_S", "Rocks_M", "Rocks_L", "Buildings", "Props" };

        static readonly string[] ChineseLayers = { "Default", "Water", "UI", "植被", "石头_小", "石头_大", "建筑" };

        static string Suggest(string asset, float size, string[] layers) =>
            PrefabLayerMatcher.Suggest(asset, size, layers).Layer;

        [TestCase("SM_PineTree_01", 3f, "Foliage")]
        [TestCase("Bush_Round", 1f, "Foliage")]
        [TestCase("OldHouse", 9f, "Buildings")]
        [TestCase("Crate_Wood", 0.8f, "Props")]
        [TestCase("lake_surface", 50f, "Water")]
        [TestCase("松树_02", 4f, "Foliage")]
        [TestCase("木屋", 7f, "Buildings")]
        public void MatchesByCategory(string asset, float size, string expected) =>
            Assert.AreEqual(expected, Suggest(asset, size, EnglishLayers));

        [TestCase("Boulder_A", 1f, "Rocks_S")]
        [TestCase("Rock_Cliff_03", 5f, "Rocks_M")]
        [TestCase("Rock_Cliff_03", 20f, "Rocks_L")]
        public void PicksSizeVariantByFootprint(string asset, float size, string expected) =>
            Assert.AreEqual(expected, Suggest(asset, size, EnglishLayers));

        [TestCase("石头堆", 1f, "石头_小")]
        [TestCase("Rock_Cliff_03", 20f, "石头_大")]
        [TestCase("OldHouse", 9f, "建筑")]
        [TestCase("SM_PineTree_01", 3f, "植被")]
        public void SupportsChineseLayerNames(string asset, float size, string expected) =>
            Assert.AreEqual(expected, Suggest(asset, size, ChineseLayers));

        [Test]
        public void WordInsideAnotherWordDoesNotMatch()
        {
            // "StreetLamp" contains the letters "tree" but is not a tree.
            Assert.AreNotEqual("Foliage", Suggest("StreetLamp", 0.5f, EnglishLayers));
        }

        [Test]
        public void KeepsCurrentLayerWhenProjectHasNoCategoryLayers()
        {
            string[] engineOnly = { "Default", "TransparentFX", "Ignore Raycast", "UI" };
            Assert.IsNull(Suggest("SM_PineTree_01", 3f, engineOnly));
        }

        [Test]
        public void IgnoresGameplayLayers()
        {
            string[] gameplay = { "Default", "Player", "Enemy", "Interactable" };
            Assert.IsNull(Suggest("Rock_Cliff_03", 20f, gameplay));
        }

        [Test]
        public void SizeThresholdsAreConfigurable()
        {
            var settings = new PrefabLayerMatchSettings { smallMax = 10f, mediumMax = 30f };
            Assert.AreEqual("Rocks_S", PrefabLayerMatcher.Suggest("Rock_Cliff_03", 5f, EnglishLayers, settings).Layer);
        }
    }
}
