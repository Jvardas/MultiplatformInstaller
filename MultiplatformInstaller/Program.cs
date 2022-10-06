using Konsole;
using microk8sWinInstaller;
using MultiplatformInstaller.Installers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using static microk8sWinInstaller.Commons;

namespace MultiplatformInstaller
{
    class Program
    {
        public enum ProgramState
        {
            Main,
            Commands,
            Exit
        }

        public readonly static string cloudConfigPath = Path.Combine(Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]), "cloud-config.yaml");
        public readonly static string configPath = Path.Combine(Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]), "config.yaml");

        private static string initialMessage = "";
        static ProgramState programState = ProgramState.Main;
        static MultipassInstance selectedInstance = null;
        static ProgressBar progressBar = null;

        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--debug")
            {
                Console.WriteLine("Waiting for debugger...");
                SpinWait.SpinUntil(() => System.Diagnostics.Debugger.IsAttached);
                Console.WriteLine("Debugger attached");
            }

            InitializeConfigFile(cloudConfigPath);
            InitializeConfigFile(configPath);

            while (true)
            {
                switch (programState)
                {
                    case ProgramState.Main:
                        MainScreen();
                        break;
                    case ProgramState.Commands:
                        CommandsScreen();
                        break;
                    case ProgramState.Exit:
                        return;
                }
            }
        }

        public static void MainScreen()
        {
            Console.Clear();
            selectedInstance = null;

            ServiceController multipassService = ServiceController.GetServices().FirstOrDefault(sc => sc.ServiceName == "Multipass");

            if (multipassService == null)
            {
                CreateNewInstance();
                multipassService = ServiceController.GetServices().FirstOrDefault(sc => sc.ServiceName == "Multipass");
            }

            switch (multipassService.Status)
            {
                case ServiceControllerStatus.ContinuePending:
                case ServiceControllerStatus.StartPending:
                    Console.WriteLine("Multipassd is going to be running soon");
                    multipassService.WaitForStatus(ServiceControllerStatus.Running);
                    break;
                case ServiceControllerStatus.PausePending:
                    Console.WriteLine("Multipassd is being paused");
                    multipassService.WaitForStatus(ServiceControllerStatus.Paused);
                    Console.WriteLine("Multipassd is continuing");
                    multipassService.Continue();
                    multipassService.WaitForStatus(ServiceControllerStatus.Running);
                    break;
                case ServiceControllerStatus.StopPending:
                    Console.WriteLine("Multipassd is being stopped");
                    multipassService.WaitForStatus(ServiceControllerStatus.Stopped);
                    Console.WriteLine("Multipassd is starting");
                    multipassService.Start();
                    multipassService.WaitForStatus(ServiceControllerStatus.Running);
                    break;
                case ServiceControllerStatus.Paused:
                    Console.WriteLine("Multipassd is continuing");
                    multipassService.Continue();
                    multipassService.WaitForStatus(ServiceControllerStatus.Running);
                    break;
                case ServiceControllerStatus.Stopped:
                    Console.WriteLine("Multipassd is starting");
                    multipassService.Start();
                    multipassService.WaitForStatus(ServiceControllerStatus.Running);
                    break;
                default:
                    break;
            }

            Console.WriteLine("Multipassd is running");

            var instanceCount = 0;
            var activeInstances = new Dictionary<int, MultipassInstance>();
            ExecMultipassCommand("list", line =>
            {
                Console.Write((instanceCount == 0 ? "#" : (instanceCount - 1).ToString()) + " ");
                Console.WriteLine($"{line}");
                if (instanceCount++ == 0) return;

                var matches = Regex.Matches(line, @"(.+?)\s+");
                if (matches.Count() < 3) return;
                var name = matches[0].Groups[1]?.Value;
                var status = matches[1].Groups[1]?.Value;
                var ipv4 = matches[2].Groups[1]?.Value;
                if (!String.IsNullOrWhiteSpace(name))
                {
                    activeInstances.Add(instanceCount - 2, new MultipassInstance(name, status, ipv4)); // -2 cause the first line has the table headers and no vm names
                }
            });

            if (!activeInstances.Any())
            {
                var newInstance = CreateNewInstance();
                activeInstances.Add(0, newInstance);
            }

            Console.WriteLine();
            var selectedInstanceId = -1;
            if (activeInstances.Count >= 1)
            {
                while (true)
                {
                    Console.WriteLine($"Enter a vm id (0 - {activeInstances.Count - 1}) to proceed, type \"new\" to create a new instance, p to purge all deleted instances or type \"exit\" to exit: ");
                    var strSelectedInstanceId = Console.ReadLine();
                    if (strSelectedInstanceId == "p")
                    {

                        ExecMultipassCommand("purge", output =>
                        {
                            Console.WriteLine(output);
                        });
                        return;
                    }
                    else if (strSelectedInstanceId == "exit")
                    {
                        programState = ProgramState.Exit;
                        return;
                    }
                    else if (strSelectedInstanceId == "new")
                    {
                        var newInstance = CreateNewInstance();
                        selectedInstanceId = activeInstances.Count;
                        activeInstances.Add(selectedInstanceId, newInstance);
                        break;
                    }
                    if (Int32.TryParse(strSelectedInstanceId, out selectedInstanceId) && 0 <= selectedInstanceId && activeInstances.Count >= selectedInstanceId)
                    {
                        break;
                    }
                }
            }
            else
            {
                selectedInstanceId = 0;
            }

            var menuItems = new Dictionary<int, string>();

            selectedInstance = activeInstances[selectedInstanceId];
            var isPopulated = selectedInstance.PopulateCommands();
            if (isPopulated)
            {
                programState = ProgramState.Commands;
            }
            else
            {
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
            }
        }

        public static void CommandsScreen()
        {
            Console.Clear();
            Console.WriteLine("Available commands:");
            foreach (var cmd in selectedInstance.InstanceCommands)
            {
                Console.WriteLine($"{cmd.Key} {cmd.Value.Description}");
            }

            Console.WriteLine();

            while (true)
            {
                var selectedCommandId = -1;
                while (true)
                {
                    Console.WriteLine($"Enter a command id (0 - {selectedInstance.InstanceCommands.Count - 1}) to proceed or type \"exit\" to go back:");
                    var strSelectedCommandId = Console.ReadLine();
                    if (strSelectedCommandId == "exit")
                    {
                        programState = ProgramState.Main;
                        return;
                    }
                    if (Int32.TryParse(strSelectedCommandId, out selectedCommandId) && 0 <= selectedCommandId && selectedInstance.InstanceCommands.Count > selectedCommandId)
                    {
                        break;
                    }
                }

                var selectedCommand = selectedInstance.InstanceCommands[selectedCommandId];
                Console.WriteLine();
                var commandResult = selectedCommand.Command();
                Console.WriteLine(commandResult);
                Console.WriteLine();
                if (selectedCommand.ShouldExitAfterExecution)
                {
                    programState = ProgramState.Main;
                    return;
                }
            }
        }

        public static MultipassInstance CreateNewInstance(bool openShellOnComplete = false)
        {

            var currentOs = OperatingSystem.CurrentOS();
            IInstaller installer;

            if (OperatingSystem.IsWindows())
            {
                installer = new WindowsInstaller();
            }
            else if (OperatingSystem.IsMacOS())
            {
                throw new NotImplementedException();
            }
            else if (OperatingSystem.IsLinux())
            {
                throw new NotImplementedException();
            }
            else
            {
                return null;
            }

            if (!installer.IsInstalled)
            {
                Console.WriteLine("Searching for the latest multipass release...");
                InitializeProgressBar("Downloading installer...");
                var installerPath = installer.Download(UpdateProgressBar);
                Console.CursorVisible = true;
                installer.Install(installerPath);
            }
            else
            {
                installer.ClearInstallationFiles();
            }

            var launchCommand = $"launch --cloud-init \"{cloudConfigPath}\"";
            InitializeProgressBar("Downloading image...");
            ExecMultipassCommand(launchCommand, line =>
            {
                var progressMatch = Regex.Match(line, @"([0-9]+)\s*%");
                if (progressMatch.Success && Int32.TryParse(progressMatch.Groups[1].Value, out var progress))
                {
                    UpdateProgressBar(progress, "");
                }
                Console.CursorVisible = true;
            });

            string vmName = "";
            string status = "";
            string ipv4 = "";
            ExecMultipassCommand("list", line =>
            {
                var matches = Regex.Matches(line, @"(.+?)\s+");
                if (matches.Count < 3)
                {
                    return;
                }
                vmName = matches[0].Groups[1]?.Value;
                status = matches[1].Groups[1]?.Value;
                ipv4 = matches[2].Groups[1]?.Value;
            });

            if (!String.IsNullOrWhiteSpace(vmName) && openShellOnComplete)
            {
                ExecMultipassCommand("shell " + vmName, redirectOutput: false);
            }

            return new MultipassInstance(vmName, MultipassInstanceStatus.Running, ipv4);
        }

        private static void UpdateProgressBar(int v, string message)
        {
            if (progressBar == null)
            {
                return;
            }

            progressBar.Refresh(v, message);
        }

        private static void InitializeProgressBar(string progressName)
        {
            const int totalTicks = 100;

            progressBar = new ProgressBar(totalTicks);
            Console.CursorVisible = false;
        }

        private static void InitializeConfigFile(string cfgFileName)
        {
            var fi = new FileInfo(cfgFileName);
            while (!fi.Exists)
            {
                Console.WriteLine($"{fi.Name} not found!");
                Console.Write("Continue with [D]efault/[C]reate your own/[E]dit default:");
                var option = Console.ReadKey();
                Console.WriteLine();

                switch (option.Key)
                {
                    case ConsoleKey.C:
                        Console.WriteLine($"Press any key when {fi.Name} is ready...");
                        Console.ReadKey();
                        break;
                    case ConsoleKey.E:
                        Console.WriteLine($"Press any key when {fi.Name} is ready...");
                        WriteResourceToFile($"MultiplatformInstaller.{fi.Name}", cfgFileName);
                        Console.ReadKey();
                        break;
                    case ConsoleKey.D:
                        WriteResourceToFile($"MultiplatformInstaller.{fi.Name}", cfgFileName);
                        break;
                    default:
                        break;
                }
                fi = new FileInfo(cfgFileName);
            }
        }

        private static void WriteResourceToFile(string resourceName, string fileName)
        {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                using (var file = new FileStream(fileName, FileMode.Create, FileAccess.Write))
                {
                    resource.CopyTo(file);
                }
            }
        }
    }
}
