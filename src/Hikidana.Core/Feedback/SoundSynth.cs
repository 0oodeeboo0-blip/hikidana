using System.Buffers.Binary;

namespace Hikidana.Core.Feedback;

/// <summary>効果音の種類。</summary>
public enum SoundCue
{
    /// <summary>棚を開いた（上がる 2 つの音）。</summary>
    Open,

    /// <summary>棚を閉じた（下がる 2 つの音）。</summary>
    Close,

    /// <summary>貼り付けた（短い「コッ」という音）。</summary>
    Paste,
}

/// <summary>
/// 効果音を、その場で作る（音のファイルは持たない）。やわらかい短い音を、16 ビット・モノラル・44.1 kHz の WAV にする。
/// 鳴らすのはアプリの側（Windows の PlaySound）。
/// </summary>
public static class SoundSynth
{
    public const int SampleRate = 44100;

    /// <summary>1 つの音（ポーンと鳴って、すぐ消える）。</summary>
    /// <param name="Frequency">高さ（Hz）。</param>
    /// <param name="Start">鳴り始め（秒）。</param>
    /// <param name="Duration">長さ（秒）。</param>
    /// <param name="Amplitude">大きさ（0〜1）。</param>
    /// <param name="Glide">鳴っている間に変わる高さ（Hz。負なら下がる）。</param>
    private readonly record struct Blip(double Frequency, double Start, double Duration, double Amplitude, double Glide = 0);

    /// <summary>その効果音の WAV（ファイルの中身そのまま）。</summary>
    public static byte[] Create(SoundCue cue) => Render(cue switch
    {
        SoundCue.Open => [new Blip(660, 0, 0.08, 0.17), new Blip(990, 0.05, 0.11, 0.15)],
        SoundCue.Close => [new Blip(880, 0, 0.08, 0.15), new Blip(587, 0.05, 0.11, 0.13)],
        _ => [new Blip(1500, 0, 0.055, 0.17, Glide: -380), new Blip(740, 0, 0.045, 0.08)],
    });

    private static byte[] Render(Blip[] blips)
    {
        var seconds = blips.Max(b => b.Start + b.Duration) + 0.01;
        var count = (int)Math.Ceiling(seconds * SampleRate);
        var samples = new double[count];
        foreach (var blip in blips)
        {
            var start = (int)Math.Round(blip.Start * SampleRate);
            var length = (int)Math.Round(blip.Duration * SampleRate);
            var phase = 0.0;
            for (var i = 0; i < length && start + i < count; i++)
            {
                var progress = i / (double)length;
                phase += 2 * Math.PI * (blip.Frequency + (blip.Glide * progress)) / SampleRate;

                // 立ち上がりは 4 ミリ秒、そのあとはすっと消える。終わりで急に切れて「ぷつっ」と鳴らないよう、最後の 3 ミリ秒で 0 にする
                var attack = Math.Min(1, i / (0.004 * SampleRate));
                var tail = Math.Min(1, (length - i) / (0.003 * SampleRate));
                var envelope = attack * Math.Exp(-5 * progress) * tail;

                // 少しだけ倍音を混ぜて、丸い音にする
                samples[start + i] += blip.Amplitude * envelope * (Math.Sin(phase) + (0.18 * Math.Sin(2 * phase)));
            }
        }

        var pcm = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            var value = (short)Math.Round(Math.Clamp(samples[i], -1, 1) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), value);
        }

        return Wav(pcm);
    }

    /// <summary>16 ビット・モノラルの PCM に、WAV の頭を付ける。</summary>
    private static byte[] Wav(byte[] pcm)
    {
        var wav = new byte[44 + pcm.Length];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + pcm.Length);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], pcm.Length);
        pcm.CopyTo(span[44..]);
        return wav;
    }
}
