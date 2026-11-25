using System;
using System.Linq;
using Windows.Media.MediaProperties;

namespace Hepta
{
    public class MediaOptions
    {
        public int Height = 720, Fps = 0, VideoKbps = 0, AudioKbps = 0, SampleRate = 0, Channels = 0;
        public MediaOptions Copy()
        {
            return (MediaOptions)MemberwiseClone();
        }

        public bool Valid()
        {
            return new[]
            {
                480,
                720,
                1080
            }.Contains(Height) && new[]
            {
                0,
                24,
                25,
                30,
                50,
                60
            }.Contains(Fps) && new[]
            {
                0,
                1000,
                2500,
                5000,
                8000,
                12000
            }.Contains(VideoKbps) && new[]
            {
                0,
                96,
                128,
                192,
                256,
                320
            }.Contains(AudioKbps) && new[]
            {
                0,
                22050,
                44100,
                48000
            }.Contains(SampleRate) && Channels >= 0 && Channels <= 2;
        }

        public void Apply(MediaEncodingProfile profile, string target)
        {
            if (!Valid())
                throw new ArgumentException("Media settings");
            if (profile.Video != null)
            {
                profile.Video.Height = (uint)Height;
                profile.Video.Width = (uint)(Height == 480 ? 854 : Height == 720 ? 1280 : 1920);
                if (Fps > 0)
                {
                    profile.Video.FrameRate.Numerator = (uint)Fps;
                    profile.Video.FrameRate.Denominator = 1;
                }

                if (VideoKbps > 0)
                    profile.Video.Bitrate = (uint)VideoKbps * 1000;
            }

            if (profile.Audio != null)
            {
                if (SampleRate > 0)
                    profile.Audio.SampleRate = (uint)SampleRate;
                if (Channels > 0)
                    profile.Audio.ChannelCount = (uint)Channels;
                if (AudioKbps > 0 && target != "wav" && target != "flac")
                    profile.Audio.Bitrate = (uint)AudioKbps * 1000;
            }
        }
    }
}
