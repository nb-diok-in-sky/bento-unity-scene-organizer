using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bento
{
    sealed class PrefabLayerMatchSettings
    {
        /// <summary>Horizontal size (metres) below which the smallest size variant is used.</summary>
        public float smallMax = 2f;

        /// <summary>Horizontal size (metres) below which the medium size variant is used.</summary>
        public float mediumMax = 8f;
    }

    readonly struct PrefabLayerSuggestion
    {
        public PrefabLayerSuggestion(string layer, string reason, float confidence)
        {
            Layer = layer;
            Reason = reason;
            Confidence = confidence;
        }

        /// <summary>Null when no project layer matched.</summary>
        public string Layer { get; }
        public string Reason { get; }
        public float Confidence { get; }
    }

    /// <summary>
    /// Suggests a layer by matching asset names against the layers the project itself defines,
    /// so it adapts to any layer scheme instead of assuming one. A layer matches when its name
    /// appears in the asset name, or when both belong to the same generic category (for example
    /// a layer named "Foliage" matches an asset named "pine_tree"). When a project has size
    /// variants of the same layer ("Rocks_S" / "Rocks_L", "树_小" / "树_大"), the asset's
    /// footprint picks the variant.
    /// </summary>
    static class PrefabLayerMatcher
    {
        const float DirectConfidence = 0.9f;
        const float CategoryConfidence = 0.7f;

        /// <summary>Built-in layers that describe engine behaviour rather than content.</summary>
        static readonly HashSet<string> NonContentLayers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Default", "TransparentFX", "Ignore Raycast", "UI" };

        /// <summary>Generic, engine-agnostic categories. A term can belong to several categories.</summary>
        static readonly string[][] Categories =
        {
            new[] { "water", "river", "lake", "sea", "ocean", "pond", "waterfall", "水", "河", "湖", "海" },
            new[] { "terrain", "ground", "land", "landscape", "地形", "地面" },
            new[] { "grass", "lawn", "weed", "草" },
            new[] { "bush", "shrub", "hedge", "灌木" },
            new[] { "tree", "pine", "palm", "oak", "trunk", "树" },
            new[] { "vegetation", "foliage", "plant", "flora", "flower", "tree", "bush", "grass", "植被", "植物", "花", "树", "草" },
            new[] { "rock", "stone", "boulder", "cliff", "pebble", "石", "岩" },
            new[] { "building", "house", "hut", "cabin", "shed", "castle", "wall", "roof", "architecture", "structure", "tower", "建筑", "房", "屋", "楼", "墙", "塔", "亭" },
            new[] { "prop", "item", "furniture", "decoration", "decor", "crate", "barrel", "box", "chest", "table", "chair", "bench", "lamp", "lantern", "sign", "道具", "物件", "摆件", "箱", "桶", "桌", "椅", "灯" },
            new[] { "road", "path", "street", "bridge", "路", "桥" },
            new[] { "character", "npc", "player", "enemy", "角色", "人物" },
            new[] { "vehicle", "car", "cart", "boat", "ship", "车", "船" },
            new[] { "effect", "fx", "vfx", "particle", "特效" },
        };

        static readonly Dictionary<string, int> SizeWords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "tiny", 0 }, { "small", 0 }, { "mini", 0 }, { "s", 0 }, { "小", 0 },
            { "medium", 1 }, { "mid", 1 }, { "middle", 1 }, { "m", 1 }, { "中", 1 },
            { "large", 2 }, { "big", 2 }, { "huge", 2 }, { "l", 2 }, { "大", 2 },
        };

        static readonly Regex TokenPattern = new Regex(@"[一-鿿]+|[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+", RegexOptions.Compiled);

        /// <summary>Layers of the given list that can describe content (excludes Default, UI and similar).</summary>
        public static List<string> ContentLayers(IEnumerable<string> layerNames) =>
            layerNames.Where(name => !string.IsNullOrWhiteSpace(name) && !NonContentLayers.Contains(name)).ToList();

        public static PrefabLayerSuggestion Suggest(
            string assetName,
            float horizontalSize,
            IEnumerable<string> projectLayers,
            PrefabLayerMatchSettings settings = null)
        {
            settings = settings ?? new PrefabLayerMatchSettings();
            var assetTokens = Tokenize(assetName).Select(Singular).ToList();
            string assetText = (assetName ?? string.Empty).ToLowerInvariant();

            var matches = new List<(LayerShape shape, float confidence, string reason)>();
            foreach (string layer in ContentLayers(projectLayers))
            {
                var shape = LayerShape.Parse(layer);
                if (shape.BaseTokens.Count == 0) continue;
                if (TryMatch(shape, assetTokens, assetText, out float confidence, out string reason))
                    matches.Add((shape, confidence, reason));
            }

            if (matches.Count == 0)
                return new PrefabLayerSuggestion(null, "没有匹配到项目中的 Layer，保持原样", 0f);

            float best = matches.Max(match => match.confidence);
            var top = matches.Where(match => match.confidence == best).ToList();
            string baseKey = top.OrderByDescending(match => match.shape.BaseKey.Length).First().shape.BaseKey;
            var variants = top.Where(match => match.shape.BaseKey == baseKey).ToList();
            var chosen = variants.Count == 1 ? variants[0] : PickBySize(variants, horizontalSize, settings);
            string sizeNote = variants.Count > 1 ? $"；按尺寸 {horizontalSize:0.#}m 选择" : string.Empty;
            return new PrefabLayerSuggestion(chosen.shape.Name, chosen.reason + sizeNote, chosen.confidence);
        }

        static bool TryMatch(LayerShape shape, List<string> assetTokens, string assetText, out float confidence, out string reason)
        {
            foreach (string token in shape.BaseTokens)
            {
                if (TermMatches(token, assetTokens, assetText))
                {
                    confidence = DirectConfidence;
                    reason = $"资产名包含 Layer 名“{token}”";
                    return true;
                }
            }

            foreach (string[] category in Categories.Where(category => shape.BaseTokens.Any(token => InCategory(token, category))))
            {
                string term = category.FirstOrDefault(candidate => TermMatches(candidate, assetTokens, assetText));
                if (term == null) continue;
                confidence = CategoryConfidence;
                reason = $"资产名中的“{term}”与 Layer“{shape.Name}”属于同一类";
                return true;
            }

            confidence = 0f;
            reason = null;
            return false;
        }

        static (LayerShape shape, float confidence, string reason) PickBySize(
            List<(LayerShape shape, float confidence, string reason)> variants,
            float size,
            PrefabLayerMatchSettings settings)
        {
            var ordered = variants.OrderBy(variant => variant.shape.SizeRank).ToList();
            if (size < settings.smallMax) return ordered.First();
            if (size < settings.mediumMax && ordered.Count > 2) return ordered[1];
            return ordered.Last();
        }

        /// <summary>CJK terms match as substrings; Latin terms must equal a whole word of the asset name.</summary>
        static bool TermMatches(string term, List<string> assetTokens, string assetText)
        {
            if (IsCjk(term)) return assetText.Contains(term);
            return term.Length >= 2 && assetTokens.Contains(Singular(term));
        }

        static bool InCategory(string layerToken, string[] category)
        {
            if (IsCjk(layerToken)) return category.Any(term => IsCjk(term) && layerToken.Contains(term));
            string singular = Singular(layerToken);
            return category.Any(term => !IsCjk(term) && Singular(term) == singular);
        }

        static IEnumerable<string> Tokenize(string value) =>
            string.IsNullOrEmpty(value)
                ? Enumerable.Empty<string>()
                : TokenPattern.Matches(value).Cast<Match>().Select(match => match.Value.ToLowerInvariant());

        static string Singular(string token)
        {
            if (token.Length > 4 && token.EndsWith("ies", StringComparison.Ordinal)) return token.Substring(0, token.Length - 3) + "y";
            if (token.Length > 3 && token.EndsWith("es", StringComparison.Ordinal) &&
                (token.EndsWith("shes", StringComparison.Ordinal) || token.EndsWith("ches", StringComparison.Ordinal)))
                return token.Substring(0, token.Length - 2);
            if (token.Length > 3 && token.EndsWith("s", StringComparison.Ordinal) && !token.EndsWith("ss", StringComparison.Ordinal))
                return token.Substring(0, token.Length - 1);
            return token;
        }

        static bool IsCjk(string value) => value.Length > 0 && value[0] >= '一' && value[0] <= '鿿';

        sealed class LayerShape
        {
            public string Name;
            public List<string> BaseTokens;
            public string BaseKey;
            public int SizeRank = -1;

            public static LayerShape Parse(string layer)
            {
                var tokens = Tokenize(layer).ToList();
                var shape = new LayerShape { Name = layer, BaseTokens = new List<string>() };
                foreach (string token in tokens)
                {
                    string sizeToken = token;
                    string remainder = null;
                    if (IsCjk(token) && token.Length > 1 && SizeWords.ContainsKey(token.Substring(token.Length - 1)))
                    {
                        sizeToken = token.Substring(token.Length - 1);
                        remainder = token.Substring(0, token.Length - 1);
                    }
                    if (SizeWords.TryGetValue(sizeToken, out int rank) && (remainder != null || tokens.Count > 1))
                    {
                        shape.SizeRank = rank;
                        if (remainder != null) shape.BaseTokens.Add(remainder);
                        continue;
                    }
                    shape.BaseTokens.Add(token);
                }
                shape.BaseKey = string.Join("_", shape.BaseTokens);
                return shape;
            }
        }
    }
}
