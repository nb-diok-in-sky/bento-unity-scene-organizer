using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Bento
{
    static class BentoFavorites
    {
        const string EditorPrefsKey = "Bento.FavoritePageIds";
        const char Separator = '|';

        /// <summary>Favorites shown before the user has customized any, so first-time users see the everyday tools.</summary>
        internal static readonly string[] DefaultIds =
        {
            "folder_favorites", "batch_rename", "asset_move", "select_used_assets", "hierarchy_organization",
            "tag_layer", "prefab_replace", "resource_variant"
        };

        static HashSet<string> _ids;

        public static bool Contains(string pageId)
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(pageId) && _ids.Contains(pageId);
        }

        public static void Toggle(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return;
            EnsureLoaded();
            if (!_ids.Add(pageId))
                _ids.Remove(pageId);
            Save();
        }

        public static IReadOnlyList<TPage> Resolve<TPage>(IEnumerable<TPage> pages) where TPage : IBentoPage
        {
            EnsureLoaded();
            return pages.Where(page => _ids.Contains(page.Id)).ToList();
        }

        static void EnsureLoaded()
        {
            if (_ids != null) return;
            if (!EditorPrefs.HasKey(EditorPrefsKey))
            {
                _ids = new HashSet<string>(DefaultIds, StringComparer.OrdinalIgnoreCase);
                return;
            }
            string serialized = EditorPrefs.GetString(EditorPrefsKey, string.Empty);
            _ids = new HashSet<string>(
                serialized.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        }

        static void Save()
        {
            EditorPrefs.SetString(EditorPrefsKey, string.Join(Separator.ToString(), _ids.OrderBy(id => id)));
        }
    }
}
