using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace Bento
{
    [McpForUnityTool(
        "bento",
        Description = "Bento Unity production tools: read scene and asset state, prepare reviewed plans, and open toolbox pages.",
        AutoRegister = true,
        Group = "core")]
    public static class BentoMcpTool
    {
        static readonly IReadOnlyDictionary<string, Func<JObject, object>> Commands =
            McpCommandRegistry.Create();

        public static object HandleCommand(JObject parameters)
        {
            string action = parameters?["action"]?.ToString()?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("action is required");

            try
            {
                return Commands.TryGetValue(action, out Func<JObject, object> command)
                    ? command(parameters)
                    : new ErrorResponse(
                        $"Unknown action: {action}. Supported: {string.Join(", ", Commands.Keys.OrderBy(value => value))}");
            }
            catch (Exception exception)
            {
                return new ErrorResponse(exception.Message, new { stackTrace = exception.StackTrace });
            }
        }
    }
}
