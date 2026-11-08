using System.Buffers.Binary;
using Hikidana.Core.Feedback;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Feedback;

public sealed class SoundSynthTests
{
    [Fact]
    public void EachCueIsAShortQuietWav()
    {
        foreach (var cue in Enum.GetValues<SoundCue>())
        {
            var wav = SoundSynth.Create(cue);
            var span = wav.AsSpan();
            Assert.True(span[..4].SequenceEqual("RIFF"u8) && span[8..12].SequenceEqual("WAVE"u8), $"{cue}: WAV の頭がありません。");
            Assert.Equal(wav.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(span[4..]), $"{cue}: RIFF の長さ");
            Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(span[20..]), $"{cue}: PCM");
            Assert.Equal(SoundSynth.SampleRate, BinaryPrimitives.ReadInt32LittleEndian(span[24..]), $"{cue}: 標本化の速さ");
            Assert.True(span[36..40].SequenceEqual("data"u8), $"{cue}: data がありません。");
            var bytes = BinaryPrimitives.ReadInt32LittleEndian(span[40..]);
            Assert.Equal(wav.Length - 44, bytes, $"{cue}: data の長さ");

            // 短く（0.25 秒まで）、やわらかい大きさ（いちばん大きいところでも半分まで）。音が無いわけではない
            var seconds = bytes / 2.0 / SoundSynth.SampleRate;
            Assert.True(seconds is > 0.03 and < 0.25, $"{cue}: {seconds:0.000} 秒");
            var peak = 0;
            for (var i = 44; i + 1 < wav.Length; i += 2)
            {
                peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(span[i..])));
            }

            Assert.True(peak is > 3000 and < short.MaxValue / 2, $"{cue}: いちばん大きいところ {peak}");

            // 終わりは静か（ぷつっと切れない）
            var last = Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(span[^2..]));
            Assert.True(last < 200, $"{cue}: 終わりの大きさ {last}");
        }
    }

    [Fact]
    public void CuesSoundDifferent()
    {
        Assert.False(SoundSynth.Create(SoundCue.Open).AsSpan().SequenceEqual(SoundSynth.Create(SoundCue.Close)));
        Assert.False(SoundSynth.Create(SoundCue.Open).AsSpan().SequenceEqual(SoundSynth.Create(SoundCue.Paste)));
    }
}
