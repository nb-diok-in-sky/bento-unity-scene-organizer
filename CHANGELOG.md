# Changelog

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

## [1.0.0] - 2026-09-27

首个开源版本。

### Changed

- 仓库改为 UPM 包结构，可通过 git URL 安装。
- 源码按工具箱界面分组重新组织：`Editor/Framework`、`Editor/Common`、`Editor/Tools/<分组>/<功能>`。
- 主程序集更名为 `Bento.Editor`；可选模块更名为 `Bento.FbxExporter.Editor`、`Bento.Mcp.Editor`。
- MCP 模块改为通过 `versionDefines` 自动启用，不再修改工程的 Scripting Define Symbols。
- 页面改为用 `[BentoPage]` 特性自注册，首次显示时才创建；可选模块和第三方程序集用同样方式添加页面。
- 新增 `BentoWindow.OpenPage<TTool>()`，右键菜单和 MCP 按类型打开页面。
- “烘焙光照”从独立窗口改为工具箱“场景”分组下的页面，菜单入口保留。
- 定位、场景聚焦、全选/全不选/反选统一由 `BentoGUI` 提供。
- 递归抽取资产的“反选”不再勾选有冲突的项。
- 公开 API 收窄为扩展所需的类型，其余改为 internal。

### Added

- EditMode 测试（Unity Test Framework）：资产移动/重命名/回滚安全测试、页面注册测试。

### Fixed

- 安全自测的临时目录不再依赖 `Assets/Editor`，在任意项目中均可运行。

### Removed

- AI资产整理、几何相交审查、Blender 建筑内外墙、FBX 快速导入（由“模型导入”覆盖）。
- 贴图扫描、贴图改名、填充材质球、一键处理、材质修复、URP 材质恢复。
- `Tools/Bento > 刷新可选依赖模块` 菜单（可选模块现在由 Unity 自动检测）。
- `Tools/Bento > 运行资产流水线安全自测` 菜单（改为 Test Runner 中的测试）。

### 从旧版本迁移

- 旧版本是把 `Bento` 文件夹复制进 `Assets/`。改用包安装前，请先删除旧文件夹，否则 GUID 会冲突。
- 旧版本可能在 Player Settings 中留下 `BENTO_ENABLE_MCP_FOR_UNITY` 宏，不再需要，可以手动删除。
- 文件夹收藏、工具收藏、资源变体预设等 EditorPrefs 数据保持兼容。
