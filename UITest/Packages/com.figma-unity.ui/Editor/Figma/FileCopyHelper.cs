using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace FigmaUnity.UI.Editor.Figma
{
    /// <summary>
    /// Windows-safe file copy with retries (avoids failures when PNG is open in Explorer/Figma).
    /// </summary>
    public static class FileCopyHelper
    {
        const int DefaultMaxAttempts = 6;

        public static bool TryCopy(string sourcePath, string destPath, out string error, int maxAttempts = DefaultMaxAttempts)
        {
            error = null;
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destPath))
            {
                error = "Source or destination path is empty.";
                return false;
            }

            sourcePath = Path.GetFullPath(sourcePath);
            destPath = Path.GetFullPath(destPath);

            if (!File.Exists(sourcePath))
            {
                error = $"Source file not found: {sourcePath}";
                return false;
            }

            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            if (File.Exists(destPath)
                && string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
                return true;

            var delayMs = 40;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    CopyViaTempFile(sourcePath, destPath);
                    return true;
                }
                catch (IOException ex)
                {
                    if (attempt < maxAttempts - 1)
                    {
                        Thread.Sleep(delayMs);
                        delayMs = Math.Min(delayMs * 2, 500);
                        continue;
                    }

                    if (File.Exists(destPath))
                    {
                        error =
                            $"Could not overwrite locked file '{destPath}' ({ex.Message}). " +
                            "Keeping the existing copy — close Figma/Preview if you need a fresh export.";
                        Debug.LogWarning($"[Figma UI] {error}");
                        return true;
                    }

                    error = $"Failed to copy to '{destPath}': {ex.Message}";
                    return false;
                }
                catch (UnauthorizedAccessException ex)
                {
                    if (File.Exists(destPath))
                    {
                        error =
                            $"Access denied writing '{destPath}' ({ex.Message}). Keeping existing copy.";
                        Debug.LogWarning($"[Figma UI] {error}");
                        return true;
                    }

                    error = $"Access denied writing '{destPath}': {ex.Message}";
                    return false;
                }
            }

            error = $"Failed to copy to '{destPath}'.";
            return false;
        }

        static void CopyViaTempFile(string sourcePath, string destPath)
        {
            var tempPath = destPath + ".figmaunity.tmp";
            try
            {
                File.Copy(sourcePath, tempPath, true);

                if (File.Exists(destPath))
                {
                    try
                    {
                        File.Replace(tempPath, destPath, null);
                        return;
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // fallback below
                    }

                    File.Delete(destPath);
                }

                File.Move(tempPath, destPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch { /* ignore stale temp */ }
                }
            }
        }
    }
}
