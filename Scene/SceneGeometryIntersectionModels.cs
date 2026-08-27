using System;
using System.Collections.Generic;

namespace Bento
{
    public enum SceneGeometryScanScope
    {
        BetweenLayers,
        SelectionOnly,
        SelectionAgainstScene,
        LoadedScenes
    }

    public enum SceneGeometryIntersectionKind
    {
        ColliderPenetration,
        BoundsCandidate
    }

    [Serializable]
    public sealed class SceneGeometryScanOptions
    {
        public SceneGeometryScanScope scope = SceneGeometryScanScope.SelectionAgainstScene;
        public int firstLayer = 0;
        public int secondLayer = 1;
        public bool includeInactive;
        public bool ignoreSameHierarchy = true;
        public bool includeBoundsOnlyCandidates = true;
        public float minimumBoundsOverlap = 0.01f;
        public float minimumPenetration = 0.001f;
        public int maximumResults = 500;
    }

    [Serializable]
    public sealed class SceneGeometryIntersectionRecord
    {
        public bool include = true;
        public int firstInstanceId;
        public int secondInstanceId;
        public string firstPath;
        public string secondPath;
        public string firstScene;
        public string secondScene;
        public SceneGeometryIntersectionKind kind;
        public float depth;
        public float boundsOverlapVolume;
        public string note;
    }

    [Serializable]
    public sealed class SceneGeometryScanResult
    {
        public string scannedAtUtc;
        public SceneGeometryScanScope scope;
        public int candidateObjectCount;
        public int firstCandidateObjectCount;
        public int secondCandidateObjectCount;
        public int firstLayer;
        public int secondLayer;
        public int testedPairCount;
        public bool truncated;
        public List<SceneGeometryIntersectionRecord> intersections = new List<SceneGeometryIntersectionRecord>();
    }
}
