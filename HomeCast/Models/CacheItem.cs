using System.ComponentModel;

namespace HomeCast.Models
{
    public class CacheItem : INotifyPropertyChanged
    {
        #region Private Variables

        private bool isReady = false;
        private double? cachedRatio = null;

        #endregion

        #region Public Properties

        public required FileInfo CacheFileInfo { get; set; }

        public bool IsReady { get => isReady; set { isReady = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsReady))); } }
        public double? CachedRatio { get => cachedRatio; set { cachedRatio = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CachedRatio))); } }
        public CancellationTokenSource CacheCancellationTokenSource { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion
    }
}