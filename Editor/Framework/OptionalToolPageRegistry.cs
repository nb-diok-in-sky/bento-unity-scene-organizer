using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Bento
{
    public static class OptionalToolPageRegistry
    {
        public static void AppendAvailable(ICollection<IBentoPage> pages)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] providerTypes;
                try
                {
                    providerTypes = assembly.GetTypes()
                        .Where(type => type.Namespace == "Bento.Optional"
                                       && type.Name.EndsWith("ToolPageProvider", StringComparison.Ordinal))
                        .ToArray();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    providerTypes = exception.Types
                        .Where(type => type != null
                                       && type.Namespace == "Bento.Optional"
                                       && type.Name.EndsWith("ToolPageProvider", StringComparison.Ordinal))
                        .ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (Type providerType in providerTypes)
                {
                    MethodInfo append = providerType.GetMethod(
                        "AppendPages",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(ICollection<IBentoPage>) },
                        null);
                    if (append == null)
                        continue;
                    try
                    {
                        append.Invoke(null, new object[] { pages });
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception.InnerException ?? exception);
                    }
                }
            }
        }

        public static void AddPage<T>(
            ICollection<IBentoPage> pages,
            string group,
            string id,
            string name,
            T owner,
            Action<T> draw)
            where T : class
        {
            if (pages.Any(page => string.Equals(page.Id, id, StringComparison.Ordinal)))
                return;
            pages.Add(new BentoPage(group, id, name, () => draw(owner), owner));
        }
    }
}
