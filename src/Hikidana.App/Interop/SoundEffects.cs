using System.Runtime.InteropServices;
using Hikidana.Core.Diagnostics;
using Hikidana.Core.Feedback;

namespace Hikidana.App.Interop;

/// <summary>
/// 効果音を鳴らす（Windows の PlaySound。音はその場で作った WAV を、メモリから鳴らす）。画面のスレッドで呼ぶ。
/// 鳴らしている間も Windows が音の中身を読むので、作った音はアプリが終わるまで手放さない。
/// </summary>
internal static partial class SoundEffects
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_MEMORY = 0x0004;

    private static readonly Dictionary<SoundCue, nint> Sounds = [];
    private static bool broken;

    public static void Play(SoundCue cue)
    {
        if (broken)
        {
            return;
        }

        try
        {
            if (!Sounds.TryGetValue(cue, out var sound))
            {
                var wav = SoundSynth.Create(cue);
                sound = Marshal.AllocHGlobal(wav.Length);
                Marshal.Copy(wav, 0, sound, wav.Length);
                Sounds[cue] = sound;
            }

            // 前の音が鳴っていれば、止めて鳴らす（音は短いので、重ねなくてよい）
            PlaySound(sound, 0, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or OutOfMemoryException)
        {
            broken = true;
            Log.Warn("効果音を鳴らせませんでした。", ex);
        }
    }

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(nint sound, nint module, uint flags);
}
