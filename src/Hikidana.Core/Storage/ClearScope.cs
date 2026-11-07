using Hikidana.Core.Clips;

namespace Hikidana.Core.Storage;

/// <summary>棚をまとめて片付けるときの範囲。どちらも、ピン留めしたものは消さない。</summary>
public enum ClearScope
{
    /// <summary>スクリーンショットだけ。</summary>
    Screenshots,

    /// <summary>ピン留め以外のすべて。</summary>
    All,
}

public static class ClearScopeExtensions
{
    /// <summary><paramref name="clip"/> が、<paramref name="scope"/> でまとめて消すものに入るなら true。</summary>
    public static bool Matches(this ClearScope scope, Clip clip) =>
        !clip.Pinned && (scope == ClearScope.All || clip.Kind == ClipKind.Screenshot);
}
