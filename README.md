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
