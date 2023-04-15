using System.Runtime.Serialization;

namespace WonkCast.Models.Jellyfin
{
    public enum ProgressEvents
    {
        [EnumMember(Value = "timeupdate")]
        TimeUpdate,
        [EnumMember(Value="pause")]
        Pause,
        [EnumMember(Value = "unpause")]
        Unpause,
        [EnumMember(Value = "volumechange")]
        VolumeChange,
        [EnumMember(Value = "repeatmodechange")]
        RepeatModeChange,
        [EnumMember(Value = "audiotrackchange")]
        AudioTrackChange,
        [EnumMember(Value = "subtitletrackchange")]
        SubtitleTrackChange,
        [EnumMember(Value = "playlistitemMove")]
        PlaylistItemMove,
        [EnumMember(Value = "playlistitemRemove")]
        PlaylistItemRemove,
        [EnumMember(Value = "playlistitemAdd")]
        PlaylistItemAdd,
        [EnumMember(Value = "qualitychange")]
        QualityChange,
        [EnumMember(Value = "subtitleoffsetchange")]
        SubtitleOffsetChange,
        [EnumMember(Value = "playbackratechange")]
        PlaybackRateChange
    }
}