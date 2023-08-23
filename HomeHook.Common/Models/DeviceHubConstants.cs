namespace HomeHook.Common.Models
{
    public static class DeviceHubConstants
    {
        #region Client Method Constants

        public const string DeviceStatusUpdateMethod = "UpdateDeviceStatus";
        public const string StatusMessageUpdateMethod = "UpdateStatusMessage";
        public const string CurrentMediaItemIdUpdateMethod = "UpdateCurrentMediaItemId";
        public const string CurrentTimeUpdateMethod = "UpdateCurrentTime";
        public const string StartTimeUpdateMethod = "UpdateStartTime";
        public const string RepeatModeUpdateMethod = "UpdateRepeatMode";
        public const string VolumeUpdateMethod = "UpdateVolume";
        public const string IsMutedUpdateMethod = "UpdateIsMuted";
        public const string PlaybackRateUpdateMethod = "UpdatePlaybackRate";
        public const string MediaItemsAddMethod = "AddMediaItems";
        public const string MediaItemsRemoveMethod = "RemoveMediaItems";
        public const string MediaItemsMoveUpMethod = "MoveUpMediaItems";
        public const string MediaItemsMoveDownMethod = "MoveDownMediaItems";
        public const string MediaItemsClearMethod = "ClearMediaItems";
        public const string MediaQueueOrderUpdateMethod = "UpdateMediaQueueOrder";
        public const string MediaItemCacheUpdateMethod = "UpdateMediaItemCache";

        #endregion
    }
}
