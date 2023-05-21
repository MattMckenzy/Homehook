using System.Collections.Concurrent;
using System.Diagnostics;
using HomeHook.Common.Models;
using HomeHook.Common.Services;
using HomeCast.Models;
using HomeCast.Services;

namespace HomeCast
{
    public partial class ScriptsProcessor : IHostedService
    {              
        private IConfiguration Configuration { get; }
        private LoggingService<ScriptsProcessor> LoggingService { get; }

        private static Dictionary<string, Script> Scripts { get; } = new()
        {
        };

        private static IEnumerable<ScriptType> AutoStartScripts { get;} = Array.Empty<ScriptType>();

        private static ConcurrentDictionary<string, Process> ScriptProcesses { get; } = new();

        private OSType OSType { get; set; }
        private DeviceModel DeviceModel { get; set; }

        public ScriptsProcessor(IConfiguration configuration, LoggingService<ScriptsProcessor> loggingService)
        {
            Configuration = configuration;
            LoggingService = loggingService;
        }

        public EventHandler<DeviceUpdateEventArgs>? DeviceUpdate { get; set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!Enum.TryParse(Configuration["Device:OS"], out OSType parsedOSType))
                throw new InvalidOperationException($"Please set a valid OS type in the \"Device:OS\" variable! Possible selections are: {string.Join(", ", Enum.GetNames<OSType>())}");
            else
                OSType = parsedOSType;

            if (!Enum.TryParse(Configuration["Device:Model"], out DeviceModel parsedModelType))
                throw new InvalidOperationException($"Please set a valid Device model in the \"Device:OS\" variable! Possible selections are: {string.Join(", ", Enum.GetNames<OSType>())}");
            else
                DeviceModel = parsedModelType;

            foreach (ScriptType scriptType in AutoStartScripts)
                StartScript(scriptType);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (Process process in ScriptProcesses.Values)
                process.Dispose();
            ScriptProcesses.Clear();

            return Task.CompletedTask;
        }

        public bool StartScript(ScriptType scriptType, string additionalArguments = "")
        {
            Script? script = Scripts.FirstOrDefault(item => item.Key == $"{OSType}-{DeviceModel}-{scriptType}").Value;
            if (script == null)
                return false;

            FileInfo scriptFileInfo = new(Path.Combine("Scripts", $"{script.DeviceModel}-{script.ScriptType}.{(script.OSType == OSType.Linux ? "sh" : string.Empty)}"));

            if (!scriptFileInfo.Exists)
                return false;

            Process process = new()
            {
                StartInfo = new()
                {
                    FileName = scriptFileInfo.FullName,
                    Arguments = $"{script.Arguments} {additionalArguments}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = scriptFileInfo.DirectoryName
                },
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (object _, DataReceivedEventArgs dataReceivedEventArgs) => LinuxProcess_OutputDataReceived(script, dataReceivedEventArgs);
            process.ErrorDataReceived += (object _, DataReceivedEventArgs dataReceivedEventArgs) => LinuxProcess_ErrorDataReceived(script, dataReceivedEventArgs);
            process.Exited += (object? _, EventArgs eventArgs) => LinuxProcess_Exited(script, additionalArguments);

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            ScriptProcesses.AddOrUpdate(script.Key, process, (_, currentProcess) => { currentProcess.Dispose(); return process; });

            return true;
        }

        private async void LinuxProcess_OutputDataReceived(Script script, DataReceivedEventArgs dataReceivedEventArgs)
        {
            await LoggingService.LogDebug($"HomeHook Script Output", $"Script output: {dataReceivedEventArgs.Data}");

            switch (script.ScriptType)
            {
                default:
                    break;
            }
        }
                                                                                                                                                                                                                                                                                                                                                                                                                                           
        private void LinuxProcess_ErrorDataReceived(Script script, DataReceivedEventArgs dataReceivedEventArgs) =>
            _ = Task.Run(async () => await LoggingService.LogError($"HomeHook Script Error", $"Error in script \"{script.DeviceModel}-{script.ScriptType}\": {dataReceivedEventArgs.Data}"));
        
        private void LinuxProcess_Exited(Script script, string additionalArguments)
        {
            ScriptProcesses.TryRemove(script.Key, out Process? process);
            process?.Dispose();

            if (script.IsPersistent)
                StartScript(script.ScriptType, additionalArguments);
        }
    }
}