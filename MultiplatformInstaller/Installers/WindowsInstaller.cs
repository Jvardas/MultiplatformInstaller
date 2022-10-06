using Octokit;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace MultiplatformInstaller.Installers
{
    internal class WindowsInstaller : IInstaller
    {
        private readonly string _startupShortcutPath;
        private readonly string _executablePath;
        public WindowsInstaller()
        {
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            _startupShortcutPath = Path.Combine(startupFolder, "microk8sWinInstaller.lnk");
            _executablePath = Environment.GetCommandLineArgs()[0];
        }

        public bool IsInstalled => Directory.Exists(@"C:\Program Files\Multipass");

        public string Download(Action<int, string> reportProgressCallback = null)
        {
            var gitClient = new GitHubClient(new ProductHeaderValue("MultipassInstaller"));
            var releases = gitClient.Repository.Release.GetAll("CanonicalLtd", "multipass").GetAwaiter().GetResult();
            var latestRelease = releases.Where(r => r.Assets.Any(a => a.ContentType.Contains("ms-dos"))).OrderByDescending(r => r.PublishedAt).FirstOrDefault();

            if (latestRelease == null)
            {
                throw new Exception("Couldn't find latest multipass releases");
            }

            var asset = latestRelease.Assets.First(a => a.ContentType.Contains("ms-dos"));

            var assetUrl = asset.BrowserDownloadUrl;

            var downloadPath = Path.Combine(Path.GetDirectoryName(_executablePath), asset.Name);
            DownloadInstaller(assetUrl, downloadPath, reportProgressCallback);
            return downloadPath;
        }

        public void Install(string installerPath)
        {
            CreateShortcut(_startupShortcutPath, _executablePath);

            DeployApplication(installerPath);
        }

        public void ClearInstallationFiles()
        {
            if (File.Exists(_startupShortcutPath))
            {
                File.Delete(_startupShortcutPath);
            }
        }

        private void DownloadInstaller(string uri, string targetName, Action<int, string> setProgress = null)
        {
            if (File.Exists(targetName))
            {
                Console.WriteLine("Installer already downloaded");
                return;
            }

            var mre = new ManualResetEvent(false);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            IAsyncResult asyncResult = null;


            asyncResult = request.BeginGetResponse((state) =>
            {
                var response = request.EndGetResponse(asyncResult) as HttpWebResponse;
                var length = response.ContentLength;

                var responseStream = response.GetResponseStream();
                var file = GetContentWithProgressReporting(responseStream, length, setProgress);

                File.WriteAllBytes(targetName, file);

                mre.Set();
            }, null);

            mre.WaitOne();
        }

        private void CreateShortcut(string shortcutPath, string targetPath)
        {
            Type t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")); //Windows Script Host Shell Object
            dynamic shell = Activator.CreateInstance(t);
            try
            {
                var lnk = shell.CreateShortcut(shortcutPath);
                try
                {
                    lnk.TargetPath = targetPath;
                    lnk.IconLocation = "shell32.dll, 1";
                    lnk.Save();
                }
                finally
                {
                    Marshal.FinalReleaseComObject(lnk);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }


        private void DeployApplication(string executableFilePath)
        {
            Console.WriteLine(" ");
            Console.WriteLine("Deploying application...");
            try
            {
                var oldPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
                var fi = new FileInfo(executableFilePath);
                var psi = new ProcessStartInfo("Powershell")
                {
                    WorkingDirectory = fi.DirectoryName,
                    Arguments = $"$setup=Start-Process '{executableFilePath}' -ArgumentList '/S' -Wait -PassThru",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };

                var p = Process.Start(psi);
                p.OutputDataReceived += (s, e) => { Console.WriteLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { Console.Error.WriteLine(e.Data); };


                p.WaitForExit();

                var newUserPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
                var addedPathValue = newUserPath.Replace(oldPath, String.Empty).Trim(';');
                Environment.SetEnvironmentVariable("Path", oldPath + ';' + addedPathValue);

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error occured: {0}", ex.InnerException);
            }
        }


        private void DeployApplicationOld(string executableFilePath)
        {
            PowerShell powerShell = null;
            Console.WriteLine(" ");
            Console.WriteLine("Deploying application...");
            try
            {
                using (powerShell = PowerShell.Create())
                {
                    powerShell.AddScript($"$setup=Start-Process '{executableFilePath}' -ArgumentList '/S' -Wait -PassThru");

                    Collection<PSObject> PSOutput = powerShell.Invoke();
                    foreach (PSObject outputItem in PSOutput)
                    {
                        if (outputItem != null)
                        {

                            Console.WriteLine(outputItem.BaseObject.GetType().FullName);
                            Console.WriteLine(outputItem.BaseObject.ToString() + "\n");
                        }
                    }

                    if (powerShell.Streams.Error.Count > 0)
                    {
                        string temp = powerShell.Streams.Error.First().ToString();
                        Console.WriteLine("Error: {0}", temp);

                    }
                    else
                    {
                        Console.WriteLine("Installation has completed successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error occured: {0}", ex.InnerException);
            }
            finally
            {
                if (powerShell != null)
                    powerShell.Dispose();
            }
        }

        private static byte[] GetContentWithProgressReporting(Stream responseStream, long contentLength, Action<int, string> setProgress)
        {
            setProgress?.Invoke(0, "Downloading multipass");

            // Allocate space for the content
            var data = new byte[contentLength];
            int currentIndex = 0;
            var buffer = new byte[256];
            do
            {
                int bytesReceived = responseStream.Read(buffer, 0, 256);
                Array.Copy(buffer, 0, data, currentIndex, bytesReceived);
                currentIndex += bytesReceived;

                // Report percentage
                double percentage = (double)currentIndex / contentLength;

                setProgress?.Invoke((int)(percentage * 100), "Downloading multipass");
            } while (currentIndex < contentLength);

            setProgress?.Invoke(100, "Downloading multipass");
            return data;
        }
    }
}
