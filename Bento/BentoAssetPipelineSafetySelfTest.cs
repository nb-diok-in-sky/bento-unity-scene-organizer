using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public static class BentoAssetPipelineSafetySelfTest
    {
        const string Root = "Assets/Editor/_BentoSafetyTemp";
        const string Source = Root + "/Source";
        const string Target = Root + "/Target";

        [MenuItem("Tools/Bento/运行资产流水线安全自测")]
        public static void Run()
        {
            try
            {
                Prepare();
                VerifyMoveAndRestore();
                VerifyDuplicateTargetRejected();
                VerifyPartialFailureRollsBack();
                VerifyUnsafePlansRejected();
                VerifyRenameAndRestore();
                VerifyResourceVariantMaterialIsolation();
                Debug.Log("[Bento Safety] PASS：移动/重命名 GUID 与引用保留、资源变体材质隔离、反向恢复、中途失败回滚、危险计划拒绝均通过。");
                EditorUtility.DisplayDialog("Bento 安全自测", "全部测试通过。", "确定");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Bento 安全自测失败", exception.Message, "确定");
                throw;
            }
            finally
            {
                DeleteRoot();
            }
        }

        static void VerifyMoveAndRestore()
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

        static void VerifyDuplicateTargetRejected()
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

        static void VerifyPartialFailureRollsBack()
        {
            var first = CreateTexture("RollbackFirst");
            var second = CreateTexture("RollbackSecond");
            string firstPath = Source + "/RollbackFirst.asset";
            string secondPath = Source + "/RollbackSecond.asset";
            string firstGuid = AssetDatabase.AssetPathToGUID(firstPath);
            var invalidSecond = CreateEntry(second, secondPath, Target + "/RollbackSecond.asset");
            invalidSecond.sourceGuid = "invalid-guid-to-force-post-first-move-failure";
            var history = new AssetMoveHistory();
            bool failed = false;
            try
            {
                SafeAssetMoveService.Execute(new List<AssetMoveEntry>
                {
                    CreateEntry(first, firstPath, Target + "/RollbackFirst.asset"),
                    invalidSecond
                }, history);
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }
            Require(failed, "中途失败测试没有触发异常");
            Require(AssetDatabase.AssetPathToGUID(firstPath) == firstGuid, "中途失败后第一个资源未回滚");
            Require(history.moves.Count == 0, "成功回滚后仍保留了误导性移动历史");
        }

        static void VerifyUnsafePlansRejected()
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

        static void VerifyRenameAndRestore()
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

        static void VerifyResourceVariantMaterialIsolation()
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

        static void Prepare()
        {
            DeleteRoot();
            AssetDatabase.CreateFolder("Assets/Editor", "_BentoSafetyTemp");
            AssetDatabase.CreateFolder(Root, "Source");
            AssetDatabase.CreateFolder(Root, "Target");
        }

        static void DeleteRoot()
        {
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            AssetDatabase.Refresh();
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
