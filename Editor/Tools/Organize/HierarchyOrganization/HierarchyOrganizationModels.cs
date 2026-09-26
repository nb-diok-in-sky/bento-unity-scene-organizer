using System;
using System.Collections.Generic;

namespace Bento
{
    [Serializable]
    sealed class HierarchyOrganizationPlan
    {
        public string scenePath;
        public List<HierarchyOrganizationEntry> entries = new List<HierarchyOrganizationEntry>();
    }

    [Serializable]
    sealed class HierarchyOrganizationEntry
    {
        public int objectInstanceId;
        public string objectPath;
        public int parentInstanceId;
        public string targetParentPath;
        public bool include = true;
        public string warning;
    }
}
