# Hikidana 開発メモ（Claude 向け）

- 計画と決定事項は `docs/plan.md`。方針を変えたらそこも更新する。
- ユーザーとのやり取りは日本語。画面の文言も日本語。ユーザーは Git に詳しくないので、Git の操作は頼まない（ダウンロードは Actions の成果物か Releases から）。
- 見た目は案A「なじむ」（Windows 11 の Fluent、すりガラス、Windows のアクセント色）。デザイン案は claude.ai の Artifact「Hikidana デザイン案」。
- いちばんよく使う貼り付け先は ChatGPT と Claude（アプリとブラウザー。中身はどちらも Chromium）。

## 構成

- `src/Hikidana.Core`: クリップボードの中身の見分け方・保存・貼り付けの決まり（net10.0。画面にも Windows にも依存しない）。
- `src/Hikidana.Win32`: Windows の API を直接呼ぶ部分（クリップボードの読み書きと見張り、Ctrl+V の送信、貼り付けの流れ）。
  net10.0 にしてあるので作業環境でもビルドできる（動くのは Windows だけ。アセンブリに SupportedOSPlatform("windows")）。
- `src/Hikidana.App`: WPF のアプリ（net10.0-windows）。画面と Windows に固有の部分だけ。発行では .NET ごと 1 つの exe にまとめる。
  - `Shelf/ShelfController.cs`: 取っ手・棚・知らせのウインドウを動かす中心（開く・閉じる、置き場所、貼り付け、設定の保存）。
  - `Shelf/ShelfWindow.xaml`（棚とカード）、`HandleWindow.xaml`（端の取っ手）、`ToastWindow.xaml`（知らせ）。
    どれも WS_EX_NOACTIVATE でフォーカスを奪わず、置き場所は本当の画素で SetWindowPos する（`Interop/WindowHelper.cs`）。
  - `Theme/Palette.cs`: 色（案A）。`Hkd.` で始まる名前のブラシを入れ、XAML は DynamicResource で使う。
  - `Demo/DemoData.cs`: `--demo` の見本（一時フォルダーの別の棚）。CI の画面写真と、アプリを動かすテストで使う。
  - 置き場所の計算（`Core/Layout/ShelfLayout.cs`）、設定（`Core/Settings`）、記録（`Core/Diagnostics/Log.cs`）は Core にあり、手元でテストできる。
- `tests/Hikidana.Testing`: 小さなテストの仕組み（`[Fact]`、`Assert`、`TestRunner`）。NuGet を使わないので、作業環境でも動く。
  `[Fact(WindowsOnly = true)]` は Windows でだけ、`[Fact(Sta = true)]` は STA のスレッドで動く。
- `tests/Hikidana.Core.Tests`: Core のテスト。`dotnet run --project tests/Hikidana.Core.Tests -c Release`、
  絞り込みは末尾に `-- --filter 名前の一部`。
- `tests/Hikidana.Win32.Tests`: 本物のクリップボードとキー入力のテスト。Edge で開いたテストのページ
  （`Pages/paste-test.html`。チャットのアプリと同じく paste イベントで受け取り、中身をウインドウの題名に JSON で書く）と
  メモ帳に、本当に貼り付ける。Windows でだけ動く（作業環境ではビルドと「全部飛ばし」だけ確かめられる）。
  途中で環境が足りない（Edge が無いなど）とわかったら `SkipException` を投げる（失敗ではなく飛ばしになる）。

## ビルドと確認

- 作業環境には .NET 10 SDK がある（`apt install dotnet-sdk-10.0`）。nuget.org には接続できないので、
  NuGet のパッケージを使わないプロジェクト（Core とテスト）だけは手元でビルド・テストできる。WPF（App）は手元ではビルドできない。
- 変更は push し、GitHub Actions（`.github/workflows/ci.yml`）の結果で確かめる。
  - `gh run list --limit 5`、`gh run view <id> --log-failed`、`gh run view <id> --log`
  - CI のジョブ: 仕組みのテスト（Linux）、アプリのビルド（Windows。テスト、発行、本当に貼り付けるテスト、起動テストと画面写真つき）
- Actions の成果物（artifact）は作業環境からはダウンロードできない。画面写真は CI が `screenshots` ブランチに置くので、git で取る:
  `git fetch --depth 1 origin +refs/heads/screenshots:refs/remotes/origin/screenshots && git show origin/screenshots:startup-light.png > /tmp/startup-light.png`
  - `paste-*.png`: Windows の部分のテストで貼り付けた直後の画面。`failed-*.png`: 失敗したときの画面。
  - `app-*.png`: 発行したアプリで、取っ手から棚を開き、カードをクリックして Edge に貼った様子。
  - `startup-*.png`: 見本の中身で棚を開いた様子（ライト・ダーク・左の端・上の端・閉じた状態）。
- アプリのビルド結果は、利用者が Actions の成果物 `Hikidana-win-x64` からダウンロードする（GitHub へのログインが要る）。
- 配布: `v0.1.0` のようなタグを push するか、Actions の「Release」で「Run workflow」を押して版を入れると、
  `.github/workflows/release.yml` が zip を作って GitHub の Releases に置く
  （説明は `docs/releases/<版>.md`、無ければ `docs/releases/default.md`。「-」を含む版は先行版）。
  作業環境からはタグを push できない。出すときは利用者に「Run workflow」を押してもらう。
- 起動のときの指定: `--theme light|dark|system`、`--demo`（見本の中身。本物の棚と設定は使わない）、`--open`（棚を開いて始める）、
  `--edge right|left|top|bottom`、`--position 0〜1`（取っ手の位置）、`--data フォルダー`（しまう場所）。
- 発行したアプリを動かすテスト（`tests/Hikidana.Win32.Tests/AppTests.cs`）は、環境変数 `HIKIDANA_EXE` に exe の場所を入れたときだけ動く。

## 注意

- csproj などの XML のコメントに「--」を書かない（読み込みエラーになる）。XAML のコメントも同じ。
- XAML のパスのデータ（`Path.Data`）は、絶対座標の M・L・C・A・Z だけで書く。
- WPF のプロジェクトでは `System.IO.Path` と `System.Windows.Shapes.Path` がぶつかる。コードでは片方をフルネームで書く。
  また WPF のプロジェクトでは `System.IO` が暗黙の using に入らないので、`Path`・`File`・`IOException` を使うファイルには `using System.IO;` を書く。
- C# の文字列の `\uE718` のような書き方は、シェルのヒアドキュメントで書くと実際の文字に変わることがある。Python で書くか、書いたあとに確かめる
  （XAML では `&#xE718;` と書く）。

## 守ること

- 画像を貼るときは画像の形（PNG と DIB）だけを置き、ファイルの参照（CF_HDROP）を混ぜない。
  ファイルとして渡すのは、貼り付け先がエクスプローラーかデスクトップのときと、利用者が「ファイルとして貼り付け」を選んだときだけ。
- クリップボードに書いたら読み戻して確かめる。確かめられないときは Ctrl+V を送らず、そのことを知らせる。
- Ctrl+V は SendInput で送る。押されたままの Shift・Ctrl・Alt・Windows キーが離れるのを待ってから送る。
  相手が管理者として動いているときは送らない（コピーまでにして知らせる）。
- 棚は作業中のアプリからフォーカスを奪わない。キーボードで開いたときだけ例外で、閉じたら元のアプリに戻す。
- PowerShell やほかのプロセスに頼らない（Win32 API を直接呼ぶ）。
- 自分が書いたクリップボードの中身を、取り込み直さない。
