using HomeHook.Common.Models;
using HomeHook.Common.Services;
using System.Diagnostics;

namespace HomeCast.Services
{
    public class CommandService
    {
        private IConfiguration Configuration { get; }
        private LoggingService<CommandService> LoggingService { get; }

        public CommandService(IConfiguration configuration, LoggingService<CommandService> loggingService)
        {
            Configuration = configuration;
            LoggingService = loggingService;
        }

        public IEnumerable<CommandDefinition> CommandDefinitions
        {
            get
            {
                return Configuration.GetSection("Device:Commands").Get<IEnumerable<CommandDefinition>>()
                    ?? Array.Empty<CommandDefinition>();
            }
        }

        public async Task CallCommand(string name)
        {
            CommandDefinition? commandDefinition = CommandDefinitions.FirstOrDefault(commandDefinition => commandDefinition.Name.Equals(name));
            if (commandDefinition != null && !string.IsNullOrWhiteSpace(commandDefinition.Command))
            {
                Process CommandProcess = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"-c \"{commandDefinition.Command.Replace("\"", "\\\"")}\"",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    },
                    EnableRaisingEvents = true
                };

                CommandProcess.OutputDataReceived += CommandProcess_OutputDataReceived;
                CommandProcess.ErrorDataReceived += CommandProcess_ErrorDataReceived;
                CommandProcess.Exited += CommandProcess_Exited;
                CommandProcess.Start();

                CommandProcess.BeginOutputReadLine();
                CommandProcess.BeginErrorReadLine();
            }
            else
                await LoggingService.LogError("Command not found", $"Could not find a valid command with the name: {name}");
        }

        private async void CommandProcess_OutputDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            await LoggingService.LogDebug("Command Output", dataReceivedEventArgs.Data ?? "N/A");
        }

        private async void CommandProcess_ErrorDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            await LoggingService.LogError("Command Error", dataReceivedEventArgs.Data ?? "N/A");
        }

        private async void CommandProcess_Exited(object? sender, EventArgs eventArgs)
        {
            await LoggingService.LogDebug("Command Process Exited", string.Empty);
        }
    }
}