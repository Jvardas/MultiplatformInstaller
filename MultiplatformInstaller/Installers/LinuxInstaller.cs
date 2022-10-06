using System;

namespace MultiplatformInstaller.Installers
{
    internal class LinuxInstaller : IInstaller
    {
        public bool IsInstalled => throw new NotImplementedException();
        public bool IsDownloadRequired => false;

        public void ClearInstallationFiles()
        {
        }

        public string Download(Action<int, string> reportProgressCallback = null)
        {
            throw new InvalidOperationException();
        }

        public void Install(string _)
        {
            // TODO How to check if it is already installed. 1. Check filepath 2. check whith "which" command 3. .net api for linux installation
            // TODO If it is not installed check if snapd is installed or run snapd command and if it fails download it and retry
            // TODO Check if progress reporting is available during installation
            // TODO Check that path is properly populated and if current session is up-to-date
        }
    }
}
