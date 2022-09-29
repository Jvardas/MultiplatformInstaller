using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace MultiplatformInstaller.Installers
{
    internal interface IInstaller
    {
        bool IsInstalled { get; }

        /// <summary>
        /// Download the latest multipass release from GitHub
        /// </summary>
        /// <param name="reportProgressCallback">Optional. A callback used for reporting download progress</param>
        /// <returns>The path of the downloaded installer</returns>
        string Download(Action<int, string> reportProgressCallback = null);
        void Install(string installerPath);
        void ClearInstallationFiles();
    }
}
