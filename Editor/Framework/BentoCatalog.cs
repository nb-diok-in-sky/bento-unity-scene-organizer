using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    /// <summary>Collects every class marked with <see cref="BentoPageAttribute"/> across loaded assemblies.</summary>
    static class BentoCatalog
    {
        public static List<RegisteredToolPage> BuildPages()
        {
            var pages = new List<RegisteredToolPage>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Type type in TypeCache.GetTypesWithAttribute<BentoPageAttribute>())
            {
                var info = (BentoPageAttribute)Attribute.GetCustomAttribute(type, typeof(BentoPageAttribute));
                string problem = Validate(type, info);
                if (problem == null && !ids.Add(info.Id))
                    problem = $"页面 id“{info.Id}”重复";
                if (problem != null)
                {
                    Debug.LogError($"[Bento] 跳过页面 {type.FullName}：{problem}");
                    continue;
                }
                pages.Add(new RegisteredToolPage(type, info));
            }

            return pages
                .OrderBy(page => GroupRank(page.Group))
                .ThenBy(page => page.Group, StringComparer.Ordinal)
                .ThenBy(page => page.Order)
                .ThenBy(page => page.Name, StringComparer.Ordinal)
                .ToList();
        }

        static string Validate(Type type, BentoPageAttribute info)
        {
            if (type.IsAbstract) return "类型是抽象类";
            if (!typeof(IBentoTool).IsAssignableFrom(type)) return $"没有实现 {nameof(IBentoTool)}";
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (type.GetConstructor(flags, null, Type.EmptyTypes, null) == null) return "缺少无参构造函数";
            if (string.IsNullOrWhiteSpace(info.Id) || string.IsNullOrWhiteSpace(info.Group) ||
                string.IsNullOrWhiteSpace(info.Name))
                return "id、分组或名称为空";
            return null;
        }

        static int GroupRank(string group)
        {
            int index = Array.IndexOf(BentoGroups.DisplayOrder, group);
            return index < 0 ? int.MaxValue : index;
        }
    }
}
