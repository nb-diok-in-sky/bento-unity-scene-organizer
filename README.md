# Bento

面向 Unity 美术资产整理与场景编辑的个人编辑器工具箱。

主要功能：

- 材质、贴图扫描与批处理
- Hierarchy 预制体替换与批量重命名
- 场景资源变体生成，复制并重新绑定材质和贴图
- Project 资产安全移动、重命名、GUID 验证和失败回滚
- 文件夹收藏与只读引用审计
- 可选 MCP 只读分析接口

## 在 Unity 项目中使用

将 `Bento` 文件夹复制到项目的 Editor 目录，例如：

```text
Assets/Editor/Bento
```

MCP 相关脚本依赖 MCP for Unity。没有安装该插件的项目需要移除 MCP 相关脚本，或后续使用程序集宏隔离。

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
