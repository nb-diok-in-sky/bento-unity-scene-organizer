# Bento

面向 Unity 美术资产整理与场景编辑的个人编辑器工具箱。

主要功能：

- 材质、贴图扫描与批处理
- Missing/Error/Built-in 材质的序列化属性恢复与 URP/Lit 迁移
- 从选中 Prefab 收集材质，并在执行前逐项筛选和定位
- 选中 FBX 与贴图后生成 URP/Lit 材质、重映射材质槽并创建 Prefab
- Hierarchy 预制体替换与批量重命名
- 场景资源变体生成，复制并重新绑定材质和贴图
- Project 资产安全移动、重命名、GUID 验证和失败回滚
- 文件夹收藏与只读引用审计
- 从选中模型收集模型、材质和贴图，并按三类独立命名后安全移动
- Terrain 当前绘制层贴图的独立选择入口，避免混入普通模型资产
- 根据 Hierarchy 或 Project 当前选择生成对应的 Bento 智能右键菜单
- 可选 MCP 只读分析接口

## 在 Unity 项目中使用

将 `Bento` 文件夹复制到项目的 Editor 目录，例如：

```text
Assets/Editor/Bento
```

MCP 相关脚本依赖 MCP for Unity。没有安装该插件的项目需要移除 MCP 相关脚本，或后续使用程序集宏隔离。

本仓库不保存 `.mcp.json`、密钥或个人 AI 配置。

## URP 材质恢复

也可以直接载入 Project 中选中的材质，或收集选中 Prefab 的 Renderer 引用材质。

执行前的列表支持全选、全不选、反选、只选疑似损坏 Shader 和逐项定位。迁移目标固定为
`Universal Render Pipeline/Lit`。工具从材质序列化数据读取旧贴图、颜色和数值，因此原 Shader
丢失或进入 `Hidden/InternalErrorShader` 时仍能尝试恢复常见属性。执行会写入 Undo，并在项目根目录
的 `Bento_材质迁移备份` 中保存迁移清单。

该流程只迁移常见 PBR 属性，不会复刻原自定义 Shader 的风、顶点动画、混合层、距离淡出或其他专属效果。

## FBX 快速导入

在 `资源 > FBX 快速导入` 中锁定一个 Project FBX，再选择一组贴图。工具会依据现有命名规则推断
BaseColor、Normal、Metallic、AO、Height、Emission 等语义，列表中的类型可以手动修正。

当前版本创建一个 URP/Lit 材质，并通过 `ModelImporter` 将它映射到 FBX 的全部材质槽。多材质槽的
逐槽匹配尚未实现。`直接生成 Prefab` 会在内存中实例化 FBX 后保存 Prefab，不会修改当前场景。

贴图列表支持 `PackedMask`。默认 MSA 预设按 `R=Metallic、G=Smoothness、B=AO` 读取，并生成
URP/Lit 使用的 `MetallicSmoothness` 与 `Occlusion` 两张贴图。窗口可以修改每项数据的来源通道；
如果源贴图存储 Roughness，可以启用 Smoothness 反相。工具也会把 Normal 设置为 Normal Map，并将
PackedMask 和生成的数据贴图设为 Linear。

同一页面的“批量 FBX 转 Prefab”支持递归扫描来源文件夹，并将 Prefab 平铺到统一目录，或按来源
子目录保留结构。执行前可以预览、定位和取消单项。已有同名 Prefab 时支持跳过、生成唯一名称或
覆盖；默认在输出根目录下创建一个与来源文件夹同名的文件夹，并使用保留目录结构和跳过策略。
该批量入口只创建 Prefab，不自动修改材质。

## 可选依赖：Unity FBX Exporter

`OBJ 转项目 FBX` 可以使用 Unity 官方 FBX Exporter。官方包与 Bento 适配层保持隔离，
不会提交到本仓库，也不会把 Unity/Autodesk 源码混入 `Bento`。

推荐固定版本：

- `com.unity.formats.fbx`：`5.1.6`
- `com.autodesk.fbx`：`5.1.3`

官方信息：

- [Unity FBX Exporter 仓库](https://github.com/Unity-Technologies/com.unity.formats.fbx)
- [Unity FBX Exporter 文档](https://docs.unity3d.com/Packages/com.unity.formats.fbx@5.1/manual/index.html)
- [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license)

本地安装时，将两个官方包分别放到 Unity 项目的下列目录：

```text
LocalPackages/com.unity.formats.fbx
LocalPackages/com.autodesk.fbx
```

然后在项目 `Packages/manifest.json` 中加入：

```json
"com.autodesk.fbx": "file:../LocalPackages/com.autodesk.fbx",
"com.unity.formats.fbx": "file:../LocalPackages/com.unity.formats.fbx"
```

Bento 仓库通过 `.gitignore` 排除这两个目录。其他使用者需要自行从 Unity Package
Manager 安装官方包，或按上述目录结构放入固定版本的本地包。
