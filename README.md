# MDV

Windows 11 ネイティブの**閲覧専用 Markdown ビューア**です。英語・日本語・韓国語・スペイン語の UI に対応し、GitHub風のレンダリングを行います。

## 概要

MDV（Markdown Viewer）は、Markdown ファイルを開いて閲覧することに特化したデスクトップアプリです。編集機能は持たず、GitHub の Markdown プレビューに近い見た目・体験で `.md` ファイルを表示します。

## インストール

### Microsoft Store（推奨）

[Microsoft Store の MDV - Markdown Viewer](https://apps.microsoft.com/detail/9P45KBTKG49K) からインストールできます（Store で「**MDV - Markdown Viewer**」を検索しても見つかります）。Store 版は Microsoft によって署名・配布されるため、**証明書の手動信頼は不要**で、更新も自動的に行われます。

### GitHub Releases（自己署名版）

Microsoft Store への一本化に伴い、今後の新規配布は Store が中心になります。以下は自己署名版を手動でインストールする場合の手順です（旧バージョン向け）。

[Releases](https://github.com/kajiyajp/MDV/releases) から最新版をダウンロードしてください。

1. `MDV.App_x.x.x.x_x64.msix` と `MDV_signing.cer` をダウンロード
2. **管理者権限の PowerShell** で署名証明書を信頼済みにする（自己署名のため初回のみ必要）:

   ```powershell
   Import-Certificate -FilePath ".\MDV_signing.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

3. `MDV.App_x.x.x.x_x64.msix` をダブルクリックしてインストール

自己完結型パッケージのため、.NET や Windows App SDK ランタイムを別途入れる必要はありません。アンインストールは Windows の「アプリと機能」から行えます。

## 対応機能

- 表（テーブル）
- コードブロックのシンタックスハイライト
- タスクリスト（チェックボックス）
- 画像表示
- リンク（外部リンクは既定のブラウザで開く）
- LaTeX 数式（KaTeX によるレンダリング）
- Mermaid 図
- テーマ切替（ライト / ダーク / システム連動）
- 表示言語の切り替え（英語 / 日本語 / 韓国語 / スペイン語 / システム連動。[表示]→[言語]）。翻訳の無い言語の環境では英語で表示
- 文字サイズの拡大・縮小・リセット
- ステータスバー（左：文字エンコード、右：改行コード）
- 設定の永続化（テーマ・表示言語・文字サイズ・ステータスバー表示状態などを次回起動時に復元）
- バージョン情報（同梱しているサードパーティ製ソフトウェアのライセンスも表示）
- 3 つの開き方
  - メニューからファイルを選択
  - ファイルのドラッグ＆ドロップ
  - ファイル関連付け（エクスプローラーから `.md` 等をダブルクリック）

## 技術スタック

- C# / .NET 9
- WinUI 3（Windows App SDK）
- WebView2 + Markdig（Markdown → HTML 変換）
- フロントエンド資産（KaTeX / Mermaid / highlight.js / github-markdown-css）はすべてローカルに同梱（オフラインで動作）

## ビルド方法

```powershell
dotnet build MDV.sln
```

> ビルドには Visual Studio の「C++ によるデスクトップ開発」ワークロード（MSVC ツール）が必要です（WinUI 3 の内部ビルド工程で参照されます）。

### 開発時の実行

単一プロジェクト MSIX 構成のため、ターミナルからの実行が確実です:

```powershell
cd src/MDV.App
dotnet run -c Debug
```

`Microsoft.Windows.SDK.BuildTools.WinApp` がパッケージ ID を自動登録して起動するため、Visual Studio の MSIX パッケージ化ツールが無くても全機能（ファイル関連付け・設定の永続化を含む）が動作します。

> Store 版をインストールしている PC では、同じパッケージ ID のため `dotnet run` が「already installed as a non-development-mode package」で失敗します。Store 版をアンインストールするか、`Package.appxmanifest` の `Publisher` を一時的に自分の証明書の Subject に変えて自己署名 MSIX を作り、別アプリとして入れて確認してください（変更はコミットしないこと）。

### Core 層のテスト

```powershell
dotnet test tests/MDV.Core.Tests
```

### 配布用パッケージの作成

#### Microsoft Store 提出用（`.msixupload`・未署名）

Store 提出用パッケージは Microsoft が署名するため、**署名なし**で作成します。生成した `.msixupload` を Partner Center にアップロードします。

```powershell
msbuild src/MDV.App/MDV.App.csproj /restore `
  /p:Configuration=Release /p:Platform=x64 `
  /p:AppxBundle=Always /p:AppxBundlePlatforms=x64 `
  /p:UapAppxPackageBuildMode=StoreUpload `
  /p:AppxPackageSigningEnabled=false `
  /p:GenerateAppxPackageOnBuild=true
```

生成物: `src/MDV.App/AppPackages/MDV.App_<version>_x64_bundle.msixupload`

> `MDV.App.csproj` の `AppxBundleAutoResourcePackageQualifiers`（`Scale|DXFeatureLevel`）は消さないでください。無いとバンドル作成時に言語リソースが言語別パッケージへ分割され、Store 版で既定言語以外の翻訳が表示されなくなります。

> PowerShell で実行してください（Git Bash では `/p:` がパスとして誤変換されます）。

#### 自己署名 MSIX（サイドロード / ローカル検証用）

自己署名証明書で署名した自己完結型 MSIX を作成する例:

```powershell
msbuild src/MDV.App/MDV.App.csproj /restore `
  /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 `
  /p:WindowsAppSDKSelfContained=true /p:SelfContained=true `
  /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxBundle=Never `
  /p:GenerateAppxPackageOnBuild=true `
  /p:AppxPackageSigningEnabled=true /p:PackageCertificateThumbprint=<証明書のThumbprint> `
  /p:AppxPackageDir=<出力先>\
```

## 対応拡張子

- `.md`
- `.markdown`
- `.mdown`
- `.mkd`

## 対象OS

Windows 11 のみ

## 既知の制限

1. ファイルが外部で更新されても自動再読み込みは行いません（手動の「再読み込み」操作が必要です）。
2. テーマを動的に切り替えても、すでにレンダリング済みの Mermaid 図は再テーマされません（反映するには再読み込みが必要です）。
3. 文字サイズの拡大・縮小は WebView2 の CSS zoom によって実装しています（WinUI 3 の WebView2 はネイティブの `ZoomFactor` が公開されていないため）。
4. 表示言語の変更は、アプリの再起動後に反映されます。
5. 文書からの相対パスで指定したローカル画像は表示されず、ページ内リンクや他の Markdown ファイルへのリンクはクリックしても動作しません（[Issue #17](https://github.com/kajiyajp/MDV/issues/17)）。

## サンプル文書

`docs/sample.md` に、表・コード・タスクリスト・数式・Mermaid 図・外部リンクを網羅した検証用サンプルがあります。

## 開発・翻訳への協力

Issue やプルリクエストを送る前に、MDV の方針と手順をまとめた [CONTRIBUTING.md](CONTRIBUTING.md)（英語・日本語）をお読みください。

新しい言語の追加や翻訳の修正の手順は [docs/translations/README.md](docs/translations/README.md)（英語）を参照してください。コードやマニフェストを変更せずに言語を追加できます。

## 協力者

MDV の開発・翻訳に協力してくださった方々です。ありがとうございます。

- [Coldlapse](https://github.com/Coldlapse) — 英語・韓国語翻訳、文字コード対応などの修正
- [Alexis Gross](https://github.com/AlexisGross) — スペイン語翻訳

## ライセンス

本ソフトウェアは [MIT License](https://github.com/kajiyajp/MDV/blob/main/LICENSE) の下で公開されています。

同梱・利用しているサードパーティ製ソフトウェア（KaTeX / Mermaid / highlight.js / github-markdown-css / Markdig ほか）のライセンスは [THIRD-PARTY-NOTICES.md](https://github.com/kajiyajp/MDV/blob/main/THIRD-PARTY-NOTICES.md) を参照してください。
