using System.Collections.Specialized;
using System.IO;
using System.Windows;
using Hikidana.Core.Paste;

namespace Hikidana.App.Shelf;

/// <summary>ドラッグで渡すもの（<see cref="DragPlan"/>）を、Windows のドラッグの形（OLE の DataObject）にする。</summary>
internal static class DragData
{
    public static DataObject Create(DragPlan plan)
    {
        var data = new DataObject();
        if (plan.Files.Count > 0)
        {
            var files = new StringCollection();
            foreach (var file in plan.Files)
            {
                files.Add(file);
            }

            data.SetFileDropList(files);
        }

        if (plan.Text is { } text)
        {
            data.SetText(text, TextDataFormat.UnicodeText);
        }

        foreach (var format in plan.Formats)
        {
            // 中身はそのままのバイト列で渡す（HTML Format などを、WPF に作り直させない）
            data.SetData(format.Name, new MemoryStream(format.Data, writable: false));
        }

        return data;
    }

    /// <summary>落とす先に許すこと。いつもコピー（エクスプローラーへ落としても、元のファイルは動かさない）。</summary>
    public static DragDropEffects Effects(DragPlan plan) =>
        plan.AllowLink ? DragDropEffects.Copy | DragDropEffects.Link : DragDropEffects.Copy;
}
