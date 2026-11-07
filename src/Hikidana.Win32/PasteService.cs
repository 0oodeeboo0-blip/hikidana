using Hikidana.Core.Clips;
using Hikidana.Core.Paste;
using Hikidana.Core.Storage;
using Hikidana.Win32.Native;

namespace Hikidana.Win32;

/// <summary>貼り付けの結果の種類。</summary>
public enum PasteOutcome
{
    /// <summary>貼り付けた（クリップボードに置いて、Ctrl+V を送った）。</summary>
    Pasted,

    /// <summary>クリップボードに置いただけ。貼り付けは利用者が Ctrl+V で行う。</summary>
    CopiedOnly,

    /// <summary>クリップボードにも置けなかった。</summary>
    Failed,
}

/// <summary>貼り付けの結果。<see cref="Message"/> は、そのまま画面の知らせに出せる文。</summary>
/// <param name="TargetName">貼り付け先の名前（「ChatGPT」など）。</param>
/// <param name="ExportedFile">ファイルとして渡すために書き出したファイル。</param>
/// <param name="Detail">失敗したときの詳しい理由（記録用）。</param>
public sealed record PasteResult(PasteOutcome Outcome, string Message, string? TargetName = null, string? ExportedFile = null, string? Detail = null);

/// <summary>
/// しまったものを貼り付ける。クリップボードに書いて読み戻して確かめ、貼り付け先を確かめてから Ctrl+V を送る。
/// 確かめられないことがあれば Ctrl+V は送らず、コピーまでにして、そのことを知らせる。
/// </summary>
public sealed class PasteService
{
    private readonly ClipStore store;
    private readonly ClipboardWatcher watcher;

    public PasteService(ClipStore store, ClipboardWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(watcher);
        this.store = store;
        this.watcher = watcher;
    }

    /// <summary>押されたままのキーが離れるのを待つ長さ。過ぎたら、離したことにしてから送る。</summary>
    public TimeSpan ModifierTimeout { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>貼り付けたもの・コピーしたものを、いちばん上へ動かす。</summary>
    public bool MoveToTop { get; set; } = true;

    /// <summary>クリップボードに置くだけ（棚の「コピー」）。</summary>
    public async Task<PasteResult> CopyAsync(Clip clip, PasteMode mode = PasteMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var (plan, error) = await PrepareAsync(clip, PasteTarget.App, mode).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        if (await WriteAsync(plan!).ConfigureAwait(false) is { } failed)
        {
            return failed;
        }

        Touch(clip);
        return new PasteResult(PasteOutcome.CopiedOnly, "コピーしました。", ExportedFile: plan!.ExportedFile);
    }

    /// <summary>
    /// <paramref name="target"/> に貼り付ける。<paramref name="target"/> は、棚を開く前に前面にあったウインドウ。
    /// </summary>
    public async Task<PasteResult> PasteAsync(Clip clip, TargetWindow? target, PasteMode mode = PasteMode.Normal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (target is null || !target.Exists || target.ProcessId == (uint)Environment.ProcessId)
        {
            var copied = await CopyAsync(clip, mode).ConfigureAwait(false);
            return copied.Outcome == PasteOutcome.Failed
                ? copied
                : copied with { Message = "貼り付け先が見つからないので、コピーだけしました。Ctrl+V で貼り付けてください。" };
        }

        var name = target.DisplayName ?? target.ProcessName ?? "貼り付け先";
        var (plan, error) = await PrepareAsync(clip, target.Kind, mode).ConfigureAwait(false);
        if (error is not null)
        {
            return error with { TargetName = name };
        }

        if (await WriteAsync(plan!).ConfigureAwait(false) is { } failed)
        {
            return failed with { TargetName = name };
        }

        Touch(clip);
        PasteResult CopiedOnly(string message) => new(PasteOutcome.CopiedOnly, message, name, plan!.ExportedFile);

        if (!plan!.SendKeys)
        {
            return CopiedOnly(plan.Note ?? "コピーしました。");
        }

        // 管理者として動いているアプリには、キー操作が届かない（Windows が止める）
        if (!ProcessInfo.IsSelfElevated && ProcessInfo.IsElevated(target.ProcessId))
        {
            return CopiedOnly($"「{name}」は管理者として動いているので、自動では貼り付けられません。コピーは済んでいるので、Ctrl+V で貼り付けてください。");
        }

        if (!await BringToFrontAsync(target, cancellationToken).ConfigureAwait(false))
        {
            return CopiedOnly($"「{name}」を前に出せなかったので、コピーだけしました。Ctrl+V で貼り付けてください。");
        }

        if (!await InputSender.WaitForModifiersReleasedAsync(ModifierTimeout, cancellationToken).ConfigureAwait(false))
        {
            InputSender.ReleaseModifiers(InputSender.HeldModifiers());
        }

        if (!target.IsForeground())
        {
            return CopiedOnly("貼り付ける前に、ほかのウインドウに切り替わりました。コピーは済んでいるので、Ctrl+V で貼り付けてください。");
        }

        if (!InputSender.SendCtrlV())
        {
            return CopiedOnly("キー操作を送れませんでした。コピーは済んでいるので、Ctrl+V で貼り付けてください。");
        }

        return new PasteResult(PasteOutcome.Pasted, plan.Note ?? $"「{name}」に貼り付けました。", name, plan.ExportedFile);
    }

    private async Task<(PastePlan? Plan, PasteResult? Error)> PrepareAsync(Clip clip, PasteTarget target, PasteMode mode)
    {
        try
        {
            // 大きな画像は形を作るのに時間がかかる（4K で 0.3 秒ほど）ので、画面のスレッドでは作らない
            var plan = await Task.Run(() => PastePlanner.Plan(store, clip, target, mode)).ConfigureAwait(false);
            return plan.IsEmpty
                ? (null, new PasteResult(PasteOutcome.Failed, plan.Note ?? "貼り付けるものが見つかりませんでした。"))
                : (plan, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return (null, new PasteResult(PasteOutcome.Failed, "貼り付けの準備ができませんでした。", Detail: ex.Message));
        }
    }

    private async Task<PasteResult?> WriteAsync(PastePlan plan)
    {
        WriteStatus status;
        try
        {
            status = await watcher.WriteAsync(plan.Formats).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return new PasteResult(PasteOutcome.Failed, "クリップボードを使えませんでした。", Detail: ex.Message);
        }

        return status switch
        {
            WriteStatus.Ok => null,
            WriteStatus.Busy => new PasteResult(PasteOutcome.Failed, "ほかのアプリがクリップボードを使っていて、書き込めませんでした。少し待ってから、もう一度お試しください。"),
            WriteStatus.NotVerified => new PasteResult(PasteOutcome.Failed, "クリップボードに書き込めたか確かめられなかったので、貼り付けを止めました。もう一度お試しください。"),
            _ => new PasteResult(PasteOutcome.Failed, "クリップボードに書き込めませんでした。"),
        };
    }

    private void Touch(Clip clip)
    {
        if (!MoveToTop)
        {
            return;
        }

        try
        {
            store.Touch(clip.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 並べ替えを保存できなくても、貼り付けは続ける
            System.Diagnostics.Trace.WriteLine($"[Hikidana] 並べ替えを保存できませんでした: {ex.Message}");
        }
    }

    private static async Task<bool> BringToFrontAsync(TargetWindow target, CancellationToken cancellationToken)
    {
        if (target.IsForeground())
        {
            return true;
        }

        NativeMethods.SetForegroundWindow(target.Handle);
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(15, cancellationToken).ConfigureAwait(false);
            if (target.IsForeground())
            {
                // 前に出たばかりのウインドウが、キー操作を受け取れるようになるまで少し待つ
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

        return false;
    }
}
