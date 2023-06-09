﻿namespace SharpJaad.AAC
{
    public enum ChannelConfiguration : int
    {
        CHANNEL_CONFIG_UNSUPPORTED = -1,
        CHANNEL_CONFIG_NONE = 0,
        CHANNEL_CONFIG_MONO = 1,
        CHANNEL_CONFIG_STEREO = 2,
        CHANNEL_CONFIG_STEREO_PLUS_CENTER = 3,
        CHANNEL_CONFIG_STEREO_PLUS_CENTER_PLUS_REAR_MONO = 4,
        CHANNEL_CONFIG_FIVE = 5,
        CHANNEL_CONFIG_FIVE_PLUS_ONE = 6,
        CHANNEL_CONFIG_SEVEN_PLUS_ONE = 7
    }
}
