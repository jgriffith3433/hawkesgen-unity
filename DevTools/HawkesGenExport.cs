// Not part of the package: exports Assets/HawkesGen as a .unitypackage (the direct download on hawkesgen.com).
//   Unity -batchmode -quit -projectPath . -executeMethod HawkesGen.DevTests.HawkesGenExport.Run -out <path>
using System;
using UnityEditor;

namespace HawkesGen.DevTests
{
    public static class HawkesGenExport
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-out");
            string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : "HawkesGen.unitypackage";
            AssetDatabase.ExportPackage("Assets/HawkesGen", path, ExportPackageOptions.Recurse);
            UnityEngine.Debug.Log("[export] " + path);
        }
    }
}
