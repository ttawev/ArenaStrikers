// Импорт звуков из Assets/Arena/Resources/Audio (трибуны и музыка, см. Game/MatchAudio.cs):
// музыка и гул трибун — потоком с диска (длинные файлы не держим в памяти), короткие — сжатыми в памяти.
using UnityEditor;
using UnityEngine;

public class ArenaAudioImport : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        string p = assetPath.Replace('\\', '/');
        if (!p.StartsWith("Assets/Arena/Resources/Audio/")) return;
        var ai = (AudioImporter)assetImporter;
        string name = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
        bool longFile = name.StartsWith("music") || name == "crowd_loop";
        var s = ai.defaultSampleSettings;
        s.loadType = longFile ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = longFile ? 0.55f : 0.7f;
        s.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate; s.sampleRateOverride = 44100;
        ai.defaultSampleSettings = s;
        ai.loadInBackground = longFile;
    }
}
