using System;
using System.Linq;
using NUnit.Framework;

namespace Bento.Tests
{
    [TestFixture]
    public sealed class ToolCatalogTests
    {
        static readonly string[] CorePageIds =
        {
            "folder_favorites", "batch_rename", "asset_move", "asset_operation_history", "asset_info",
            "suffix_cleanup", "recursive_extract", "hierarchy_organization", "scene_light_batch",
            "scene_renderer_shadow", "model_import_workflow", "resource_variant", "fbx_material_inherit",
            "prefab_layer", "tag_layer", "shader_replace", "select_used_assets", "select_terrain_paint_textures",
            "terrain_layer_generation", "scene_material_replace", "scene_material_usage", "scene_material_picker",
            "prefab_replace", "scene_model_instance_audit", "lightmap_uv_bake"
        };

        [Test]
        public void AllCorePagesAreRegistered()
        {
            var ids = BentoCatalog.BuildPages().Select(page => page.Id).ToList();
            CollectionAssert.IsSubsetOf(CorePageIds, ids);
        }

        [Test]
        public void PageIdsAreUnique()
        {
            var ids = BentoCatalog.BuildPages().Select(page => page.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
        }

        [Test]
        public void EveryPageCanBeInstantiated()
        {
            foreach (var page in BentoCatalog.BuildPages())
                Assert.DoesNotThrow(() => Activator.CreateInstance(page.ToolType, nonPublic: true), page.Id);
        }

        [Test]
        public void PagesAreGroupedInDisplayOrder()
        {
            var ranks = BentoCatalog.BuildPages()
                .Select(page => Array.IndexOf(BentoGroups.DisplayOrder, page.Group))
                .Select(rank => rank < 0 ? int.MaxValue : rank)
                .ToList();
            CollectionAssert.IsOrdered(ranks);
        }
    }
}
