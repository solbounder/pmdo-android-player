using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using Microsoft.Xna.Framework.Audio;
using NVorbis;

namespace RogueEssence.Content
{
    public sealed class LoopedSong : IDisposable
    {
        private const int BufferSeconds = 1;
        private readonly object sync = new object();
        private readonly string fileName;
        private readonly long totalSamples;
        private VorbisReader reader;
        private DynamicSoundEffectInstance soundStream;
        private float[] samples;
        private long loopStart;
        private long loopEnd;
        private float volume = 1f;
        private bool disposed;

        public string Name { get; }
        public float Volume
        {
            get { lock (sync) return volume; }
            set
            {
                lock (sync)
                {
                    volume = AudioVolume.Sanitize(value);
                    if (soundStream != null)
                        soundStream.Volume = AudioVolume.Sanitize(volume);
                }
            }
        }
        public SoundState State
        {
            get { lock (sync) return soundStream?.State ?? SoundState.Stopped; }
        }
        public int Channels { get; }
        public int SampleRate { get; }
        public Dictionary<string, List<string>> Tags { get; }

        public LoopedSong(string fileName)
        {
            this.fileName = fileName;
            using VorbisReader metadata = new VorbisReader(fileName);
            Channels = metadata.Channels;
            if (Channels is not 1 and not 2)
                throw new InvalidDataException("Android audio supports only mono or stereo Vorbis streams.");
            SampleRate = metadata.SampleRate;
            totalSamples = metadata.TotalSamples;
            Name = Path.GetFileNameWithoutExtension(fileName);
            loopStart = 0;
            loopEnd = totalSamples;
            Tags = ParseTags(metadata.Comments);
            if (TryGetLong("LOOPSTART", out long start))
                loopStart = Math.Clamp(start, 0, totalSamples);
            if (TryGetLong("LOOPLENGTH", out long length) && length > 0)
                loopEnd = Math.Min(totalSamples, loopStart + length);
            else if (TryGetLong("LOOPEND", out long end))
                loopEnd = Math.Clamp(end, loopStart + 1, totalSamples);
        }

        internal void Play() => StartPlayback(0, 3);

        internal void PlayAt(long sample) => StartPlayback(Math.Clamp(sample, 0, totalSamples), 2);

        private void StartPlayback(long sample, int bufferCount)
        {
            DynamicSoundEffectInstance failedStream = null;
            VorbisReader failedReader = null;
            Exception failure = null;
            lock (sync)
            {
                ThrowIfDisposed();
                try
                {
                    EnsurePlaybackResources();
                    reader.SamplePosition = sample;
                    for (int index = 0; index < bufferCount; index++)
                        QueueBuffer();
                    soundStream.Play();
                }
                catch (Exception ex)
                {
                    failure = ex;
                    (failedStream, failedReader) = DetachPlaybackResources();
                }
            }
            failedStream?.Dispose();
            failedReader?.Dispose();
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private void EnsurePlaybackResources()
        {
            ThrowIfDisposed();
            if (soundStream != null)
                return;

            VorbisReader createdReader = null;
            DynamicSoundEffectInstance createdStream = null;
            try
            {
                createdReader = new VorbisReader(fileName);
                if (createdReader.Channels != Channels ||
                    createdReader.SampleRate != SampleRate ||
                    createdReader.TotalSamples < loopEnd)
                    throw new InvalidDataException("Vorbis stream metadata changed before playback.");
                float[] createdSamples = new float[Math.Max(Channels * SampleRate * BufferSeconds, Channels * 1024)];
                createdStream = new DynamicSoundEffectInstance(
                    SampleRate,
                    Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
                createdStream.Volume = AudioVolume.Sanitize(volume);
                createdStream.BufferNeeded += BufferNeeded;

                reader = createdReader;
                samples = createdSamples;
                soundStream = createdStream;
                createdReader = null;
                createdStream = null;
            }
            catch
            {
                if (createdStream != null)
                    createdStream.BufferNeeded -= BufferNeeded;
                createdStream?.Dispose();
                createdReader?.Dispose();
                throw;
            }
        }

        internal void Resume()
        {
            lock (sync)
                soundStream?.Resume();
        }

        internal void Pause()
        {
            lock (sync)
                soundStream?.Pause();
        }

        internal bool MaintainPlayback()
        {
            lock (sync)
            {
                if (disposed || soundStream == null || reader == null || samples == null ||
                    soundStream.State == SoundState.Paused)
                    return false;

                while (soundStream.PendingBufferCount < 3)
                {
                    int previousCount = soundStream.PendingBufferCount;
                    QueueBuffer();
                    if (soundStream.PendingBufferCount == previousCount)
                        break;
                }

                if (soundStream.State == SoundState.Stopped && soundStream.PendingBufferCount > 0)
                {
                    soundStream.Play();
                    return true;
                }
                return false;
            }
        }

        internal void Stop()
        {
            lock (sync)
            {
                if (soundStream == null || reader == null)
                    return;
                soundStream.Stop();
                reader.SamplePosition = 0;
            }
        }

        public long GetSamplesPlayed()
        {
            lock (sync)
                return reader?.SamplePosition ?? 0;
        }

        public long GetSampleFromTimeSpan(TimeSpan time)
        {
            long sample = (long)(time.TotalSeconds * SampleRate);
            long loopLength = Math.Max(1, loopEnd - loopStart);
            return sample < loopStart ? sample : loopStart + ((sample - loopStart) % loopLength);
        }

        private void BufferNeeded(object sender, EventArgs args)
        {
            lock (sync)
            {
                if (disposed || soundStream == null || reader == null || samples == null ||
                    !ReferenceEquals(sender, soundStream))
                    return;
                QueueBuffer();
            }
        }

        private void QueueBuffer()
        {
            if (disposed || soundStream == null || reader == null || samples == null)
                return;
            if (reader.SamplePosition >= loopEnd)
                reader.SamplePosition = loopStart;
            long remainingFrames = loopEnd - reader.SamplePosition;
            int wanted = (int)Math.Min(samples.Length, remainingFrames * Channels);
            int read = wanted > 0 ? reader.ReadSamples(samples, 0, wanted) : 0;
            if (read == 0)
            {
                reader.SamplePosition = loopStart;
                read = reader.ReadSamples(samples, 0, samples.Length);
            }
            if (read > 0)
                soundStream.SubmitBuffer(ToPcm16(samples, read));
        }

        internal static byte[] ToPcm16(float[] source, int count)
        {
            byte[] pcm = new byte[count * sizeof(short)];
            for (int i = 0; i < count; i++)
            {
                short value = (short)Math.Clamp((int)Math.Round(source[i] * short.MaxValue), short.MinValue, short.MaxValue);
                pcm[i * 2] = (byte)value;
                pcm[i * 2 + 1] = (byte)(value >> 8);
            }
            return pcm;
        }

        private bool TryGetLong(string name, out long value)
        {
            value = 0;
            return Tags.TryGetValue(name, out List<string> values) && values.Count > 0 && long.TryParse(values[0], out value);
        }

        private static Dictionary<string, List<string>> ParseTags(IEnumerable<string> comments)
        {
            Dictionary<string, List<string>> result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string comment in comments ?? Array.Empty<string>())
            {
                string[] split = comment.Split(new[] { '=' }, 2);
                if (split.Length != 2)
                    continue;
                if (!result.TryGetValue(split[0], out List<string> values))
                {
                    values = new List<string>();
                    result[split[0]] = values;
                }
                values.Add(split[1]);
            }
            return result;
        }

        public void Dispose()
        {
            DynamicSoundEffectInstance oldStream;
            VorbisReader oldReader;
            lock (sync)
            {
                if (disposed)
                    return;
                disposed = true;
                (oldStream, oldReader) = DetachPlaybackResources();
            }
            oldStream?.Dispose();
            oldReader?.Dispose();
        }

        private (DynamicSoundEffectInstance Stream, VorbisReader Reader) DetachPlaybackResources()
        {
            DynamicSoundEffectInstance oldStream = soundStream;
            VorbisReader oldReader = reader;
            if (oldStream != null)
                oldStream.BufferNeeded -= BufferNeeded;
            soundStream = null;
            reader = null;
            samples = null;
            return (oldStream, oldReader);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(LoopedSong));
        }

        public override bool Equals(object obj) => obj is LoopedSong other && string.Equals(Name, other.Name, StringComparison.Ordinal);
        public override int GetHashCode() => Name.GetHashCode();
        public static bool operator ==(LoopedSong left, LoopedSong right) => Equals(left, right);
        public static bool operator !=(LoopedSong left, LoopedSong right) => !Equals(left, right);
    }
}
