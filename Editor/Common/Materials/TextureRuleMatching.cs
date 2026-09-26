using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static partial class TexRules
    {
        public static (string baseName, string suffix) ExtractSuffix(
            string name, HashSet<string> allNames, bool allowSingleCharSuffix = true)
        {
            if (allNames.Any(n => n != name && n.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)))
                return (name, "");

            foreach (var (known, _) in BuiltInByLength)
            {
                if (!allowSingleCharSuffix && known.Length == 1) continue;
                string ending = "_" + known;
                if (name.EndsWith(ending, StringComparison.OrdinalIgnoreCase) && name.Length > ending.Length)
                    return (name.Substring(0, name.Length - ending.Length),
                            name.Substring(name.Length - known.Length));
            }

            int idx = name.LastIndexOf('_');
            if (idx > 0)
            {
                string potentialSuffix = name.Substring(idx + 1);
                if (potentialSuffix.Length > 0 && !potentialSuffix.All(char.IsDigit) &&
                    (allowSingleCharSuffix || potentialSuffix.Length > 1))
                    return (name.Substring(0, idx), potentialSuffix);
            }

            return (name, "");
        }

        public static TexType GuessType(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return TexType.BaseColor;
            foreach (var (known, type) in BuiltIn)
                if (suffix.Equals(known, StringComparison.OrdinalIgnoreCase))
                    return type;
            return TexType.Unknown;
        }

        public static Dictionary<TexType, string> GetPropertyMap(Material mat)
        {
            if (mat.shader != null && mat.shader.name.Contains("Universal"))
                return URPLitProps;
            return StandardProps;
        }
    }
}
