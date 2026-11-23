using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace T2U.Kit.Tests
{
    /// <summary>Test data shipped with the package (Tests/Data). Outside Unity: env T2U_KIT_TESTDATA.</summary>
    public static class KitTestData
    {
        public static string Dir
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("T2U_KIT_TESTDATA");
                if (!string.IsNullOrEmpty(env)) return env;
#if UNITY_EDITOR
                var pi = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(KitTestData).Assembly);
                if (pi != null) return Path.Combine(pi.resolvedPath, "Tests", "Data");
#endif
                return Path.GetFullPath("Packages/com.t2u.kit/Tests/Data");
            }
        }

        public static JObject Json(string file) => JObject.Parse(File.ReadAllText(Path.Combine(Dir, file)));
        public static double Hex(string h) => BitConverter.Int64BitsToDouble(Convert.ToInt64(h, 16));
        public static string Hex(double d) => BitConverter.DoubleToInt64Bits(d).ToString("x16");
    }
}
