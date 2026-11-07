using System.Text.Json.Serialization;
using Hikidana.Core.Clips;

namespace Hikidana.Core.Storage;

/// <summary>clips.json の中身。</summary>
public sealed class ClipIndex
{
    /// <summary>形式の版。中身の形を変えたら上げる。</summary>
    public int Version { get; set; } = 1;

    public List<Clip> Items { get; set; } = [];
}

/// <summary>clips\{id}\formats.json の 1 行。そのまま戻す形の名前と、中身のファイル。</summary>
public sealed class StoredFormat
{
    /// <summary>形の名前（登録された形の番号は起動のたびに変わるので、名前で覚える）。</summary>
    public required string Name { get; set; }

    /// <summary>Windows に最初からある形（CF_）なら、その番号。登録された形なら 0。</summary>
    public uint StandardId { get; set; }

    /// <summary>中身のファイルの名前。</summary>
    public required string File { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ClipIndex))]
[JsonSerializable(typeof(List<StoredFormat>))]
internal sealed partial class StoreJsonContext : JsonSerializerContext;
