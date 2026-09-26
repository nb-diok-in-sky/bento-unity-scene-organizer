using UnityEditor;
using UnityEngine;

namespace Bento
{
    [BentoPage("scene_material_picker", BentoGroups.Scene, "场景材质吸管", Order = 30)]
    sealed class SceneMaterialPickerTool : IBentoPageLifecycle, IBentoTool
    {
        readonly SceneMaterialPickerController _controller = new SceneMaterialPickerController();

        public void Draw()
        {
            EditorGUILayout.LabelField("场景材质吸管", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "点击“开始吸取”后，在 Scene 视图点击模型表面。工具会在 Project 中选中命中的材质；无法精确判断子网格时会显示材质槽供选择。",
                MessageType.Info);

            if (GUILayout.Button(_controller.IsActive ? "停止吸取" : "开始吸取材质", GUILayout.Height(38)))
            {
                if (_controller.IsActive) _controller.Stop();
                else _controller.Start();
            }

            EditorGUILayout.HelpBox(
                _controller.Status,
                _controller.IsActive ? MessageType.Warning : MessageType.None);
        }

        public void OnActivated() { }
        public void OnDeactivated() => _controller.Stop();
        public void OnSelectionChanged() { }
    }
}
