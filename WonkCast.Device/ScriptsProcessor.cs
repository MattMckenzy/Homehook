using System.Diagnostics;
using WonkCast.Common.Services;

namespace WonkCast.DeviceService
{
    public class ScriptsProcessor : IHostedService
    {
        private enum LinuxScript
        { 
            JabraListen,
            JabraSetVolume
        }

        private static readonly Dictionary<LinuxScript, string> AutoStartLinuxScripts = new() 
        { 
            { LinuxScript.JabraListen, "" } 
        };

        private static Dictionary<LinuxScript, Process> LinuxScriptProcesses { get; } = new();
        
        private PlayerService PlayerService { get; }
        private LoggingService<ScriptsProcessor> LoggingService { get; }

        public ScriptsProcessor(PlayerService playerService, LoggingService<ScriptsProcessor> loggingService)
        {
            PlayerService = playerService;
            LoggingService = loggingService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                foreach (KeyValuePair<LinuxScript, string> linuxScript in AutoStartLinuxScripts)
                {
                    StartLinuxProcess(linuxScript.Key, linuxScript.Value);
                }
            }

            return Task.CompletedTask;
        }

        private void StartLinuxProcess(LinuxScript linuxScript, string arguments = "")
        {
            Process linuxProcess = new()
            {
                StartInfo = new()
                {
                    FileName = $"{linuxScript}.sh",
                    Arguments = arguments,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = "Scripts"
                },
                EnableRaisingEvents = true
            };

            linuxProcess.OutputDataReceived += (object _, DataReceivedEventArgs e) => { LinuxProcess_OutputDataReceived(linuxScript, e); };
            linuxProcess.ErrorDataReceived += (object _, DataReceivedEventArgs e) => { LinuxProcess_ErrorDataReceived(linuxScript, e); };
        }

        private void LinuxProcess_OutputDataReceived(LinuxScript sender, DataReceivedEventArgs e)
        {
            switch (sender)
            {
                case LinuxScript.JabraListen:
                    if (int.TryParse(e.Data, out int volumeStep))
                    {
                        PlayerService.Device.Volume = (double)volumeStep / 11;
                        PlayerService.UpdateClients();
                    }

                break;
            }
        }

        private void LinuxProcess_ErrorDataReceived(LinuxScript linuxScript, DataReceivedEventArgs e)
        {
            _ = Task.Run(async () => await LoggingService.LogError($"WonkCast Device \"{PlayerService.Device.Name}\" Error", $"Error in script \"{linuxScript}\": {e.Data}"));
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            foreach(Process process in LinuxScriptProcesses.Values)            
                process.Dispose();
            LinuxScriptProcesses.Clear();

            return Task.CompletedTask;
        }
    }
}