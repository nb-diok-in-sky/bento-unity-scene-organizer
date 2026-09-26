using System;
using System.Collections.Generic;

namespace Bento
{
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
