using System.Collections.Generic;
using System.Linq;

namespace Bento
{
    static class HierarchyOrganizationSession
    {
        static readonly List<HierarchyOrganizationEntry> PendingEntries = new List<HierarchyOrganizationEntry>();

        public static void SetDraft(IEnumerable<HierarchyOrganizationEntry> entries)
        {
            PendingEntries.Clear();
            if (entries != null) PendingEntries.AddRange(entries.Select(Clone));
        }

        public static List<HierarchyOrganizationEntry> TakeDraft()
        {
            var result = PendingEntries.Select(Clone).ToList();
            PendingEntries.Clear();
            return result;
        }

        static HierarchyOrganizationEntry Clone(HierarchyOrganizationEntry entry) => new HierarchyOrganizationEntry
        {
            objectInstanceId = entry.objectInstanceId,
            objectPath = entry.objectPath,
            parentInstanceId = entry.parentInstanceId,
            targetParentPath = entry.targetParentPath,
            include = entry.include,
            warning = entry.warning
        };
    }
}
