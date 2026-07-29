namespace Bento
{
    public static class SceneGeometryIntersectionSession
    {
        static SceneGeometryScanResult _current;

        public static SceneGeometryScanResult Current => _current;

        public static void Replace(SceneGeometryScanResult result) => _current = result;

        public static void Clear() => _current = null;
    }
}
