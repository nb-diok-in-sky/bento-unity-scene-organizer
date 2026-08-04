using System.Collections.Generic;

namespace Bento
{
    public enum HierarchyCompensationMode
    {
        Individual,
        Shared
    }

    public sealed class HierarchyCompensationPreview
    {
        public HierarchyCompensationMode mode;
        public bool canExecute;
        public int compensationNodeCount;
        public string reason;
        public readonly List<HierarchyOrganizationEntry> entries = new List<HierarchyOrganizationEntry>();
    }
}
