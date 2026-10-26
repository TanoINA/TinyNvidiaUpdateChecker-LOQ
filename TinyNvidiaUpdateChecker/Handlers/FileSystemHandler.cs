using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace TinyNvidiaUpdateChecker.Handlers
{
    public static class FileSystemHandler
    {
        public static void DeleteWithRetry(string path, bool recursive = false)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    else if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive);
                    }

                    return;
                }
                catch (Exception ex) when (attempt < 3 && (ex is IOException or UnauthorizedAccessException))
                {
                    // Antivirus scanners can briefly lock newly extracted files.
                    Thread.Sleep(100);
                }
            }
        }

        public static bool DeleteTemporaryFiles(string folderPath, IEnumerable<string> tempFiles)
        {
            try
            {
                foreach (string entry in tempFiles)
                {
                    DeleteWithRetry(Path.Combine(folderPath, entry), true);
                }

                // If directory is empty after deleting tempFiles, delete it as well
                if (Directory.Exists(folderPath) && Directory.GetFileSystemEntries(folderPath).Length == 0)
                {
                    DeleteWithRetry(folderPath);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
