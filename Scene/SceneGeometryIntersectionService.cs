using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bento
{
    public static class SceneGeometryIntersectionService
    {
        sealed class Candidate
        {
            public GameObject root;
            public Renderer[] renderers;
            public Collider[] colliders;
            public SceneTransformedBounds[] fittedBounds;
            public Bounds bounds;
            public bool selected;
        }

        const float MinimumCellSize = 0.5f;

        public static SceneGeometryScanResult Scan(SceneGeometryScanOptions options)
        {
            options = options ?? new SceneGeometryScanOptions();
            if (options.scope == SceneGeometryScanScope.BetweenLayers)
                return ScanBetweenLayers(options);

            var selectedRoots = new HashSet<GameObject>(BentoUtility.GetTopLevelSelectedSceneObjects());
            if (options.scope != SceneGeometryScanScope.LoadedScenes && selectedRoots.Count == 0)
                throw new InvalidOperationException("当前模式需要先在 Hierarchy 选择至少一个场景对象。");

            var candidates = CollectCandidates(options, selectedRoots);
            var result = new SceneGeometryScanResult
            {
                scannedAtUtc = DateTime.UtcNow.ToString("O"),
                scope = options.scope,
                candidateObjectCount = candidates.Count
            };

            foreach (var pair in BuildCandidatePairs(candidates, options))
            {
                result.testedPairCount++;
                if (!TryAnalyzePair(pair.Item1, pair.Item2, options, out var record)) continue;
                result.intersections.Add(record);
                if (result.intersections.Count < Mathf.Max(1, options.maximumResults)) continue;
                result.truncated = true;
                break;
            }

            SceneGeometryIntersectionSession.Replace(result);
            return result;
        }

        static SceneGeometryScanResult ScanBetweenLayers(SceneGeometryScanOptions options)
        {
            ValidateLayerPair(options.firstLayer, options.secondLayer);
            var allCandidates = CollectLoadedSceneCandidates(options.includeInactive);
            var firstCandidates = allCandidates.Where(candidate => candidate.root.layer == options.firstLayer).ToList();
            var secondCandidates = allCandidates.Where(candidate => candidate.root.layer == options.secondLayer).ToList();
            var result = new SceneGeometryScanResult
            {
                scannedAtUtc = DateTime.UtcNow.ToString("O"),
                scope = options.scope,
                candidateObjectCount = firstCandidates.Count + secondCandidates.Count,
                firstCandidateObjectCount = firstCandidates.Count,
                secondCandidateObjectCount = secondCandidates.Count,
                firstLayer = options.firstLayer,
                secondLayer = options.secondLayer
            };

            foreach (var pair in BuildCrossLayerPairs(firstCandidates, secondCandidates))
            {
                result.testedPairCount++;
                if (!TryAnalyzePair(pair.Item1, pair.Item2, options, out var record)) continue;
                result.intersections.Add(record);
                if (result.intersections.Count < Mathf.Max(1, options.maximumResults)) continue;
                result.truncated = true;
                break;
            }

            SceneGeometryIntersectionSession.Replace(result);
            return result;
        }

        static void ValidateLayerPair(int firstLayer, int secondLayer)
        {
            if (firstLayer < 0 || firstLayer >= 32 || secondLayer < 0 || secondLayer >= 32)
                throw new InvalidOperationException("Layer 必须在 Unity 的 0 到 31 范围内。");
            if (firstLayer == secondLayer)
                throw new InvalidOperationException("Layer A 和 Layer B 不能选择同一个 Layer。");
        }

        static List<Candidate> CollectLoadedSceneCandidates(bool includeInactive)
        {
            return LoadedSceneRoots()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(includeInactive))
                .Where(IsSupportedRenderer)
                .GroupBy(renderer => renderer.gameObject)
                .Where(group => group.Key != null)
                .Select(group => BuildCandidate(group.Key, group, includeInactive))
                .ToList();
        }

        static Candidate BuildCandidate(
            GameObject candidateRoot,
            IEnumerable<Renderer> renderers,
            bool includeInactive)
        {
            Renderer[] rendererArray = renderers.Distinct().ToArray();
            Bounds bounds = rendererArray[0].bounds;
            foreach (Renderer renderer in rendererArray.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return new Candidate
            {
                root = candidateRoot,
                renderers = rendererArray,
                colliders = candidateRoot.GetComponentsInChildren<Collider>(includeInactive)
                    .Where(collider => collider != null && collider.enabled && !collider.isTrigger)
                    .Distinct()
                    .ToArray(),
                fittedBounds = BuildFittedBounds(rendererArray),
                bounds = bounds
            };
        }

        static List<Candidate> CollectCandidates(SceneGeometryScanOptions options, HashSet<GameObject> selectedRoots)
        {
            IEnumerable<GameObject> roots = options.scope == SceneGeometryScanScope.SelectionOnly
                ? selectedRoots
                : LoadedSceneRoots();

            var candidates = new List<Candidate>();
            foreach (var root in roots)
            {
                if (root == null || (!options.includeInactive && !root.activeInHierarchy)) continue;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(options.includeInactive).Where(IsSupportedRenderer))
                {
                    var rendererRoot = renderer.gameObject;
                    var colliders = rendererRoot.GetComponents<Collider>()
                        .Where(collider => collider != null && collider.enabled && !collider.isTrigger).ToArray();
                    candidates.Add(new Candidate
                    {
                        root = rendererRoot,
                        renderers = new[] { renderer },
                        colliders = colliders,
                        fittedBounds = BuildFittedBounds(new[] { renderer }),
                        bounds = renderer.bounds,
                        selected = selectedRoots.Any(selected => rendererRoot == selected || rendererRoot.transform.IsChildOf(selected.transform))
                    });
                }
            }
            return candidates.GroupBy(candidate => candidate.root.GetInstanceID()).Select(group => group.First()).ToList();
        }

        static IEnumerable<GameObject> LoadedSceneRoots()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects()) yield return root;
            }
        }

        static bool IsSupportedRenderer(Renderer renderer) =>
            renderer != null && renderer.enabled &&
            (renderer is MeshRenderer || renderer is SkinnedMeshRenderer);

        static IEnumerable<Tuple<Candidate, Candidate>> BuildCandidatePairs(
            IReadOnlyList<Candidate> candidates,
            SceneGeometryScanOptions options)
        {
            float cellSize = EstimateCellSize(candidates);
            var buckets = new Dictionary<Vector3Int, List<int>>();
            var emitted = new HashSet<ulong>();

            for (int index = 0; index < candidates.Count; index++)
            {
                foreach (var cell in CoveredCells(candidates[index].bounds, cellSize))
                {
                    if (!buckets.TryGetValue(cell, out var indices))
                        buckets[cell] = indices = new List<int>();
                    indices.Add(index);
                }
            }

            foreach (var indices in buckets.Values)
            for (int a = 0; a < indices.Count; a++)
            for (int b = a + 1; b < indices.Count; b++)
            {
                int firstIndex = Mathf.Min(indices[a], indices[b]);
                int secondIndex = Mathf.Max(indices[a], indices[b]);
                ulong key = ((ulong)(uint)firstIndex << 32) | (uint)secondIndex;
                if (!emitted.Add(key)) continue;
                var first = candidates[firstIndex];
                var second = candidates[secondIndex];
                if (options.scope == SceneGeometryScanScope.SelectionAgainstScene && !first.selected && !second.selected) continue;
                if (options.ignoreSameHierarchy && IsSameHierarchy(first.root.transform, second.root.transform)) continue;
                if (!first.bounds.Intersects(second.bounds)) continue;
                yield return Tuple.Create(first, second);
            }
        }

        static IEnumerable<Tuple<Candidate, Candidate>> BuildCrossLayerPairs(
            IReadOnlyList<Candidate> firstCandidates,
            IReadOnlyList<Candidate> secondCandidates)
        {
            if (firstCandidates.Count == 0 || secondCandidates.Count == 0) yield break;

            float cellSize = EstimateCellSize(firstCandidates.Concat(secondCandidates).ToArray());
            var secondBuckets = new Dictionary<Vector3Int, List<int>>();
            for (int index = 0; index < secondCandidates.Count; index++)
            foreach (Vector3Int cell in CoveredCells(secondCandidates[index].bounds, cellSize))
            {
                if (!secondBuckets.TryGetValue(cell, out List<int> indices))
                    secondBuckets[cell] = indices = new List<int>();
                indices.Add(index);
            }

            var emitted = new HashSet<ulong>();
            for (int firstIndex = 0; firstIndex < firstCandidates.Count; firstIndex++)
            foreach (Vector3Int cell in CoveredCells(firstCandidates[firstIndex].bounds, cellSize))
            {
                if (!secondBuckets.TryGetValue(cell, out List<int> secondIndices)) continue;
                foreach (int secondIndex in secondIndices)
                {
                    ulong key = ((ulong)(uint)firstIndex << 32) | (uint)secondIndex;
                    if (!emitted.Add(key)) continue;
                    Candidate first = firstCandidates[firstIndex];
                    Candidate second = secondCandidates[secondIndex];
                    if (!first.bounds.Intersects(second.bounds)) continue;
                    yield return Tuple.Create(first, second);
                }
            }
        }

        static float EstimateCellSize(IReadOnlyList<Candidate> candidates)
        {
            if (candidates.Count == 0) return MinimumCellSize;
            var sizes = candidates.Select(candidate => Mathf.Max(candidate.bounds.size.x, candidate.bounds.size.z))
                .Where(size => size > 0f).OrderBy(size => size).ToArray();
            return Mathf.Max(MinimumCellSize, sizes[sizes.Length / 2] * 2f);
        }

        static IEnumerable<Vector3Int> CoveredCells(Bounds bounds, float cellSize)
        {
            Vector3Int min = FloorCell(bounds.min, cellSize);
            Vector3Int max = FloorCell(bounds.max, cellSize);
            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
                yield return new Vector3Int(x, y, z);
        }

        static Vector3Int FloorCell(Vector3 value, float cellSize) => new Vector3Int(
            Mathf.FloorToInt(value.x / cellSize),
            Mathf.FloorToInt(value.y / cellSize),
            Mathf.FloorToInt(value.z / cellSize));

        static bool TryAnalyzePair(
            Candidate first,
            Candidate second,
            SceneGeometryScanOptions options,
            out SceneGeometryIntersectionRecord record)
        {
            record = null;
            float overlapVolume = BoundsOverlapVolume(first.bounds, second.bounds, out float minimumAxisOverlap);
            if (minimumAxisOverlap < Mathf.Max(0f, options.minimumBoundsOverlap)) return false;

            float deepest = 0f;
            bool hasColliderPenetration = false;
            foreach (var firstCollider in first.colliders)
            foreach (var secondCollider in second.colliders)
            {
                if (!firstCollider.bounds.Intersects(secondCollider.bounds)) continue;
                if (!Physics.ComputePenetration(
                        firstCollider, firstCollider.transform.position, firstCollider.transform.rotation,
                        secondCollider, secondCollider.transform.position, secondCollider.transform.rotation,
                        out _, out float distance)) continue;
                hasColliderPenetration = true;
                deepest = Mathf.Max(deepest, distance);
            }

            if (hasColliderPenetration && deepest >= Mathf.Max(0f, options.minimumPenetration))
            {
                record = CreateRecord(first, second, SceneGeometryIntersectionKind.ColliderPenetration,
                    deepest, overlapVolume, "Collider 精确穿透");
                return true;
            }

            if (!options.includeBoundsOnlyCandidates || first.colliders.Length > 0 && second.colliders.Length > 0)
                return false;

            float fittedOverlap = 0f;
            bool fittedIntersects = false;
            foreach (SceneTransformedBounds firstBounds in first.fittedBounds)
            foreach (SceneTransformedBounds secondBounds in second.fittedBounds)
            {
                if (!SceneTransformedBoundsUtility.Intersects(
                        firstBounds,
                        secondBounds,
                        out float overlap))
                    continue;
                fittedIntersects = true;
                fittedOverlap = Mathf.Max(fittedOverlap, overlap);
            }
            if (!fittedIntersects || fittedOverlap < Mathf.Max(0f, options.minimumBoundsOverlap))
                return false;
            record = CreateRecord(first, second, SceneGeometryIntersectionKind.BoundsCandidate,
                fittedOverlap, overlapVolume, "缺少可用 Collider，Renderer 贴合包围盒疑似相交");
            return true;
        }

        static SceneTransformedBounds[] BuildFittedBounds(IEnumerable<Renderer> renderers)
        {
            var bounds = new List<SceneTransformedBounds>();
            foreach (Renderer renderer in renderers)
                if (SceneTransformedBoundsUtility.TryBuild(renderer, out SceneTransformedBounds fitted))
                    bounds.Add(fitted);
            return bounds.ToArray();
        }

        static SceneGeometryIntersectionRecord CreateRecord(
            Candidate first,
            Candidate second,
            SceneGeometryIntersectionKind kind,
            float depth,
            float overlapVolume,
            string note) => new SceneGeometryIntersectionRecord
        {
            firstInstanceId = first.root.GetInstanceID(),
            secondInstanceId = second.root.GetInstanceID(),
            firstPath = BentoUtility.GetHierarchyPath(first.root.transform),
            secondPath = BentoUtility.GetHierarchyPath(second.root.transform),
            firstScene = first.root.scene.path,
            secondScene = second.root.scene.path,
            kind = kind,
            depth = depth,
            boundsOverlapVolume = overlapVolume,
            note = note
        };

        static float BoundsOverlapVolume(Bounds first, Bounds second, out float minimumAxisOverlap)
        {
            float x = Mathf.Max(0f, Mathf.Min(first.max.x, second.max.x) - Mathf.Max(first.min.x, second.min.x));
            float y = Mathf.Max(0f, Mathf.Min(first.max.y, second.max.y) - Mathf.Max(first.min.y, second.min.y));
            float z = Mathf.Max(0f, Mathf.Min(first.max.z, second.max.z) - Mathf.Max(first.min.z, second.min.z));
            minimumAxisOverlap = Mathf.Min(x, Mathf.Min(y, z));
            return x * y * z;
        }

        static bool IsSameHierarchy(Transform first, Transform second) =>
            first == second || first.IsChildOf(second) || second.IsChildOf(first);

    }
}
