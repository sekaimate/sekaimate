using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;

namespace Basis.MediaPipe.Editor
{
    public static class BasisMediaPipeWebBuild
    {
        private const string PackageRoot = "Packages/com.basis.mediapipe";
        private const string OutputDirectoryName = "BasisMediaPipeWeb";
        private static readonly string[] WebFiles =
        {
            "BasisMediaPipeWorker.mjs",
            "vision_bundle.mjs",
            "vision_wasm_module_internal.js",
            "vision_wasm_module_internal.wasm",
        };
        private static readonly string[] ModelFiles =
        {
            "face_landmarker.task.bytes",
            "hand_landmarker.task.bytes",
            "pose_landmarker_lite.task.bytes",
        };

        [PostProcessBuild(110)]
        public static void OnPostprocessBuild(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.WebGL)
            {
                return;
            }

            string outputDirectory = Path.Combine(buildPath, OutputDirectoryName);
            Directory.CreateDirectory(outputDirectory);
            CopyFiles(Path.Combine(PackageRoot, "Web~"), WebFiles, outputDirectory);
            CopyFiles(Path.Combine(PackageRoot, "Models"), ModelFiles, outputDirectory);
        }

        private static void CopyFiles(string sourceDirectory, string[] fileNames, string outputDirectory)
        {
            foreach (string fileName in fileNames)
            {
                string source = Path.Combine(sourceDirectory, fileName);
                if (!File.Exists(source))
                {
                    throw new BuildFailedException($"MediaPipe web runtime is missing {source}.");
                }

                File.Copy(source, Path.Combine(outputDirectory, fileName), true);
            }
        }
    }
}
