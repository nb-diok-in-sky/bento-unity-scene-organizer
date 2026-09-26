using System;

namespace Bento
{
    /// <summary>
    /// Registers a class as a page of the Bento toolbox window. The class must implement
    /// <see cref="IBentoTool"/> and have a parameterless constructor; it is created the first
    /// time the page is shown. Classes in optional assemblies are discovered the same way.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class BentoPageAttribute : Attribute
    {
        public BentoPageAttribute(string id, string group, string name)
        {
            Id = id;
            Group = group;
            Name = name;
        }

        /// <summary>Stable identifier used by favorites and MCP. Do not change once released.</summary>
        public string Id { get; }
        public string Group { get; }
        public string Name { get; }

        /// <summary>Sort order inside the group; lower comes first.</summary>
        public int Order { get; set; }
    }

    public interface IBentoTool
    {
        void Draw();
    }

    public static class BentoGroups
    {
        public const string Organize = "整理";
        public const string Models = "资源、模型和预制体";
        public const string Materials = "材质";
        public const string Scene = "场景";
        public const string Settings = "AI与设置";

        internal static readonly string[] DisplayOrder = { Organize, Models, Materials, Scene, Settings };
    }
}
