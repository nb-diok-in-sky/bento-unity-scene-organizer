using System;

namespace Bento
{
    [Serializable]
    public class AssetNamingOptions
    {
        public string prefix = "";
        public string baseName = "";
        public string suffix = "";
        public bool appendNumber;
        public int startNumber = 1;
        public int numberDigits = 2;
    }

    public static class AssetNaming
    {
        public static string BuildName(string originalName, int index, AssetNamingOptions options)
        {
            options = options ?? new AssetNamingOptions();
            string body = string.IsNullOrWhiteSpace(options.baseName) ? originalName : options.baseName;
            string number = options.appendNumber
                ? (options.startNumber + index).ToString("D" + Math.Max(1, options.numberDigits))
                : string.Empty;
            return BentoUtility.SanitizeFileName($"{options.prefix}{body}{number}{options.suffix}", originalName);
        }

        public static void Copy(AssetNamingOptions source, AssetNamingOptions destination)
        {
            if (source == null || destination == null) return;
            destination.prefix = source.prefix;
            destination.baseName = source.baseName;
            destination.suffix = source.suffix;
            destination.appendNumber = source.appendNumber;
            destination.startNumber = source.startNumber;
            destination.numberDigits = source.numberDigits;
        }

    }
}
