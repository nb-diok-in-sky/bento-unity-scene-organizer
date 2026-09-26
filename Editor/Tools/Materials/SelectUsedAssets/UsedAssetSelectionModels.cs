using System.Collections.Generic;
using UnityEngine;

namespace Bento
{
    sealed class UsedAssetSelectionResult
    {
        public readonly List<GameObject> roots = new List<GameObject>();
        public readonly List<Object> models = new List<Object>();
        public readonly List<Material> materials = new List<Material>();
        public readonly List<Texture> textures = new List<Texture>();
        public readonly List<TextureScanTrace> textureTraces = new List<TextureScanTrace>();

        public IEnumerable<Object> AllAssets()
        {
            foreach (Object model in models)
                if (model != null)
                    yield return model;
            foreach (Material material in materials)
                if (material != null)
                    yield return material;
            foreach (Texture texture in textures)
                if (texture != null)
                    yield return texture;
        }
    }

    readonly struct MaterialTextureReference
    {
        public MaterialTextureReference(string propertyName, Texture texture)
        {
            PropertyName = propertyName;
            Texture = texture;
        }

        public string PropertyName { get; }
        public Texture Texture { get; }
    }

    sealed class TextureScanTrace
    {
        public string renderer;
        public string material;
        public string property;
        public string texture;
        public string path;
        public string type;
        public bool isSubAsset;
        public bool included;
    }
}
