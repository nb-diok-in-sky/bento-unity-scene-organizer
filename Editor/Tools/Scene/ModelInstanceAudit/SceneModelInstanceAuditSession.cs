namespace Bento
{
    static class SceneModelInstanceAuditSession
    {
        static SceneModelInstanceAuditResult _current;

        public static SceneModelInstanceAuditResult Current => _current;
        public static void Replace(SceneModelInstanceAuditResult result) => _current = result;
        public static void Clear() => _current = null;
    }
}
