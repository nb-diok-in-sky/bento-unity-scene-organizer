using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bento.Tests
{
    [TestFixture]
    public sealed class AssetPipelineSafetyTests
    {
        const string RootName = "_BentoTestTemp";
        const string Root = "Assets/" + RootName;
        const string Source = Root + "/Source";
        const string Target = Root + "/Target";

        [Test]
        public void MoveAndRestore()
        {
            var texture = new Texture2D(2, 2) { name = "SafetyTexture" };
            AssetDatabase.CreateAsset(texture, Source + "/SafetyTexture.asset");
            var material = new Material(Shader.Find("Unlit/Texture")) { name = "SafetyMaterial", mainTexture = texture };
            AssetDatabase.CreateAsset(material, Source + "/SafetyMaterial.mat");
            AssetDatabase.SaveAssets();

            string sourcePath = Source + "/SafetyTexture.asset";
            string targetPath = Target + "/SafetyTexture.asset";
            string guid = AssetDatabase.AssetPathToGUID(sourcePath);
            var entry = CreateEntry(texture, sourcePath, targetPath);
            var history = new AssetMoveHistory();

            SafeAssetMoveService.Execute(new List<AssetMoveEntry> { entry }, history);
            Require(AssetDatabase.AssetPathToGUID(targetPath) == guid, "移动后 GUID 改变");
            Require(AssetDatabase.LoadAssetAtPath<Material>(Source + "/SafetyMaterial.mat").mainTexture ==
                    AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath), "移动后材质贴图引用断裂");

            SafeAssetMoveService.Restore(history);
            Require(AssetDatabase.AssetPathToGUID(sourcePath) == guid, "恢复后 GUID 改变");
            Require(AssetDatabase.LoadAssetAtPath<Material>(Source + "/SafetyMaterial.mat").mainTexture ==
                    AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath), "恢复后材质贴图引用断裂");
        }

        [Test]
        public void DuplicateTargetRejected()
        {
            var first = CreateTexture("First");
            var second = CreateTexture("Second");
            bool rejected = false;
            try
            {
                SafeAssetMoveService.Execute(new List<AssetMoveEntry>
                {
                    CreateEntry(first, Source + "/First.asset", Target + "/Same.asset"),
                    CreateEntry(second, Source + "/Second.asset", Target + "/Same.asset")
                }, new AssetMoveHistory());
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Require(rejected, "重复目标路径未被拒绝");
        }

        [Test]
        public void DuplicateSourceRejected()
        {
            var texture = CreateTexture("DuplicateSource");
            string sourcePath = Source + "/DuplicateSource.asset";
            var entry = CreateEntry(texture, sourcePath, Target + "/DuplicateSourceA.asset");
            var duplicate = CreateEntry(texture, sourcePath, Target + "/DuplicateSourceB.asset");
            bool rejected = false;
            try
            {
                SafeAssetMoveService.Execute(new List<AssetMoveEntry> { entry, duplicate }, new AssetMoveHistory());
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Require(rejected, "重复源资产未被整批预验证拒绝");
        }

        [Test]
        public void PartialFailureRollsBack()
        {
            var first = CreateTexture("RollbackFirst");
            var second = CreateTexture("RollbackSecond");
            string firstPath = Source + "/RollbackFirst.asset";
            string secondPath = Source + "/RollbackSecond.asset";
            string firstGuid = AssetDatabase.AssetPathToGUID(firstPath);
            var secondEntry = CreateEntry(second, secondPath, Target + "/RollbackSecond.asset");
            var completed = new List<AssetMoveHistoryEntry>();
            var transaction = new AssetPathTransaction(new FailOnMoveNumber(2));
            bool failed = false;
            try
            {
                transaction.Execute(new[]
                {
                    ToOperation(CreateEntry(first, firstPath, Target + "/RollbackFirst.asset")),
                    ToOperation(secondEntry)
                }, completed, "测试回滚失败");
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }
            Require(failed, "中途失败测试没有触发异常");
            Require(AssetDatabase.AssetPathToGUID(firstPath) == firstGuid, "中途失败后第一个资源未回滚");
            Require(completed.Count == 0, "成功回滚后仍保留了误导性移动历史");
        }

        [Test]
        public void MoveThatReportsFailureAfterMovingIsRolledBack()
        {
            var texture = CreateTexture("MovedBeforeFailure");
            string sourcePath = Source + "/MovedBeforeFailure.asset";
            string targetPath = Target + "/MovedBeforeFailure.asset";
            var completed = new List<AssetMoveHistoryEntry>();
            Assert.Throws<InvalidOperationException>(() =>
                new AssetPathTransaction(new MoveThenFail()).Execute(
                    new[] { ToOperation(CreateEntry(texture, sourcePath, targetPath)) }, completed, "测试回滚失败"));
            Assert.IsFalse(string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(sourcePath)));
            Assert.IsTrue(string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(targetPath)));
            Assert.AreEqual(0, completed.Count);
        }

        [Test]
        public void BatchTextureMatchingUsesModelName()
        {
            string[] paths = { "Assets/Rock_BaseColor.png", "Assets/Tree_BaseColor.png" };
            Assert.AreEqual(paths[1], FbxUrpMaterialBindingService.FindTexturePath(paths, "Tree", "_BaseColor"));
            Assert.IsNull(FbxUrpMaterialBindingService.FindTexturePath(paths, "House", "_BaseColor"));
        }

        [Test]
        public void VariantCleanupDoesNotDeleteReplacementAtSamePath()
        {
            string path = Source + "/Variant.asset";
            AssetDatabase.CreateAsset(new Texture2D(2, 2), path);
            string originalGuid = AssetDatabase.AssetPathToGUID(path);
            var result = new ResourceVariantGenerationResult();
            result.createdAssets.Add(path);
            result.createdTextures.Add(path);
            result.createdAssetGuids[path] = originalGuid;
            Assert.IsTrue(AssetDatabase.DeleteAsset(path));
            string replacementPath = Target + "/Replacement.asset";
            AssetDatabase.CreateAsset(new Texture2D(2, 2), replacementPath);
            string replacementGuid = AssetDatabase.AssetPathToGUID(replacementPath);
            Assert.IsTrue(string.IsNullOrEmpty(AssetDatabase.MoveAsset(replacementPath, path)));
            Assert.AreNotEqual(originalGuid, replacementGuid);
            var cleanup = new ResourceVariantCleanupService().Delete(result);
            CollectionAssert.Contains(cleanup.FailedPaths, path);
            Assert.AreEqual(replacementGuid, AssetDatabase.AssetPathToGUID(path));
        }

        [Test]
        public void UnsafePlansRejected()
        {
            var first = CreateTexture("ChainFirst");
            var second = CreateTexture("ChainSecond");
            bool chainedRejected = false;
            try
            {
                SafeAssetMoveService.Execute(new List<AssetMoveEntry>
                {
                    CreateEntry(first, Source + "/ChainFirst.asset", Source + "/ChainSecond.asset"),
                    CreateEntry(second, Source + "/ChainSecond.asset", Target + "/ChainSecond.asset")
                }, new AssetMoveHistory());
            }
            catch (InvalidOperationException)
            {
                chainedRejected = true;
            }
            Require(chainedRejected, "顺序依赖的链式移动未被拒绝");
        }

        [Test]
        public void MovePlanRevalidatesBeforeExecution()
        {
            var texture = CreateTexture("StaleMoveSource");
            string sourcePath = Source + "/StaleMoveSource.asset";
            var entry = CreateEntry(texture, sourcePath, Target + "/StaleMoveTarget.asset");
            Require(string.IsNullOrEmpty(entry.warning), "初始移动计划不应包含安全警告");

            string movedPath = Source + "/StaleMoveSourceMoved.asset";
            Require(string.IsNullOrEmpty(AssetDatabase.MoveAsset(sourcePath, movedPath)), "无法准备移动计划过期测试");
            bool rejected = false;
            try
            {
                SafeAssetMoveService.Execute(new List<AssetMoveEntry> { entry }, new AssetMoveHistory());
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Require(rejected, "资源身份变化后，旧移动计划仍被执行");
        }

        [Test]
        public void RetargetingRevalidatesPlan()
        {
            var texture = CreateTexture("RetargetSource");
            var plan = SafeAssetMoveService.BuildPlan(
                new UnityEngine.Object[] { texture },
                Target,
                Target,
                Target,
                Target,
                new AssetNamingOptions());
            Require(string.IsNullOrEmpty(plan[0].warning), "初始移动计划不应包含安全警告");

            SafeAssetMoveService.RefreshTargets(
                plan,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                new AssetNamingOptions());
            Require(!string.IsNullOrEmpty(plan[0].warning), "修改目标后没有重新运行安全规则");
        }

        [Test]
        public void RenameAndRestore()
        {
            var texture = CreateTexture("RenameSource");
            string sourcePath = Source + "/RenameSource.asset";
            string guid = AssetDatabase.AssetPathToGUID(sourcePath);
            var material = new Material(Shader.Find("Unlit/Texture")) { name = "RenameMaterial", mainTexture = texture };
            AssetDatabase.CreateAsset(material, Source + "/RenameMaterial.mat");
            AssetDatabase.SaveAssets();

            var entry = SafeAssetRenameService.BuildEntry(sourcePath, "RenameTarget");
            var history = new AssetRenameHistory();
            SafeAssetRenameService.Execute(new List<AssetRenameEntry> { entry }, history);
            Require(AssetDatabase.AssetPathToGUID(entry.targetPath) == guid, "重命名后 GUID 改变");
            Require(AssetDatabase.LoadAssetAtPath<Material>(Source + "/RenameMaterial.mat").mainTexture ==
                    AssetDatabase.LoadAssetAtPath<Texture2D>(entry.targetPath), "重命名后材质贴图引用断裂");

            SafeAssetRenameService.Restore(history);
            Require(AssetDatabase.AssetPathToGUID(sourcePath) == guid, "恢复重命名后 GUID 改变");
        }

        [Test]
        public void RenameHistoryHandlerRestore()
        {
            var texture = CreateTexture("RenameHistorySource");
            string sourcePath = Source + "/RenameHistorySource.asset";
            string guid = AssetDatabase.AssetPathToGUID(sourcePath);
            var entry = SafeAssetRenameService.BuildEntry(sourcePath, "RenameHistoryTarget");
            var history = new AssetRenameHistory();

            SafeAssetRenameService.Execute(new List<AssetRenameEntry> { entry }, history);
            var record = AssetRenameOperationHandler.CreateRecord(history);
            Require(record.operationType == AssetRenameOperationHandler.Type, "重命名历史类型错误");
            Require(record.items.Count == 1 && record.items[0].assetGuid == guid, "重命名历史没有保留资产身份");

            new AssetRenameOperationHandler().Restore(record);
            Require(AssetDatabase.AssetPathToGUID(sourcePath) == guid, "统一操作历史未能恢复重命名资产");
        }

        [Test]
        public void RenamePlanRevalidatesBeforeExecution()
        {
            var source = CreateTexture("StaleRenameSource");
            string sourcePath = Source + "/StaleRenameSource.asset";
            var entry = SafeAssetRenameService.BuildEntry(sourcePath, "StaleRenameTarget");
            CreateTexture("StaleRenameTarget");

            bool occupiedTargetRejected = false;
            try { SafeAssetRenameService.Execute(new[] { entry }, new AssetRenameHistory()); }
            catch (InvalidOperationException) { occupiedTargetRejected = true; }
            Require(occupiedTargetRejected, "重命名执行前没有重新检查已被占用的目标路径");
            Require(AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath) == source, "stale 重命名计划错误移动了源资产");

            entry = SafeAssetRenameService.BuildEntry(sourcePath, "StaleRenameIdentityTarget");
            entry.sourceGuid = "invalid-guid";
            bool identityRejected = false;
            try { SafeAssetRenameService.Execute(new[] { entry }, new AssetRenameHistory()); }
            catch (InvalidOperationException) { identityRejected = true; }
            Require(identityRejected, "重命名执行前没有重新验证源资产身份");
            Require(AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath) == source, "身份失效的重命名计划错误移动了源资产");
        }

        [Test]
        public void UndoScopeCommitAndRollback()
        {
            var material = new Material(Shader.Find("Unlit/Color")) { name = "UndoScopeMaterial" };
            AssetDatabase.CreateAsset(material, Source + "/UndoScopeMaterial.mat");
            Color committed = Color.red;

            using (var undo = new BentoUndoScope("Bento 安全自测：提交"))
            {
                Undo.RecordObject(material, "Bento 安全自测：提交");
                material.color = committed;
                undo.Complete();
            }
            Require(material.color == committed, "Undo scope 成功分支被误回滚");

            using (new BentoUndoScope("Bento 安全自测：回滚"))
            {
                Undo.RecordObject(material, "Bento 安全自测：回滚");
                material.color = Color.blue;
            }
            Require(material.color == committed, "Undo scope 异常/未完成分支没有自动回滚");

        }

        [Test]
        public void HistoryRecordFailureIsReportedWithoutThrowing()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("资产操作已成功，但持久历史保存失败"));
            var service = new AssetOperationHistoryService(
                new FailingHistoryRepository(),
                Array.Empty<IRecoverableAssetOperation>());
            bool saved = service.TryRecord(new AssetOperationRecord { id = "test" }, out string warning);
            Require(!saved, "历史写入失败被误报为成功");
            Require(!string.IsNullOrEmpty(warning), "历史写入失败没有向用户返回警告");
        }

        [Test]
        public void PrefabLayerRestoreRejectsChangedHierarchy()
        {
            string prefabPath = Source + "/LayerHistory.prefab";
            var source = new GameObject("LayerRoot");
            var child = new GameObject("LayerChild");
            child.transform.SetParent(source.transform, false);
            try { PrefabUtility.SaveAsPrefabAsset(source, prefabPath); }
            finally { UnityEngine.Object.DestroyImmediate(source); }

            string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            var history = new PrefabLayerHistory
            {
                entries = new List<PrefabLayerHistoryEntry>
                {
                    new PrefabLayerHistoryEntry
                    {
                        assetGuid = prefabGuid,
                        assetPath = prefabPath,
                        changes = new List<PrefabLayerChangeRecord>
                        {
                            new PrefabLayerChangeRecord
                            {
                                hierarchyPath = "LayerRoot/MissingChild",
                                siblingIndexPath = "99",
                                oldLayer = 0,
                                newLayer = 1
                            }
                        }
                    }
                }
            };

            bool rejected = false;
            try { PrefabLayerService.Restore(history); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Prefab Layer 恢复在节点结构变化后未阻止执行");
        }

        [Test]
        public void ResourceVariantRejectsStaleSettings()
        {
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            source.name = "StaleVariantSource";
            try
            {
                var session = new ResourceVariantSession();
                session.Settings.prefabFolder = Target;
                session.Settings.materialFolder = Target;
                session.Settings.textureFolder = Target;
                session.Settings.replaceSceneObjects = false;
                session.Analyze(new[] { source });
                session.Settings.copyTextures = !session.Settings.copyTextures;

                bool rejected = false;
                try { session.Generate(); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "资源变体设置变化后仍执行了旧计划");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ResourceVariantMaterialIsolation()
        {
            string prefabFolder = Target + "/Prefabs";
            string materialFolder = Target + "/Materials";
            string textureFolder = Target + "/Textures";
            AssetDatabase.CreateFolder(Target, "Prefabs");
            AssetDatabase.CreateFolder(Target, "Materials");
            AssetDatabase.CreateFolder(Target, "Textures");

            var sourceMaterial = new Material(Shader.Find("Unlit/Color")) { name = "VariantSourceMaterial" };
            AssetDatabase.CreateAsset(sourceMaterial, Source + "/VariantSourceMaterial.mat");
            var sourceObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sourceObject.name = "VariantSourceObject";
            sourceObject.GetComponent<Renderer>().sharedMaterial = sourceMaterial;

            try
            {
                var settings = new ResourceVariantPreset
                {
                    copyMaterials = false,
                    copyTextures = false,
                    replaceSceneObjects = false,
                    prefabFolder = prefabFolder,
                    materialFolder = materialFolder,
                    textureFolder = textureFolder
                };
                var plan = new ResourceVariantPlanner(settings).Build(new List<GameObject> { sourceObject });
                var result = new ResourceVariantGenerator(settings).Generate(plan);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.createdPrefabs[0]);
                var generatedMaterial = prefab.GetComponentInChildren<Renderer>(true).sharedMaterial;

                Require(result.createdMaterials.Count == 1, "资源变体没有创建独立材质");
                Require(generatedMaterial != sourceMaterial, "资源变体仍引用原材质");
                Require(AssetDatabase.GetAssetPath(generatedMaterial) == result.createdMaterials[0], "Prefab 未引用生成的材质资产");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceObject);
            }
        }

        [Test]
        public void ResourceVariantFailureIsRecoverable()
        {
            var sourceMaterial = new Material(Shader.Find("Unlit/Color")) { name = "FailureSourceMaterial" };
            AssetDatabase.CreateAsset(sourceMaterial, Source + "/FailureSourceMaterial.mat");
            var sourceObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sourceObject.name = "FailureSourceObject";
            sourceObject.GetComponent<Renderer>().sharedMaterial = sourceMaterial;
            try
            {
                var session = new ResourceVariantSession();
                session.Settings.prefabFolder = Target + "/MissingPrefabFolder";
                session.Settings.materialFolder = Target;
                session.Settings.textureFolder = Target;
                session.Settings.copyTextures = false;
                session.Settings.replaceSceneObjects = false;
                session.Analyze(new List<GameObject> { sourceObject });
                bool failed = false;
                try { session.Generate(); }
                catch (ResourceVariantGenerationException) { failed = true; }
                Require(failed, "资源变体失败样本未触发");
                Require(session.LastResult != null, "资源变体失败后未保留部分结果");
                Require(session.LastResult.createdAssets.Count > 0, "资源变体失败样本没有产生可清理资源");
                session.DeleteGeneratedAssets();
                Require(session.LastResult == null, "资源变体失败产物未清理");
            }
            finally { UnityEngine.Object.DestroyImmediate(sourceObject); }
        }

        static Texture2D CreateTexture(string name)
        {
            var texture = new Texture2D(2, 2) { name = name };
            AssetDatabase.CreateAsset(texture, Source + "/" + name + ".asset");
            return texture;
        }

        static AssetMoveEntry CreateEntry(Texture2D texture, string sourcePath, string targetPath)
        {
            return new AssetMoveEntry
            {
                asset = texture,
                sourcePath = sourcePath,
                sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath),
                targetFolder = Target,
                targetPath = targetPath,
                kind = AssetMoveKind.Texture,
                include = true
            };
        }

        static AssetMoveHistoryEntry ToOperation(AssetMoveEntry entry) => new AssetMoveHistoryEntry
        {
            guid = entry.sourceGuid,
            oldPath = entry.sourcePath,
            newPath = entry.targetPath
        };

        sealed class FailOnMoveNumber : IAssetPathMover
        {
            readonly int _failureNumber;
            int _count;

            public FailOnMoveNumber(int failureNumber) => _failureNumber = failureNumber;

            public string Move(string sourcePath, string targetPath)
            {
                _count++;
                return _count == _failureNumber ? "intentional test failure" : AssetDatabase.MoveAsset(sourcePath, targetPath);
            }
        }

        sealed class MoveThenFail : IAssetPathMover
        {
            bool _failed;

            public string Move(string sourcePath, string targetPath)
            {
                string error = AssetDatabase.MoveAsset(sourcePath, targetPath);
                if (!string.IsNullOrEmpty(error)) return error;
                if (_failed) return string.Empty;
                _failed = true;
                return "intentional failure after move";
            }
        }

        sealed class FailingHistoryRepository : IAssetOperationHistoryRepository
        {
            public void Save(AssetOperationRecord record) => throw new InvalidOperationException("intentional test failure");
            public IReadOnlyList<AssetOperationRecord> LoadAll() => Array.Empty<AssetOperationRecord>();
            public AssetOperationRecord Load(string id) => null;
        }

        [SetUp]
        public void SetUp()
        {
            DeleteRoot();
            AssetDatabase.CreateFolder("Assets", RootName);
            AssetDatabase.CreateFolder(Root, "Source");
            AssetDatabase.CreateFolder(Root, "Target");
        }

        [TearDown]
        public void TearDown() => DeleteRoot();

        static void DeleteRoot()
        {
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            AssetDatabase.Refresh();
        }

        static void Require(bool condition, string message)
        {
            Assert.IsTrue(condition, message);
        }
    }
}
