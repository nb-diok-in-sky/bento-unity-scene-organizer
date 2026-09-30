# Changelog

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

## [1.0.0] - 2026-09-27

首个公开版本。

### Added

- 工具箱主窗口：分组导航、收藏常用功能、文件夹收藏栏、窄窗口自适应。
- 整理：文件夹收藏、批量重命名、资产移动、操作历史、资产信息、按后缀清理、递归抽取资产、Hierarchy 整理、场景光源批处理、场景物体阴影。
- 资源、模型和预制体：模型导入、资源变体、FBX 材质继承、Prefab Layer、Tag/Layer。
- 材质：Shader 替换、选中模型材质贴图、选中 Terrain 绘制贴图、Terrain Layer 生成。
- 场景：场景材质替换、查找材质使用对象、场景材质吸管、预制体替换、FBX 实例检查、烘焙光照。
- 智能右键菜单，以及资产引用复用审计等菜单工具。
- 可选模块：OBJ 转项目 FBX（Unity FBX Exporter）、AI/MCP 集成（MCP for Unity），按是否安装对应包自动启用。
- 扩展接口：`[BentoPage]` + `IBentoTool` 注册自定义页面。
- EditMode 测试：资产移动 / 重命名 / 回滚安全测试、页面注册测试。
