using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace Bento
{
    static class McpCommandRegistry
    {
        public static IReadOnlyDictionary<string, Func<JObject, object>> Create()
        {
            var commands = new Dictionary<string, Func<JObject, object>>(StringComparer.OrdinalIgnoreCase)
            {
                ["ping"] = _ => new SuccessResponse("pong", new { tool = "bento" })
            };
            McpAssetCommands.Register(commands);
            McpSceneCommands.Register(commands);
            return commands;
        }
    }
}
