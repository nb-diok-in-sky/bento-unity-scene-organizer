using System.Collections.Generic;

namespace Bento.Optional
{
    public static class McpToolPageProvider
    {
        public static void AppendPages(ICollection<IBentoPage> pages)
        {
            OptionalToolPageRegistry.AddPage(
                pages,
                "AI与设置",
                "ai_mcp",
                "AI/MCP",
                new McpIntegrationTool(),
                tool => tool.Draw());
        }
    }
}
