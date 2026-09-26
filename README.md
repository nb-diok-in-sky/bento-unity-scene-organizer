# Bento

面向 Unity 美术资产整理与场景编辑的个人编辑器工具箱。

主要功能：

- 从选中 Prefab 收集材质，并在执行前逐项筛选和定位
- 选中 FBX 与贴图后生成 URP/Lit 材质、重映射材质槽并创建 Prefab
- Hierarchy 预制体替换与批量重命名
- 场景资源变体生成，复制并重新绑定材质和贴图
- Project 资产安全移动、重命名、GUID 验证和失败回滚
- 文件夹收藏、Hierarchy 一键折叠与只读引用审计
- 从选中模型收集模型、材质和贴图，并按三类独立命名后安全移动
- Terrain 当前绘制层贴图的独立选择入口，避免混入普通模型资产
- 资源变体自动识别 Model、Prefab、Material、Texture 输出目录，并复制重连实际引用资源
- LOD 批量关闭、物体显隐和材质使用对象定位
- 批量切换模型 Generate Lightmap UVs，并通过动态光池/静态光池安全启动烘焙
- 根据 Hierarchy 或 Project 当前选择生成对应的 Bento 智能右键菜单
- 可选 MCP 只读分析接口

## 在 Unity 项目中使用

将 `Bento` 文件夹复制到项目的 Editor 目录，例如：

```text
Assets/Editor/Bento
```

MCP for Unity 和 Unity FBX Exporter 均为可选依赖。未安装时对应页面和源码不会参与编译，
其余工具可继续正常使用。工具箱首次加载时会自动检测依赖并启用或隐藏对应页面；
安装或移除包后，也可执行 `Tools/Bento > 刷新可选依赖模块` 手动刷新。

本仓库不保存 `.mcp.json`、密钥或个人 AI 配置。

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
