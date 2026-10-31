using UnityEditor;
using UnityEngine;

namespace BirimizKovulacak.EditorTools
{
    /// <summary>Import settings for Audio/Resources/music/*.ogg: Vorbis; long beds stream, short stingers decode in memory.</summary>
    public sealed class MusicImportPostprocessor : AssetPostprocessor
    {
        const string Folder = "Audio/Resources/music/";
        const float StingerSeconds = 20f;

        void OnPreprocessAudio()
        {
            if (assetPath.IndexOf(Folder, System.StringComparison.OrdinalIgnoreCase) < 0) return;
            var imp = (AudioImporter)assetImporter;
            // Long beds are named in the manifest; stingers are the short cues.
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool stinger = name == "meeting_intro" || name == "results_success" || name == "results_collapse";
            imp.forceToMono = false;
            imp.loadInBackground = true;
            imp.ambisonic = false;
            var s = imp.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            s.loadType = stinger ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.Streaming;
            imp.defaultSampleSettings = s;
            imp.preloadAudioData = false;
        }
    }
}
