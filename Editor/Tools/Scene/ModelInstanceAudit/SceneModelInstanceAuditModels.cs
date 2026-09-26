using System;
using System.Collections.Generic;

namespace Bento
{
    public enum SceneModelInstanceAuditScope
    {
        Selection,
        LoadedScenes
    }

    [Serializable]
    public sealed class SceneModelInstanceAuditRecord
    {
        public int instanceId;
        public string objectName;
        public string hierarchyPath;
        public string scenePath;
        public string modelAssetPath;
        public string modelExtension;
    }

    [Serializable]
    public sealed class SceneModelInstanceAuditResult
    {
        public string scannedAtUtc;
        public SceneModelInstanceAuditScope scope;
        public int inspectedObjectCount;
        public List<SceneModelInstanceAuditRecord> instances = new List<SceneModelInstanceAuditRecord>();
    }
}
