using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Bento
{
    static class ObjMaterialParser
    {
        public static List<ObjMaterialDefinition> Parse(string mtlAbsolutePath)
        {
            var results = new List<ObjMaterialDefinition>();
            ObjMaterialDefinition current = null;
            foreach (string raw in File.ReadAllLines(mtlAbsolutePath))
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int space = line.IndexOf(' ');
                string command = space < 0 ? line : line.Substring(0, space);
                string value = space < 0 ? string.Empty : line.Substring(space + 1).Trim();
                if (command.Equals("newmtl", StringComparison.OrdinalIgnoreCase))
                {
                    current = new ObjMaterialDefinition { name = string.IsNullOrWhiteSpace(value) ? "Material" : value };
                    results.Add(current);
                }
                else if (current != null)
                {
                    if (command.Equals("map_Kd", StringComparison.OrdinalIgnoreCase)) current.albedoFile = ExtractTextureFile(value);
                    else if (command.Equals("map_Bump", StringComparison.OrdinalIgnoreCase) || command.Equals("bump", StringComparison.OrdinalIgnoreCase)) current.normalFile = ExtractTextureFile(value);
                    else if (command.Equals("map_Pm", StringComparison.OrdinalIgnoreCase)) current.metallicFile = ExtractTextureFile(value);
                    else if (command.Equals("map_Pr", StringComparison.OrdinalIgnoreCase)) current.roughnessFile = ExtractTextureFile(value);
                    else if (command.Equals("d", StringComparison.OrdinalIgnoreCase) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float opacity)) current.opacity = opacity;
                }
            }
            return results;
        }

        static string ExtractTextureFile(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string[] parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? string.Empty : parts[parts.Length - 1].Trim('"');
        }
    }
}
