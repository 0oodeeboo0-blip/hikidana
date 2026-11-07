# Hikidana 開発メモ（Claude 向け）

- 計画と決定事項は `docs/plan.md`。方針を変えたらそこも更新する。
- ユーザーとのやり取りは日本語。画面の文言も日本語。ユーザーは Git に詳しくないので、Git の操作は頼まない（ダウンロードは Actions の成果物か Releases から）。
- 見た目は案A「なじむ」（Windows 11 の Fluent、すりガラス、Windows のアクセント色）。デザイン案は claude.ai の Artifact「Hikidana デザイン案」。
- いちばんよく使う貼り付け先は ChatGPT と Claude（アプリとブラウザー。中身はどちらも Chromium）。

## 構成

- `src/Hikidana.Core`: クリップボードの中身の見分け方・保存・貼り付けの決まり（net10.0。画面にも Windows にも依存しない）。
- `src/Hikidana.App`: WPF のアプリ（net10.0-windows）。画面と Windows に固有の部分だけ。発行では .NET ごと 1 つの exe にまとめる。
- `tests/Hikidana.Testing`: 小さなテストの仕組み（`[Fact]`、`Assert`、`TestRunner`）。NuGet を使わないので、作業環境でも動く。
  `[Fact(WindowsOnly = true)]` は Windows でだけ、`[Fact(Sta = true)]` は STA のスレッドで動く。
- `tests/Hikidana.Core.Tests`: Core のテスト。`dotnet run --project tests/Hikidana.Core.Tests -c Release`、
  絞り込みは末尾に `-- --filter 名前の一部`。

## ビルドと確認

- 作業環境には .NET 10 SDK がある（`apt install dotnet-sdk-10.0`）。nuget.org には接続できないので、
  NuGet のパッケージを使わないプロジェクト（Core とテスト）だけは手元でビルド・テストできる。WPF（App）は手元ではビルドできない。
- 変更は push し、GitHub Actions（`.github/workflows/ci.yml`）の結果で確かめる。
  - `gh run list --limit 5`、`gh run view <id> --log-failed`、`gh run view <id> --log`
  - CI のジョブ: 仕組みのテスト（Linux）、アプリのビルド（Windows。テスト、発行、起動テストと画面写真つき）
- Actions の成果物（artifact）は作業環境からはダウンロードできない。画面写真は CI が `screenshots` ブランチに置くので、git で取る:
  `git fetch --depth 1 origin +refs/heads/screenshots:refs/remotes/origin/screenshots && git show origin/screenshots:startup-light.png > /tmp/startup-light.png`
  - `startup-light.png`、`startup-dark.png`: ふつうに起動した画面（画面全体）。
- アプリのビルド結果は、利用者が Actions の成果物 `Hikidana-win-x64` からダウンロードする（GitHub へのログインが要る）。
- 配布: `v0.1.0` のようなタグを push するか、Actions の「Release」で「Run workflow」を押して版を入れると、
  `.github/workflows/release.yml` が zip を作って GitHub の Releases に置く
  （説明は `docs/releases/<版>.md`、無ければ `docs/releases/default.md`。「-」を含む版は先行版）。
  作業環境からはタグを push できない。出すときは利用者に「Run workflow」を押してもらう。
- 起動のときの指定: `--theme light|dark|system`。

## 注意

- csproj などの XML のコメントに「--」を書かない（読み込みエラーになる）。XAML のコメントも同じ。
- XAML のパスのデータ（`Path.Data`）は、絶対座標の M・L・C・A・Z だけで書く。
- WPF のプロジェクトでは `System.IO.Path` と `System.Windows.Shapes.Path` がぶつかる。コードでは片方をフルネームで書く。

## 守ること

- 画像を貼るときは画像の形（PNG と DIB）だけを置き、ファイルの参照（CF_HDROP）を混ぜない。
  ファイルとして渡すのは、貼り付け先がエクスプローラーかデスクトップのときと、利用者が「ファイルとして貼り付け」を選んだときだけ。
- クリップボードに書いたら読み戻して確かめる。確かめられないときは Ctrl+V を送らず、そのことを知らせる。
- Ctrl+V は SendInput で送る。押されたままの Shift・Ctrl・Alt・Windows キーが離れるのを待ってから送る。
  相手が管理者として動いているときは送らない（コピーまでにして知らせる）。
- 棚は作業中のアプリからフォーカスを奪わない。キーボードで開いたときだけ例外で、閉じたら元のアプリに戻す。
- PowerShell やほかのプロセスに頼らない（Win32 API を直接呼ぶ）。
- 自分が書いたクリップボードの中身を、取り込み直さない。
