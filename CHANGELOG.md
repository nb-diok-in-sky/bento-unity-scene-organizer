# Changelog

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

## [1.0.0] - 2026-09-27

首个开源版本。

### Changed

- 仓库改为 UPM 包结构，可通过 git URL 安装。
- 源码按工具箱界面分组重新组织：`Editor/Framework`、`Editor/Common`、`Editor/Tools/<分组>/<功能>`。
- 主程序集更名为 `Bento.Editor`；可选模块更名为 `Bento.FbxExporter.Editor`、`Bento.Mcp.Editor`。
- MCP 模块改为通过 `versionDefines` 自动启用，不再修改工程的 Scripting Define Symbols。

### Removed

- AI资产整理、几何相交审查、Blender 建筑内外墙、FBX 快速导入（由“模型导入”覆盖）。
- 贴图扫描、贴图改名、填充材质球、一键处理、材质修复、URP 材质恢复。
- `Tools/Bento > 刷新可选依赖模块` 菜单（可选模块现在由 Unity 自动检测）。

### 从旧版本迁移

- 旧版本是把 `Bento` 文件夹复制进 `Assets/`。改用包安装前，请先删除旧文件夹，否则 GUID 会冲突。
- 旧版本可能在 Player Settings 中留下 `BENTO_ENABLE_MCP_FOR_UNITY` 宏，不再需要，可以手动删除。
- 文件夹收藏、工具收藏、资源变体预设等 EditorPrefs 数据保持兼容。
