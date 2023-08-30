using HomeCast.Models;
using HomeHook.Common.Models;
using HomeHook.Common.Services;
using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;

namespace HomeCast.Services
{
    public partial class CecService : IDisposable
    {
        #region Injections

        private PlayerService PlayerService { get; }
        private LoggingService<CecService> LoggingService { get; }
        private IConfiguration Configuration { get; }

        #endregion

        #region Private Properties

        private Process? CecClient { get; set; }
        private bool IsClientReady { get; set; } = false;
        private bool IsCecActiveSource { get; set; } = false;
        private bool IsDisplayOn { get; set; } = false;
        private int ProcessTimeoutSeconds { get; }

        private int? StandbyTimeoutMinutes { get; set; }
        private Dictionary<string, string?> EnvironmentVariables { get; } = new();

        private PeriodicTimer PeriodicTimer { get; } = new(TimeSpan.FromMinutes(1));
        private CancellationTokenSource PeriodicTimerCancellationTokenSource { get; } = new();
        private DateTime LastMadeActive { get; set; } = DateTime.Now;

        private bool IsDisposed { get; set; }

        #endregion

        #region Contructor

        public CecService(PlayerService playerService, LoggingService<CecService> loggingService, IConfiguration configuration)
        {
            PlayerService = playerService;
            LoggingService = loggingService;
            Configuration = configuration;

            StandbyTimeoutMinutes = Configuration.GetValue<int?>("Services:Cec:StandbyTimeoutMinutes");

            ProcessTimeoutSeconds = 10;

            PlayerService.MediaPlayCallback = MakeActive;

            EnvironmentVariables =
                Configuration.GetSection("Services:Cec:EnvironmentVariables")
                    .GetChildren()
                    .Where(section => !string.IsNullOrWhiteSpace(section.Key))
                    .ToDictionary(section => section.Key, section => section.Value);

            if (StandbyTimeoutMinutes != null)
            {
                _ = Task.Run(async () =>
                {
                    while (await PeriodicTimer.WaitForNextTickAsync())
                    {
                        if (PeriodicTimerCancellationTokenSource.Token.IsCancellationRequested)
                            continue;

                        if (DateTime.Now - LastMadeActive > TimeSpan.FromMinutes((int)StandbyTimeoutMinutes) &&
                            await GetIdleMinutes() > StandbyTimeoutMinutes)
                            await Standby();
                    }
                });
            }
        }

        #endregion

        #region Process Methods 

        private void StartClient()
        {
            IsClientReady = false;
            CecClient?.Dispose();

            CecClient = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cec-client",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true
            };

            foreach (KeyValuePair<string, string?> environmentVariable in EnvironmentVariables)
                CecClient.StartInfo.EnvironmentVariables.Add(environmentVariable.Key, environmentVariable.Value);

            CecClient.OutputDataReceived += CecClient_OutputDataReceived;
            CecClient.ErrorDataReceived += CecClient_ErrorDataReceived;
            CecClient.Exited += CecClient_Exited;

            CecClient.Start();

            CecClient.BeginOutputReadLine();
            CecClient.BeginErrorReadLine();
        }

        private async Task<double> GetIdleMinutes()
        {            
            Process process = new()
            {
                StartInfo = new ProcessStartInfo {
                    FileName = "xprintidle",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            foreach (KeyValuePair<string, string?> environmentVariable in EnvironmentVariables)
                process.StartInfo.EnvironmentVariables.Add(environmentVariable.Key, environmentVariable.Value);

            process.Start();

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd(); 

            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(error))
            {
                await LoggingService.LogDebug("xprintidle Error", error);
                return 0;
            }
            else if (double.TryParse(output, out double milliseconds))
                return TimeSpan.FromMilliseconds(milliseconds).TotalMinutes;
            else
            {
                await LoggingService.LogDebug("xprintidle Output error", $"Could not parse \"{output}\" as milliseconds.");
                return 0;
            }    
        }

        private void StopProcess()
        {
            IsClientReady = false;
            IsCecActiveSource = false;
            IsDisplayOn = false;
            CecClient?.Dispose();
            CecClient = null;
        }

        private async void CecClient_OutputDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            string? cecClientOutput = dataReceivedEventArgs.Data;
            if (cecClientOutput != null)
            {
                if (cecClientOutput.Contains("usbcec: updating active source status: active"))
                {
                    LastMadeActive = DateTime.Now;
                    IsCecActiveSource = true;
                    await PlayerService.Play();
                }
                else if (cecClientOutput.Contains("usbcec: updating active source status: inactive"))
                {
                    IsCecActiveSource = false;
                    await PlayerService.Pause();
                }
                else if (cecClientOutput.Contains("power status changed from") && cecClientOutput.Contains("to 'on'"))
                {
                    if (IsCecActiveSource)
                        LastMadeActive = DateTime.Now;
                    IsDisplayOn = true;
                    await PlayerService.Pause();
                }
                else if (cecClientOutput.Contains("power status changed from") && cecClientOutput.Contains("to 'standby'"))
                {
                    IsDisplayOn = false;
                    await PlayerService.Play();
                }
                else if (cecClientOutput.Contains("waiting for input"))
                {
                    IsClientReady = true;
                }
                else if (IsCecActiveSource)
                {
                    if (cecClientOutput.Contains("key released: play"))
                        await PlayerService.Play();
                    else if (cecClientOutput.Contains("key released: pause"))
                        await PlayerService.Pause();
                    else if (cecClientOutput.Contains("key released: up"))
                        await PlayerService.Previous();
                    else if (cecClientOutput.Contains("key released: down"))
                        await PlayerService.Next();
                    else if (cecClientOutput.Contains("key released: left"))
                        await PlayerService.SeekRelative(-10);
                    else if (cecClientOutput.Contains("key released: right"))
                        await PlayerService.SeekRelative(10);
                    else if (cecClientOutput.Contains("key released: select"))
                        await PlayerService.Pause();
                    else if (cecClientOutput.Contains("key released: F1"))
                        await PlayerService.ToggleMute();
                    else if (cecClientOutput.Contains("key released: F2"))
                        await PlayerService.Stop();
                    else if (cecClientOutput.Contains("key released: F3"))
                        await PlayerService.ChangeRepeatMode(RepeatMode.Shuffle);
                    else if (cecClientOutput.Contains("key released: F4"))
                        await PlayerService.ChangeRepeatMode((await PlayerService.GetDevice()).RepeatMode == RepeatMode.All ? RepeatMode.One : RepeatMode.All);
                }
            }
        }

        private async void CecClient_ErrorDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            await LoggingService.LogDebug("CecClient Error", dataReceivedEventArgs.Data ?? string.Empty);
        }

        private async void CecClient_Exited(object? sender, EventArgs eventArgs)
        {
            await LoggingService.LogError("CecClient Exited", string.Empty);
            StopProcess();
        }

        #endregion

        #region Public Commands

        public async Task MakeActive()
        {
            if (!IsClientReady)
                StartClient();
                      
            if (await WaitForClient() && !IsCecActiveSource)
            {
                await CecClient!.StandardInput.WriteLineAsync("as");
            }
        }

        public async Task Standby()
        {
            if (!IsClientReady)
                StartClient();

            if (await WaitForClient() && IsCecActiveSource && IsDisplayOn)
            {
                await CecClient!.StandardInput.WriteLineAsync("standby 0");
            }
        }

        #endregion

        #region Private Methods

        private async Task<bool> WaitForClient()
        {
            if (CecClient == null || !IsClientReady)
            {
                CancellationToken socketWaitToken = new CancellationTokenSource(TimeSpan.FromSeconds(ProcessTimeoutSeconds)).Token;
                while (CecClient == null || !IsClientReady)
                {
                    if (socketWaitToken.IsCancellationRequested)
                    {
                        await LoggingService.LogError("CecClient Process Timeout", $"Command could not be processed since process is not ready.");
                        return false;
                    }

                    await Task.Delay(100);
                }
            }

            return true;
        }

        #endregion

        #region IDisposable Implementation

        protected virtual void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                }

                StopProcess();
                IsDisposed = true;
            }
        }

        ~CecService()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
