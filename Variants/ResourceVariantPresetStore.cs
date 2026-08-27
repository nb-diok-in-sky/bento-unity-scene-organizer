using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class ResourceVariantPresetStore
    {
        const string PresetKeyPrefix = "Bento.ResourceVariantPresets.";
        const string SourceRecordKeyPrefix = "Bento.ResourceVariantSourceRecords.";

        string PresetKey => BentoUtility.GetProjectEditorPrefsKey(PresetKeyPrefix);
        string SourceRecordKey => BentoUtility.GetProjectEditorPrefsKey(SourceRecordKeyPrefix);

        public ResourceVariantPresetList LoadPresets()
        {
            var list = LoadJson(PresetKey, new ResourceVariantPresetList(), "资源变体预设");
            list.items = list.items ?? new List<ResourceVariantPreset>();
            return list;
        }

        public void SavePresets(ResourceVariantPresetList presets)
        {
            EditorPrefs.SetString(PresetKey, JsonUtility.ToJson(presets));
        }

        public void AddSourceRecords(IEnumerable<ResourceVariantSourceRecord> newRecords)
        {
            var records = LoadJson(SourceRecordKey, new ResourceVariantSourceRecordList(), "资源变体来源记录");
            records.items = records.items ?? new List<ResourceVariantSourceRecord>();
            records.items.AddRange(newRecords);
            EditorPrefs.SetString(SourceRecordKey, JsonUtility.ToJson(records));
        }

        public ResourceVariantSourceRecordList LoadSourceRecords()
        {
            var records = LoadJson(SourceRecordKey, new ResourceVariantSourceRecordList(), "资源变体来源记录");
            records.items = records.items ?? new List<ResourceVariantSourceRecord>();
            return records;
        }

        public void RemoveSourceRecords(ISet<string> prefabPaths)
        {
            var records = LoadJson(SourceRecordKey, new ResourceVariantSourceRecordList(), "资源变体来源记录");
            records.items = records.items ?? new List<ResourceVariantSourceRecord>();
            records.items.RemoveAll(record => record != null && prefabPaths.Contains(record.generatedPrefabPath));
            EditorPrefs.SetString(SourceRecordKey, JsonUtility.ToJson(records));
        }

        static T LoadJson<T>(string key, T fallback, string label) where T : class
        {
            try
            {
                string json = EditorPrefs.GetString(key, string.Empty);
                return string.IsNullOrEmpty(json) ? fallback : JsonUtility.FromJson<T>(json) ?? fallback;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Bento] {label}损坏，已重置：{exception.Message}");
                EditorPrefs.DeleteKey(key);
                return fallback;
            }
        }
    }
}
