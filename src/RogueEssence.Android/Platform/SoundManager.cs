using System;
using System.Collections.Generic;
using Android.Util;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using NVorbis;

namespace RogueEssence.Content
{
    public sealed class SongSetting
    {
        public LoopedSong Song;
        public float CrossVolume;
        public SongSetting(LoopedSong song, float crossVolume) { Song = song; CrossVolume = crossVolume; }
    }

    public static class SoundManager
    {
        private const int MaxConcurrentOneShots = 24;
        private static float bgmVol;
        private static float bgmBalance;
        private static float seBalance;
        private static Dictionary<string, SongSetting> songs;
        private static Dictionary<string, LoopedSong> loopedSE;
        private static List<DynamicSoundEffectInstance> sounds;
        private static readonly string[] playedSounds = new string[24];
        private static int soundIndex;

        public static float BGMBalance { get => bgmBalance; set { bgmBalance = value; UpdateSongVolume(); } }
        public static float SEBalance { get => seBalance; set => seBalance = value; }

        public static void InitStatic()
        {
            bgmBalance = 1f;
            seBalance = 1f;
            songs = new Dictionary<string, SongSetting>();
            loopedSE = new Dictionary<string, LoopedSong>();
            sounds = new List<DynamicSoundEffectInstance>();
        }

        public static void PlayBGM(string baseFile, string[] family)
        {
            foreach (SongSetting old in songs.Values) { old.Song.Stop(); old.Song.Dispose(); }
            songs.Clear();
            DisposeOneShotSounds();

            if (TryPlayBGM(baseFile, family))
                return;

            // Music has priority over ambient loops. If an unusually small
            // OpenAL pool is still exhausted, release loops and retry once.
            StopAllLoopedSE();
            if (!TryPlayBGM(baseFile, family))
            {
                Log.Warn("PMDO-AUDIO", "BGM could not reserve an audio source after cleanup: " + baseFile);
                RogueEssence.FNALoggerEXT.LogWarn?.Invoke("Android BGM could not reserve an audio source after cleanup: " + baseFile);
            }
        }

        private static bool TryPlayBGM(string baseFile, string[] family)
        {
            try
            {
                foreach (string fileName in family)
                {
                    LoopedSong song = new LoopedSong(fileName);
                    songs.Add(fileName, new SongSetting(song, fileName == baseFile ? 1f : 0f));
                }
                UpdateSongVolume();
                foreach (SongSetting song in songs.Values) song.Song.Play();
                Log.Info("PMDO-AUDIO", "BGM started with " + songs.Count + " stream(s): " + baseFile);
                RogueEssence.FNALoggerEXT.LogInfo?.Invoke("Android BGM started with " + songs.Count + " stream(s): " + baseFile);
                return true;
            }
            catch (InstancePlayLimitException)
            {
                DisposeSongs();
                return false;
            }
            catch
            {
                DisposeSongs();
                throw;
            }
        }

        private static void DisposeSongs()
        {
            foreach (SongSetting song in songs.Values)
                song.Song.Dispose();
            songs.Clear();
        }

        public static void SetBGMVolume(float volume) { bgmVol = volume; UpdateSongVolume(); }
        public static void SetBGMCrossVolume(Dictionary<string, float> volumes)
        {
            foreach (KeyValuePair<string, float> entry in volumes)
                if (songs.TryGetValue(entry.Key, out SongSetting song)) song.CrossVolume = entry.Value;
            UpdateSongVolume();
        }

        private static void UpdateSongVolume()
        {
            if (songs == null) return;
            foreach (SongSetting song in songs.Values)
                song.Song.Volume = AudioVolume.Sanitize(bgmVol * BGMBalance * (float)Math.Log10(song.CrossVolume * 9f + 1f));
        }

        public static void PlayLoopedSE(string fileName, float volume = 1f)
        {
            if (string.IsNullOrWhiteSpace(fileName) || loopedSE.ContainsKey(fileName)) return;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                LoopedSong effect = null;
                try
                {
                    effect = new LoopedSong(fileName) { Volume = AudioVolume.Sanitize(volume * SEBalance) };
                    effect.Play();
                    loopedSE.Add(fileName, effect);
                    return;
                }
                catch (InstancePlayLimitException)
                {
                    effect?.Dispose();
                    if (attempt == 0)
                    {
                        DisposeOneShotSounds();
                        continue;
                    }
                    return;
                }
                catch
                {
                    effect?.Dispose();
                    throw;
                }
            }
        }

        public static void StopLoopedSE(string fileName, float volume = 1f)
        {
            if (!loopedSE.TryGetValue(fileName, out LoopedSong effect)) return;
            effect.Stop(); effect.Dispose(); loopedSE.Remove(fileName);
        }

        public static void StopAllLoopedSE()
        {
            foreach (LoopedSong effect in loopedSE.Values) { effect.Stop(); effect.Dispose(); }
            loopedSE.Clear();
        }

        public static void SetLoopedSEVolume(string fileName, float volume)
        {
            if (loopedSE.TryGetValue(fileName, out LoopedSong effect)) effect.Volume = AudioVolume.Sanitize(volume * SEBalance);
        }

        public static void NewFrame(GameTime gameTime)
        {
            soundIndex = 0;
            RemoveStoppedOneShots();
            foreach (SongSetting song in songs.Values)
                if (song.Song.MaintainPlayback())
                    Log.Warn("PMDO-AUDIO", "Recovered an underrun in BGM stream: " + song.Song.Name);
            foreach (LoopedSong effect in loopedSE.Values)
                if (effect.MaintainPlayback())
                    Log.Warn("PMDO-AUDIO", "Recovered an underrun in looped effect: " + effect.Name);
        }

        private static void RemoveStoppedOneShots()
        {
            for (int i = sounds.Count - 1; i >= 0; i--)
                if (sounds[i].State == SoundState.Stopped || sounds[i].PendingBufferCount == 0)
                {
                    sounds[i].Stop();
                    sounds[i].Dispose();
                    sounds.RemoveAt(i);
                }
        }

        private static void DisposeOneShotSounds()
        {
            foreach (DynamicSoundEffectInstance sound in sounds)
            {
                sound.Stop();
                sound.Dispose();
            }
            sounds.Clear();
        }

        private static bool DisposeOldestOneShot()
        {
            if (sounds.Count == 0) return false;
            DynamicSoundEffectInstance oldest = sounds[0];
            sounds.RemoveAt(0);
            oldest.Stop();
            oldest.Dispose();
            return true;
        }

        public static void OnApplicationPause()
        {
            if (songs != null) foreach (SongSetting song in songs.Values) song.Song.Pause();
            if (loopedSE != null) foreach (LoopedSong effect in loopedSE.Values) effect.Pause();
            if (sounds != null) foreach (DynamicSoundEffectInstance sound in sounds) if (sound.State == SoundState.Playing) sound.Pause();
        }

        public static void OnApplicationResume()
        {
            if (songs != null) foreach (SongSetting song in songs.Values) if (song.Song.State == SoundState.Paused) song.Song.Resume();
            if (loopedSE != null) foreach (LoopedSong effect in loopedSE.Values) if (effect.State == SoundState.Paused) effect.Resume();
            if (sounds != null) foreach (DynamicSoundEffectInstance sound in sounds) if (sound.State == SoundState.Paused) sound.Resume();
        }

        public static int PlaySound(string fileName, float volume = 1f)
        {
            if (volume * seBalance <= 0f || soundIndex == playedSounds.Length) return 0;
            for (int i = 0; i < soundIndex; i++) if (fileName == playedSounds[i]) return 0;
            playedSounds[soundIndex++] = fileName;
            RemoveStoppedOneShots();
            if (sounds.Count >= MaxConcurrentOneShots)
                DisposeOldestOneShot();
            using VorbisReader reader = new VorbisReader(fileName);
            if (reader.TotalSamples > int.MaxValue / Math.Max(1, reader.Channels)) return 0;
            float[] decoded = new float[(int)reader.TotalSamples * reader.Channels];
            int read = reader.ReadSamples(decoded, 0, decoded.Length);
            if (read <= 0) return 0;
            byte[] pcm = LoopedSong.ToPcm16(decoded, read);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                DynamicSoundEffectInstance sound = null;
                try
                {
                    sound = new DynamicSoundEffectInstance(reader.SampleRate, reader.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
                    sound.Volume = AudioVolume.Sanitize(volume * seBalance);
                    sound.SubmitBuffer(pcm);
                    sound.Play();
                    sounds.Add(sound);
                    return (int)(reader.TotalTime.TotalSeconds * 60d);
                }
                catch (InstancePlayLimitException)
                {
                    sound?.Dispose();
                    if (attempt == 0 && DisposeOldestOneShot())
                        continue;
                    Log.Warn("PMDO-AUDIO", "Sound effect could not reserve an audio source: " + fileName);
                    return 0;
                }
                catch
                {
                    sound?.Dispose();
                    throw;
                }
            }
            return 0;
        }
    }
}
