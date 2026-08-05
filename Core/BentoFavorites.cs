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

        public static IReadOnlyList<IBentoPage> Resolve(IEnumerable<IBentoPage> pages)
        {
            EnsureLoaded();
            return pages.Where(page => _ids.Contains(page.Id)).ToList();
        }

        static void EnsureLoaded()
        {
            if (_ids != null) return;
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
