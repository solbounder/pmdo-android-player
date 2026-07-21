using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Audio;
using NVorbis;

namespace RogueEssence.Content
{
    public sealed class LoopedSong : IDisposable
    {
        private const int BufferSeconds = 1;
        private readonly object sync = new object();
        private readonly VorbisReader reader;
        private readonly DynamicSoundEffectInstance soundStream;
        private readonly float[] samples;
        private long loopStart;
        private long loopEnd;
        private bool disposed;

        public string Name { get; }
        public float Volume { get => soundStream.Volume; set => soundStream.Volume = AudioVolume.Sanitize(value); }
        public SoundState State => soundStream.State;
        public int Channels { get; }
        public int SampleRate { get; }
        public Dictionary<string, List<string>> Tags { get; }

        public LoopedSong(string fileName)
        {
            DynamicSoundEffectInstance createdStream = null;
            try
            {
                reader = new VorbisReader(fileName);
                Channels = reader.Channels;
                SampleRate = reader.SampleRate;
                Name = Path.GetFileNameWithoutExtension(fileName);
                loopStart = 0;
                loopEnd = reader.TotalSamples;
                Tags = ParseTags(reader.Comments);
                if (TryGetLong("LOOPSTART", out long start))
                    loopStart = Math.Clamp(start, 0, reader.TotalSamples);
                if (TryGetLong("LOOPLENGTH", out long length) && length > 0)
                    loopEnd = Math.Min(reader.TotalSamples, loopStart + length);
                else if (TryGetLong("LOOPEND", out long end))
                    loopEnd = Math.Clamp(end, loopStart + 1, reader.TotalSamples);
                samples = new float[Math.Max(Channels * SampleRate * BufferSeconds, Channels * 1024)];
                createdStream = new DynamicSoundEffectInstance(SampleRate, Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
                createdStream.BufferNeeded += BufferNeeded;
                soundStream = createdStream;
            }
            catch
            {
                createdStream?.Dispose();
                reader?.Dispose();
                throw;
            }
        }

        internal void Play()
        {
            lock (sync)
            {
                reader.SamplePosition = 0;
                QueueBuffer();
                QueueBuffer();
                QueueBuffer();
                soundStream.Play();
            }
        }

        internal void PlayAt(long sample)
        {
            lock (sync)
            {
                reader.SamplePosition = Math.Clamp(sample, 0, reader.TotalSamples);
                QueueBuffer();
                QueueBuffer();
                soundStream.Play();
            }
        }

        internal void Resume() => soundStream.Resume();
        internal void Pause() => soundStream.Pause();

        internal bool MaintainPlayback()
        {
            lock (sync)
            {
                if (disposed || soundStream.State == SoundState.Paused)
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
                soundStream.Stop();
                reader.SamplePosition = 0;
            }
        }

        public long GetSamplesPlayed() => reader.SamplePosition;

        public long GetSampleFromTimeSpan(TimeSpan time)
        {
            long sample = (long)(time.TotalSeconds * SampleRate);
            long loopLength = Math.Max(1, loopEnd - loopStart);
            return sample < loopStart ? sample : loopStart + ((sample - loopStart) % loopLength);
        }

        private void BufferNeeded(object sender, EventArgs args)
        {
            lock (sync)
                QueueBuffer();
        }

        private void QueueBuffer()
        {
            if (disposed)
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
            if (disposed)
                return;
            disposed = true;
            soundStream.BufferNeeded -= BufferNeeded;
            soundStream.Dispose();
            reader.Dispose();
        }

        public override bool Equals(object obj) => obj is LoopedSong other && string.Equals(Name, other.Name, StringComparison.Ordinal);
        public override int GetHashCode() => Name.GetHashCode();
        public static bool operator ==(LoopedSong left, LoopedSong right) => Equals(left, right);
        public static bool operator !=(LoopedSong left, LoopedSong right) => !Equals(left, right);
    }
}
