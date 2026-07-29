# Bento目录说明

- `Core`：工具箱窗口、页面生命周期、通用编辑器 UI、选择与命名基础设施。
- `Scene`：Hierarchy、Prefab、场景材质、光源与 Renderer 批处理。
- `Variants`：资源变体的计划、设置、生成、清理与预设。
- `Integration`：MCP 接入与只读分析门面。
- `Tests`：资产流水线安全自测。

`Bento.Editor.asmdef` 保留在根目录，使所有子目录仍属于同一 Editor 程序集。目录分类仅改善导航，不改变命名空间、接口或运行行为。
