<!-- 作成：2026-11-26 目的：MDV の版ごとの変更履歴(日本語・英語)。新しい版を上に足していく -->

# 変更履歴 / Changelog

MDV の版ごとの主な変更です。新しい版が上にあります。日付は版番号を決めた日で、Microsoft Store での公開はその数日後になることがあります。1.0.0〜1.0.4 は GitHub Releases の自己署名版、1.0.5.0 以降は Microsoft Store 版です。

Notable changes in each version of MDV, newest first. Dates are when the version number was set; the Microsoft Store release may follow a few days later. Versions 1.0.0 to 1.0.4 were self-signed builds on GitHub Releases; 1.0.5.0 and later are Microsoft Store builds.

## 1.1.0.0 - 2026-11-26

- ページ内検索に対応しました（［表示］→［検索...］または Ctrl+F）。一致した箇所を強調して件数を表示し、Enter で次へ、Shift+Enter で前へ移動します（#2, #32）
- 検索欄が収まるよう、ウィンドウの最小幅を設けました（#32）

- Find in page is back (View > Find... or Ctrl+F). Matches are highlighted and counted; Enter moves to the next match and Shift+Enter to the previous one (#2, #32)
- The window now has a minimum width so the find box always fits (#32)

## 1.0.10.0 - 2026-11-25

- 1.0.9.0 で起動直後に終了してしまう問題を修正しました。パッケージ作成の誤りが原因で、コードの変更は版番号だけです（#31）

- Fixed MDV 1.0.9.0 closing right after it started. The cause was a packaging mistake; the only code change is the version number (#31)

## 1.0.9.0 - 2026-11-16

- 文書と同じフォルダとその下のフォルダにある画像を表示できるようになりました（相対パスで指定します）（#17, #28）
- 文書内のリンクに対応しました（ページ内の見出し、ほかの Markdown ファイル）。Web のリンクや文書のフォルダの外にある文書は、開く前に確認します（［表示］で切り替えられます）（#17, #29）
- 安全性を高めました。文書内のスクリプト、外部への通信、外部の画像を止めます（#17, #27）
- ［このアプリについて］の表示を改善しました（協力者の紹介文、ダイアログの幅とスクロール）（#30）

- Images in the document's folder and its subfolders are now shown (use relative paths) (#17, #28)
- Links inside documents now work (headings in the page and other Markdown files). MDV asks before opening web links and documents outside the document's folder (can be turned off in the View menu) (#17, #29)
- Improved security: scripts, network requests and remote images in documents are blocked (#17, #27)
- Improved the About dialog (contributor descriptions, dialog width and scrolling) (#30)

## 1.0.8.0 - 2026-11-01

- スペイン語（アルゼンチン）の表示に対応しました（#20, #22）
- ［このアプリについて］と README に協力者の欄を設けました（#23）
- テーマ切替ボタンを直し、メニューで選ばれている項目に印を付けるようにしました。文字の拡大・縮小のショートカットがどこでも効くようになりました（#16, #24）
- 見出しの ID を GitHub と同じ規則にし、日本語などの見出しへのリンクが切れないようにしました（#17, #25）

- Added Spanish (Argentina) UI (#20, #22)
- Added a contributors section to the About dialog and the README (#23)
- Fixed the theme button, added check marks to the selected menu items, and made the zoom shortcuts work everywhere (#16, #24)
- Heading IDs now follow GitHub's rules, so links to non-ASCII (e.g. Japanese) headings keep working (#17, #25)

## 1.0.7.0 - 2026-10-21

- UTF-8 以外のファイル（EUC-KR・GBK・Big5 など）を、利用者の地域の文字コードで読むようにしました（#15）
- 異常終了の記録をこの PC の中に残すようにしました（外部には送りません）。記録のフォルダは［このアプリについて］から開けます。ファイルを開けないときのエラー表示を改善し、エラーのダイアログをアプリのテーマに合わせました（#18）
- 表示言語は、利用者が変えたときだけ指定するようにしました（#14）

- Files that are not UTF-8 (such as EUC-KR, GBK and Big5) are now read with the user's regional code page (#15)
- Crash logs are now kept on this PC (never sent anywhere); the About dialog can open the log folder. Errors when opening files are handled better, and error dialogs follow the app theme (#18)
- The display language is now set only when the user changes it (#14)

## 1.0.6.0 - 2026-10-20

- 表示を多言語化し、英語と韓国語に対応しました。［表示］→［言語］で切り替えられます。翻訳の無い言語の環境では英語で表示します（#1, #7, #8, #10）
- ライトテーマでタイトルバーのボタンが見えない問題を修正しました（#12）

- Added UI localization with English and Korean, switchable from View > Language. Environments in other languages show English (#1, #7, #8, #10)
- Fixed the title bar buttons being invisible in the light theme (#12)

## 1.0.5.0 - 2026-07-13

- Microsoft Store での配布に切り替えました（#3, #4）
- Mermaid 図の描画を安定させました（#5）

- Moved distribution to the Microsoft Store (#3, #4)
- Made Mermaid diagram rendering more reliable (#5)

## 1.0.4 - 2026-07-08

- 印刷時に文字が薄いグレーになる問題を修正し、純黒で印刷するようにしました
- Windows App SDK を安定版の 1.8 に更新し、起動直後に落ちる環境があった問題を修正しました
- 署名証明書を作り直しました（自己署名版は証明書を信頼し直す必要があります）

- Text is now printed in pure black instead of light gray
- Updated the Windows App SDK to the stable 1.8 release, fixing a crash right after launch on some systems
- Regenerated the signing certificate (the self-signed build requires trusting the new certificate)

## 1.0.3 - 2026-07-01

- ［このアプリについて］ダイアログを追加しました（版・ライセンス）
- ［ファイル］→［印刷］を追加しました
- メニューにショートカットキーを追加しました（Ctrl+O / Ctrl+P / Ctrl++ / Ctrl+- / Ctrl+0 / F5 / F1 / Alt+F4）
- 右クリックメニューを閲覧向けに整理しました
- コードブロックにコピーボタンを追加しました
- この版から Ctrl+F（ページ内検索）が使えなくなりました（1.1.0.0 で再提供）

- Added the About dialog (version and licenses)
- Added File > Print
- Added keyboard shortcuts to the menus (Ctrl+O / Ctrl+P / Ctrl++ / Ctrl+- / Ctrl+0 / F5 / F1 / Alt+F4)
- Trimmed the context menu for reading
- Added a copy button to code blocks
- Ctrl+F (find in page) stopped working in this version (restored in 1.1.0.0)

## 1.0.2 - 2026-07-01

- .md / .markdown / .mdown / .mkd のファイルに専用のアイコンを付けました

- Added a dedicated file icon for .md / .markdown / .mdown / .mkd files

## 1.0.1 - 2026-07-01

- タイトルバーに「MDV — ファイル名」を表示するようにしました

- The title bar now shows "MDV — file name"

## 1.0.0 - 2026-07-01

- 最初の版です。GitHub スタイルの表示、コードのシンタックスハイライト、表・タスクリスト・画像・リンク、数式（KaTeX）、Mermaid 図、ライト/ダーク/システム連動のテーマ、文字サイズの変更、文字コード・改行コードの表示、3 つの開き方（メニュー・ドラッグ&ドロップ・ファイル関連付け）

- First release. GitHub-style rendering, syntax highlighting, tables, task lists, images and links, math (KaTeX), Mermaid diagrams, light/dark/system themes, text zoom, encoding and line-ending display, and three ways to open files (menu, drag and drop, file association)
