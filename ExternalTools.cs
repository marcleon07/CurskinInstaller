using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace CurskinInstaller
{
    static class ExternalTools
    {
        private const long AvifDecSize = 12314624;
        private const long ResvgSize = 3392000;

        private static readonly object sync = new object();
        private static string toolDirectory;

        public static string AvifDecPath
        {
            get { return EnsureTool("avifdec.exe.gz", "avifdec.exe", AvifDecSize); }
        }

        public static string ResvgPath
        {
            get { return EnsureTool("resvg.exe.gz", "resvg.exe", ResvgSize); }
        }

        private static string EnsureTool(string resourceName, string fileName, long expectedSize)
        {
            lock (sync)
            {
                if (toolDirectory == null)
                {
                    toolDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "CurskinInstaller", "tools");
                }
                Directory.CreateDirectory(toolDirectory);

                string target = Path.Combine(toolDirectory, fileName);
                if (File.Exists(target) && new FileInfo(target).Length == expectedSize)
                    return target;

                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (resource == null)
                    {
                        string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                        if (File.Exists(local))
                            return local;
                        throw new InvalidOperationException(
                            "The bundled " + fileName + " tool could not be found.");
                    }

                    string temporary = target + ".tmp";
                    using (GZipStream gzip = new GZipStream(resource, CompressionMode.Decompress))
                    using (FileStream output = new FileStream(temporary, FileMode.Create,
                        FileAccess.Write, FileShare.None))
                    {
                        gzip.CopyTo(output);
                    }

                    if (File.Exists(target))
                        File.Delete(target);
                    File.Move(temporary, target);
                }

                return target;
            }
        }

        public static string Run(string executable, string arguments, out int exitCode)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = executable;
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.WorkingDirectory = Path.GetDirectoryName(executable);

            StringBuilder output = new StringBuilder();
            object outputLock = new object();

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        lock (outputLock)
                            output.AppendLine(e.Data);
                    }
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        lock (outputLock)
                            output.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                process.WaitForExit();
                exitCode = process.ExitCode;
            }

            lock (outputLock)
                return output.ToString();
        }

        public static string Quote(string value)
        {
            if (value == null)
                return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
