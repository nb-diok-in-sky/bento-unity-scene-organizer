using System.Collections.Generic;

namespace Bento.Optional
{
    public static class FbxExporterToolPageProvider
    {
        public static void AppendPages(ICollection<IBentoPage> pages)
        {
            OptionalToolPageRegistry.AddPage(
                pages,
                "资源、模型和预制体",
                "obj_to_fbx",
                "OBJ 转项目 FBX",
                new ObjToFbxConversionTool(),
                tool => tool.Draw());
        }
    }
}
