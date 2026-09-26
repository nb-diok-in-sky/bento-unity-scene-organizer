using System;

namespace Bento
{
    [Serializable]
    sealed class ObjMaterialDefinition
    {
        public string name;
        public string albedoFile;
        public string normalFile;
        public string metallicFile;
        public string roughnessFile;
        public float opacity = 1f;
    }
}
