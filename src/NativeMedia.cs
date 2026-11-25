using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Media.Transcoding;
using Windows.Media.MediaProperties;
using Windows.Data.Pdf;

namespace Hepta
{
    internal static class NativeMedia
    {
        internal static async Task Wait(Windows.Foundation.IAsyncInfo op, CancellationToken token)
        {
            while (op.Status == Windows.Foundation.AsyncStatus.Started)
            {
                if (token.IsCancellationRequested)
                    op.Cancel();
                await Task.Delay(20).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
        }

        internal static async Task<T> Result<T>(Windows.Foundation.IAsyncOperation<T> op, CancellationToken token)
        {
            await Wait(op, token).ConfigureAwait(false);
            return op.GetResults();
        }

        static async Task Done(Windows.Foundation.IAsyncAction op, CancellationToken token)
        {
            await Wait(op, token).ConfigureAwait(false);
            op.GetResults();
        }

        static async Task Done(Windows.Foundation.IAsyncActionWithProgress<double> op, CancellationToken token)
        {
            await Wait(op, token).ConfigureAwait(false);
            op.GetResults();
        }

        public static async Task Transcode(string input, string output, string target, CancellationToken token, MediaOptions options = null, IProgress<double> progress = null)
        {
            var file = await Result(StorageFile.GetFileFromPathAsync(input), token).ConfigureAwait(false);
            var dest = await Result(StorageFile.GetFileFromPathAsync(output), token).ConfigureAwait(false);
            MediaEncodingProfile format;
            switch (target)
            {
                case "flac":
                    format = MediaEncodingProfile.CreateFlac(AudioEncodingQuality.High);
                    break;
                case "wma":
                    format = MediaEncodingProfile.CreateWma(AudioEncodingQuality.High);
                    break;
                case "avi":
                    format = MediaEncodingProfile.CreateAvi(VideoEncodingQuality.Vga);
                    break;
                case "mp3":
                    format = MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High);
                    break;
                case "m4a":
                    format = MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High);
                    break;
                case "wav":
                    format = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
                    break;
                case "wmv":
                    format = MediaEncodingProfile.CreateWmv(VideoEncodingQuality.HD720p);
                    break;
                default:
                    format = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
                    break;
            }

            if (options != null)
                options.Apply(format, target);
            var transcoder = new MediaTranscoder();
            using (var source = await Result(file.OpenAsync(FileAccessMode.Read), token).ConfigureAwait(false))
            using (var destination = await Result(dest.OpenAsync(FileAccessMode.ReadWrite), token).ConfigureAwait(false))
            {
                var plan = await Result(transcoder.PrepareStreamTranscodeAsync(source, destination, format), token).ConfigureAwait(false);
                if (!plan.CanTranscode)
                    throw new InvalidDataException(plan.FailureReason.ToString());
                var op = plan.TranscodeAsync();
                if (progress != null)
                    op.Progress = (sender, percent) => progress.Report(percent);
                await Done(op, token).ConfigureAwait(false);
            }
        }

        public static async Task Join(string[] inputs, string output, bool video, CancellationToken token)
        {
            var composition = new Windows.Media.Editing.MediaComposition();
            TimeSpan offset = TimeSpan.Zero;
            foreach (var input in inputs)
            {
                var file = await Result(StorageFile.GetFileFromPathAsync(input), token).ConfigureAwait(false);
                if (video)
                    composition.Clips.Add(await Result(Windows.Media.Editing.MediaClip.CreateFromFileAsync(file), token).ConfigureAwait(false));
                else
                {
                    var track = await Result(Windows.Media.Editing.BackgroundAudioTrack.CreateFromFileAsync(file), token).ConfigureAwait(false);
                    track.Delay = offset;
                    offset += track.OriginalDuration;
                    composition.BackgroundAudioTracks.Add(track);
                }
            }

            if (!video)
                composition.Clips.Add(Windows.Media.Editing.MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 0, 0, 0), offset));
            var dest = await Result(StorageFile.GetFileFromPathAsync(output), token).ConfigureAwait(false);
            var profile = video ? MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p) : MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High);
            var op = composition.RenderToFileAsync(dest, Windows.Media.Editing.MediaTrimmingPreference.Precise, profile);
            await Wait(op, token).ConfigureAwait(false);
            if (op.GetResults() != TranscodeFailureReason.None)
                throw new InvalidDataException("Media composition failed");
        }

        public static async Task<int> Pages(string file, CancellationToken token)
        {
            var f = await Result(StorageFile.GetFileFromPathAsync(file), token).ConfigureAwait(false);
            var doc = await Result(PdfDocument.LoadFromFileAsync(f), token).ConfigureAwait(false);
            return (int)doc.PageCount;
        }

        public static async Task<System.Windows.Size> Render(string input, int number, string output, string target, CancellationToken token)
        {
            var f = await Result(StorageFile.GetFileFromPathAsync(input), token).ConfigureAwait(false);
            var doc = await Result(PdfDocument.LoadFromFileAsync(f), token).ConfigureAwait(false);
            using (var page = doc.GetPage((uint)number))
            using (var ms = new InMemoryRandomAccessStream())
            {
                if (page.Size.Width <= 0 || page.Size.Height <= 0 || page.Size.Width * page.Size.Height > 16000000)
                    throw new InvalidDataException("PDF page size");
                double scale = OutputOptions.Current.PdfDpi / 96d;
                if (page.Size.Width * page.Size.Height * scale * scale > 32000000)
                    throw new InvalidDataException("PDF resolution limit");
                await Done(page.RenderToStreamAsync(ms, new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)), BitmapEncoderId = target == "jpg" ? Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId : Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId }), token).ConfigureAwait(false);
                ms.Seek(0);
                using (var reader = new DataReader(ms.GetInputStreamAt(0)))
                using (var dest = File.Create(output))
                {
                    while (dest.Position < (long)ms.Size)
                    {
                        uint count = (uint)Math.Min(65536L, (long)ms.Size - dest.Position);
                        var op = reader.LoadAsync(count);
                        await Wait(op, token).ConfigureAwait(false);
                        uint read = op.GetResults();
                        if (read == 0)
                            throw new EndOfStreamException();
                        var buffer = new byte[read];
                        reader.ReadBytes(buffer);
                        dest.Write(buffer, 0, buffer.Length);
                    }
                }

                return new System.Windows.Size(page.Size.Width * .75, page.Size.Height * .75);
            }
        }
    }
}
