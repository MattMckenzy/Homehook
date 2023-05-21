using HomeHook.Common.Services;

namespace HomeCast.Services
{
    public class CacheService : IDisposable
    {
        #region Injections

        private LoggingService<CacheService> LoggingService { get; }
        private IConfiguration Configuration { get; }

        #endregion

        #region Constructor

        public CacheService(LoggingService<CacheService> loggingService, IConfiguration configuration)
        {
            LoggingService = loggingService;
            Configuration = configuration;
        }

        #endregion

        #region Private Properties

        private bool DisposedValue { get; set; }

        #endregion

        #region Public Properties

        #endregion

        #region CacheService Implementation

        #endregion

        #region IDIsposable Implementation

        protected virtual void Dispose(bool disposing)
        {
            if (!DisposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                DisposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~CacheService()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Helper Methods

        #endregion
    }
}
