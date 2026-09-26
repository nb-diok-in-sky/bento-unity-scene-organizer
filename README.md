# Bento

面向 Unity 美术资产整理与场景编辑的编辑器工具箱。所有会修改资产或场景的操作都遵循同一个流程：**先生成预览，确认后再执行**。场景修改支持 Ctrl+Z；资产移动和重命名写入操作历史，失败时自动回滚。

- Unity 2022.3 或更新版本
- 仅在编辑器中运行，不会进入构建产物
- 材质相关功能以 URP/Lit 为目标 Shader
- 协议：[MIT](LICENSE)

## 安装

在 Unity 中打开 `Window > Package Manager`，点左上角 `+`，选择 **Add package from git URL**，填入：

```text
https://github.com/nb-diok-in-sky/Bento.git
```

需要固定版本时，可以在 URL 末尾加上 tag，例如 `...Bento.git#v1.0.0`。

也可以直接编辑项目的 `Packages/manifest.json`：

```json
"com.nbdiok.bento": "https://github.com/nb-diok-in-sky/Bento.git"
```

安装完成后，从菜单 `Tools/Bento > Bento` 打开。

> 如果以前是把 `Bento` 文件夹复制进 `Assets/` 使用的，请先删除那个文件夹再安装包，否则会出现 GUID 冲突。

## 功能

| 分组 | 功能 |
|---|---|
| 整理 | 文件夹收藏、批量重命名（场景对象 / Project 资产）、资产安全移动、操作历史与回滚、资产信息导出、按后缀清理、递归抽取资产、Hierarchy 整理、场景光源批处理、场景物体阴影 |
| 资源、模型和预制体 | 模型导入（批量生成 URP/Lit 材质、绑定贴图、生成 Prefab）、资源变体、FBX 材质继承、Prefab Layer、Tag/Layer |
| 材质 | Shader 替换、选中模型的材质和贴图、选中 Terrain 绘制贴图、Terrain Layer 生成 |
| 场景 | 场景材质替换、查找材质使用对象、场景材质吸管、预制体替换、FBX 实例检查、可视范围审查 |
| 菜单 | 智能右键菜单（`Bento/智能工具`，按当前选择给出可用功能）、光照 UV 批处理与烘焙、资产引用复用审计 |

每个功能的详细说明见 [使用说明](Documentation~/使用说明.md)。

## 可选依赖

下面两个模块只在安装了对应的包时才会编译，没装也不影响其他功能。Unity 会自动检测，不需要手动设置。

| 模块 | 需要的包 | 作用 |
|---|---|---|
| OBJ 转项目 FBX | `com.unity.formats.fbx`（Unity FBX Exporter） | 把 OBJ 导入结果重新导出为项目 FBX |
| AI / MCP | `com.coplaydev.unity-mcp`（MCP for Unity） | 通过 `bento` 命令向 AI 客户端提供只读查询和预览能力，最终写入仍需在 Unity 中确认 |

本仓库不保存 `.mcp.json`、密钥或任何个人 AI 配置。

## 目录结构

```text
Editor/
  Framework/        工具箱窗口、页面注册、收藏、右键菜单、公共 GUI
  Common/           命名、选择、资产信息、安全资产操作与操作历史、材质规则
  Tools/<分组>/<功能>/  每个功能一个文件夹，分组与工具箱界面一致
Optional/
  FbxExporter/      可选模块：OBJ 转 FBX
  Mcp/              可选模块：MCP 集成
Tests/Editor/       编辑器测试（Unity Test Framework）
Documentation~/     使用说明
```

## 贡献

欢迎提交 Issue 和 Pull Request。提交前请确认：

- 在 Unity 2022.3 中编译无错误。
- 在 `Window > General > Test Runner` 的 EditMode 中运行测试并通过。包内测试默认不显示，需要在项目的 `Packages/manifest.json` 中加入：

  ```json
  "testables": ["com.nbdiok.bento"]
  ```

  测试会在 `Assets/_BentoTestTemp` 下创建临时资产，结束后自动删除。
- 新增会修改资产或场景的功能时，保持“先预览、后确认”的流程，并提供撤销或回滚。
