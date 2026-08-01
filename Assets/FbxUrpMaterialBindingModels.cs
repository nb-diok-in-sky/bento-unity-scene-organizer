using UnityEngine;

namespace Bento
{
    enum FbxTextureRole
    {
        BaseColor,
        Normal,
        Metallic,
        AO,
        Height,
        Emission,
        PackedMask,
        Ignore
    }

    enum PackedMaskChannel
    {
        R,
        G,
        B,
        A,
        One,
        Zero
    }

    sealed class FbxTextureBinding
    {
        public Texture2D texture;
        public FbxTextureRole role;
    }

    sealed class PackedMaskLayout
    {
        public PackedMaskChannel metallic = PackedMaskChannel.R;
        public PackedMaskChannel smoothness = PackedMaskChannel.G;
        public bool invertSmoothness;
        public PackedMaskChannel occlusion = PackedMaskChannel.B;
    }

    sealed class FbxBatchMaterialEntry
    {
        public GameObject fbx;
        public Texture2D baseColor;
        public Texture2D normal;
        public Texture2D packedMask;
        public string folder;
        public bool include = true;
        public string note;
    }

    sealed class FbxBatchMaterialResult
    {
        public int created;
        public readonly System.Collections.Generic.List<string> failures = new();
    }
}
