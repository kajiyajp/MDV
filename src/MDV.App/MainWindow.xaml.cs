using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using MDV.App.Services;
using MDV.Core.Models;
using MDV.Core.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MDV_App;

/// <summary>
/// The application window。メニューバー・WebView2本文・ステータスバーの骨組み。
/// </summary>
public sealed partial class MainWindow : Window
{
    private string? _currentPath;
    private ThemeMode _currentTheme = ThemeMode.System;
    private string _template = "";
    private bool _webReady;
    private CoreWebView2Environment? _webEnv;
    private (string Path, string Fragment)? _pendingFragment;   // リンクで開いた文書と、表示後にスクロールする見出し(OpenDocumentLink)
    private AppSettings _settings = new();
    // WinUI3のWebView2 XAMLコントロールはCoreWebView2Controller/ZoomFactorを公開していないため、
    // CSS(zoom)経由でWebView2側に倍率を適用し、現在値はこのフィールドで保持する
    private double _zoomFactor = 1.0;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // WebView2初期化(LoadSettings)より前に閉じられても、保存済みの表示言語を消さないよう先に読み込む
        try { _settings = SettingsService.Load(); } catch { _settings = new AppSettings(); }
        BuildLanguageMenu();

        // テーマ(「システムに合わせる」でのOS側の切替を含む)が変わるたびに、ウィンドウの背景とテーマ切替アイコンを更新する
        RootGrid.ActualThemeChanged += (s, e) => { ApplyWindowBackground(); UpdateThemeToggleIcon(); };
        ApplyWindowBackground();
        UpdateThemeToggleIcon();
        AddZoomAccelerators();
        // ページ内検索(#2)。Ctrl+F は「表示 > 検索」のショートカット(WebView2 内は template.html の keydown が通知する)
        LocalizeFindPanel();
        // ウィンドウを狭めても検索欄がはみ出さないよう、入力欄の幅を本文の幅に合わせて縮める
        ContentView.SizeChanged += (s, e) => FindBox.Width = Math.Clamp(e.NewSize.Width - 200, 160, 240);
        // ウィンドウの最小幅。これより狭いと検索欄(入力欄 160 + 件数・ボタン)が収まらない。
        // 最小幅は物理ピクセルで指定するため、表示倍率(125%・150% 等)を掛ける。画面をまたいで倍率が変わったら付け直す
        RootGrid.Loaded += (s, e) =>
        {
            ApplyMinimumWidth();
            RootGrid.XamlRoot.Changed += (r, a) => ApplyMinimumWidth();
        };

        this.Closed += (s, e) => PersistSettings();
    }

    // [表示]→[言語]: 「システムに合わせる」＋翻訳のある各言語(その言語自身の名称で表示)
    private void BuildLanguageMenu()
    {
        LanguageMenu.Items.Clear();
        var options = new List<(string Tag, string Label)> { ("", Localizer.Get("MenuLanguageSystem")) };
        options.AddRange(Localizer.AvailableLanguages().Select(tag => (tag, Localizer.NativeName(tag))));
        foreach (var (tag, label) in options)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = label,
                GroupName = "Language",
                IsChecked = string.Equals(tag, _settings.Language ?? "", StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (s, e) => OnLanguageSelected(tag);
            LanguageMenu.Items.Add(item);
        }
    }

    // 表示言語を保存し、再起動を促す（メニュー等のUI文字列は起動時に読み込まれるため、反映には再起動が必要）
    private async void OnLanguageSelected(string tag)
    {
        if (string.Equals(tag, _settings.Language ?? "", StringComparison.OrdinalIgnoreCase)) return;
        _settings.Language = tag;
        PersistSettings();
        Localizer.SetLanguage(tag);

        var dialog = new ContentDialog
        {
            Title = Localizer.Get("LanguageRestartTitle"),
            Content = Localizer.Get("LanguageRestartMessage"),
            PrimaryButtonText = Localizer.Get("LanguageRestartNow"),
            CloseButtonText = Localizer.Get("LanguageRestartLater"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.Content.XamlRoot,
            RequestedTheme = (this.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        PersistSettings();
        // 開いていたファイルは再起動後にもう一度開く（App.OnLaunched がコマンドライン引数を読む）
        var args = _currentPath != null ? $"\"{_currentPath}\"" : "";
        Microsoft.Windows.AppLifecycle.AppInstance.Restart(args);
    }

    // WebView2を初回利用時に1回だけ初期化し、vendorフォルダを仮想ホストにマッピングする
    private async Task EnsureWebViewAsync()
    {
        if (_webReady) return;

        EncodingDetector.RegisterProviders();
        // WebView2の既定UI(右クリックメニュー等)をアプリの表示言語に合わせるため、言語を指定した環境で初期化する
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, null,
            new CoreWebView2EnvironmentOptions { Language = Localizer.Get("WebViewLanguage") });
        _webEnv = env;   // Aboutダイアログのライセンス表示でも同じ環境を使う
        // WebView2自体の既定背景は透明にし、ページの読み込み前はウィンドウの背景(=文書の背景色)が見えるようにする。
        // 初期化前に設定しないと、読み込み中に既定の白/黒が一瞬見えてしまう
        ContentView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
        await ContentView.EnsureCoreWebView2Async(env);

        var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
        // http://vendor/... を Assets/vendor フォルダにマッピング
        ContentView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "vendor", Path.Combine(assetsDir, "vendor"),
            CoreWebView2HostResourceAccessKind.Allow);
        // http://document/... は文書のフォルダとその下にある画像だけを返す(DocumentImages。#17)。
        // フォルダのマッピングは使わない(画像以外のファイルも読めるようになるため)
        ContentView.CoreWebView2.AddWebResourceRequestedFilter(DocumentImages.BaseUri + "*", CoreWebView2WebResourceContext.All);
        // CSP の http://document は https://document も許すので、こちらも受けて 404 を返す(受けないとホスト名の名前解決が外へ出る)。
        // https://vendor はフォルダのマッピングが受けるので外へは出ない
        ContentView.CoreWebView2.AddWebResourceRequestedFilter("https://" + DocumentImages.Host + "/*", CoreWebView2WebResourceContext.All);
        ContentView.CoreWebView2.WebResourceRequested += OnDocumentResourceRequested;
        // リンク先の文書を開いたあと、指定の見出しまでスクロールする
        ContentView.CoreWebView2.NavigationCompleted += OnContentNavigationCompleted;

        _template = LocalizeTemplate(File.ReadAllText(Path.Combine(assetsDir, "template.html")));

        OpenLinksExternally(ContentView.CoreWebView2, confirm: true);

        // 右クリックの既定メニューを閲覧向けに整理する（保存/検証などを除去し、コピー・印刷・再読み込みを残す）
        ContentView.CoreWebView2.ContextMenuRequested += OnContextMenuRequested;

        // ブラウザ既定のショートカット(Ctrl+P/F5/Ctrl+F/Ctrl+±等)を無効化し、アプリのメニュー動作に一本化する
        ContentView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
        // WebView2内でのショートカット押下(template.htmlのkeydown→postMessage)をホストで受ける
        ContentView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        _webReady = true;

        LoadSettings();
    }

    // template.html 内のUI文字列プレースホルダ(コピーボタン)を表示言語の文字列に置き換える。
    // JSの文字列リテラルとして埋め込むため、JSONエンコード(引用符・</script>等をエスケープ)して渡す。
    // JsonSerializer はRelease構成(リフレクション無効)で例外になるため、JsonEncodedText を使う。
    // 本文(Markdown)を埋め込む前のテンプレートに対して行うので、本文中の同じ文字列は置換されない。
    private static string LocalizeTemplate(string template) => template
        .Replace("{{COPY_LABEL}}", ToJsString(Localizer.Get("CopyButton")))
        .Replace("{{COPIED_LABEL}}", ToJsString(Localizer.Get("CopyButtonDone")));

    private static string ToJsString(string value) =>
        "\"" + System.Text.Json.JsonEncodedText.Encode(value).ToString() + "\"";

    // 外部リンク(http/https)はアプリ内WebView2で遷移させず、既定ブラウザで開く。
    // 文書の表示(confirm: true)では、開く前に確認する(ConfirmExternalAsync)。About のライセンス表示は同梱の文書なので確認しない
    private void OpenLinksExternally(CoreWebView2 core, bool confirm)
    {
        async void Launch(string uri)
        {
            if (!confirm || await ConfirmExternalAsync(uri)) LaunchExternal(uri);
        }
        core.NewWindowRequested += (s, e) =>
        {
            e.Handled = true;
            if (!DocumentImages.IsDocumentUri(e.Uri)) Launch(e.Uri);
        };
        core.NavigationStarting += (s, e) =>
        {
            // 「戻る」「進む」(マウスのボタンなど)による移動は取り消す。MDV には履歴の機能がなく、戻ると
            // 表示中のページと、ホスト側の文書(_currentPath:タイトルやリンクの基準フォルダ)がずれるため
            if (e.NavigationKind == CoreWebView2NavigationKind.BackOrForward)
            {
                e.Cancel = true;
                return;
            }
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https") &&
                uri.Host != "vendor")            // 同梱資産(http://vendor/...)の取得は遮らない
            {
                e.Cancel = true;
                // 文書の相対リンク(http://document/...)はブラウザに渡さず、ページも移動しない。
                // (画像を直接ページとして開くと SVG のスクリプトが動きうるため。文書内リンクへの対応は別に行う)
                if (!DocumentImages.IsDocumentUri(e.Uri)) Launch(e.Uri);
            }
        };
    }

    private async void OpenExternalLink(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https") ||
            DocumentImages.IsDocumentUri(uri) || u.Host == "vendor")
            return;
        if (await ConfirmExternalAsync(u.AbsoluteUri)) LaunchExternal(u.AbsoluteUri);
    }

    // 外部のリンク・文書を開く前に、開く先を見せて確認する。「今後は確認しない」を選んで開くと、
    // 設定(ConfirmExternalOpen)を切る([表示]メニューの項目から元に戻せる)。確認が切れていれば何も聞かずに true
    private bool _confirmShowing;
    private async Task<bool> ConfirmExternalAsync(string target)
    {
        if (!_settings.ConfirmExternalOpen) return true;
        if (_confirmShowing) return false;   // 確認中にもう一度押されたものは開かない(ダイアログは同時に 1 つしか出せない)

        var dontAsk = new CheckBox { Content = Localizer.Get("ExternalOpenDontAsk") };
        var panel = new StackPanel { Spacing = 8, MaxWidth = 480 };
        panel.Children.Add(new TextBlock { Text = Localizer.Get("ExternalOpenTarget") });
        panel.Children.Add(new TextBlock
        {
            Text = target, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas")
        });
        panel.Children.Add(dontAsk);
        panel.Children.Add(new TextBlock { Text = Localizer.Get("ExternalOpenSettingHint"), FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            Title = Localizer.Get("ExternalOpenTitle"),
            Content = panel,
            PrimaryButtonText = Localizer.Get("ExternalOpenOpen"),
            CloseButtonText = Localizer.Get("ExternalOpenCancel"),
            DefaultButton = ContentDialogButton.Close,   // Enter を押しただけでは開かない
            XamlRoot = this.Content.XamlRoot,
            RequestedTheme = (this.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default
        };

        ContentDialogResult result;
        _confirmShowing = true;
        try { result = await dialog.ShowAsync(); }
        catch { return false; }   // 他のダイアログ(About など)を表示中
        finally { _confirmShowing = false; }

        if (result != ContentDialogResult.Primary) return false;
        if (dontAsk.IsChecked == true)
        {
            _settings.ConfirmExternalOpen = false;
            ConfirmExternalItem.IsChecked = false;
            PersistSettings();
        }
        return true;
    }

    // [表示]→[外部の文書・リンクを開く前に確認する]
    private void OnToggleConfirmExternal(object s, RoutedEventArgs e)
    {
        _settings.ConfirmExternalOpen = ConfirmExternalItem.IsChecked;
        PersistSettings();
    }

    // 文書内の相対パスのリンクを開く(#17)。Markdown ファイルなら MDV で開き、# の後ろの見出しまでスクロールする。
    // それ以外(画像・フォルダ・実行ファイル・存在しないファイルなど)は何もしない
    private async void OpenDocumentLink(string href)
    {
        if (_currentPath == null) return;
        var target = DocumentLinks.Resolve(Path.GetDirectoryName(_currentPath)!, href);
        if (target is not { } link) return;

        if (string.Equals(Path.GetFullPath(_currentPath), link.Path, StringComparison.OrdinalIgnoreCase))
        {
            // 同じ文書への「ファイル名#見出し」は、読み直さずにスクロールだけする
            if (link.Fragment != null) await ScrollToFragmentAsync(link.Fragment);
            return;
        }
        // 文書のフォルダの外にある文書は、開く前に確認する
        if (!DocumentLinks.IsInsideFolder(Path.GetDirectoryName(_currentPath)!, link.Path) &&
            !await ConfirmExternalAsync(link.Path))
            return;
        // 表示し終えたら OnContentNavigationCompleted がスクロールする(開けなかったときは、そのファイルではないのでスクロールしない)
        _pendingFragment = link.Fragment != null ? (link.Path, link.Fragment) : null;
        await OpenFileAsync(link.Path);
    }

    private async Task ScrollToFragmentAsync(string fragment)
    {
        try { await ContentView.CoreWebView2.ExecuteScriptAsync($"window.__scrollToFragment && window.__scrollToFragment({ToJsString(fragment)})"); }
        catch { /* スクロールできなくても文書は読める */ }
    }

    private async void OnContentNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var pending = _pendingFragment;
        _pendingFragment = null;
        if (pending is { } p && e.IsSuccess && _currentPath != null &&
            string.Equals(Path.GetFullPath(_currentPath), p.Path, StringComparison.OrdinalIgnoreCase))
            await ScrollToFragmentAsync(p.Fragment);
    }

    // http://document/... の要求に、文書フォルダの画像だけを返す。それ以外は 404(中身なし)
    private async void OnDocumentResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var uri = e.Request.Uri;
            var folder = _currentPath != null ? Path.GetDirectoryName(_currentPath) : null;
            // 大きな画像でも画面を止めないよう、読み込みはバックグラウンドで行う
            var image = folder != null ? await Task.Run(() => DocumentImages.Read(folder, uri)) : null;
            e.Response = image is { } found
                ? _webEnv!.CreateWebResourceResponse(new MemoryStream(found.Data).AsRandomAccessStream(), 200, "OK",
                    $"Content-Type: {found.ContentType}\r\nX-Content-Type-Options: nosniff")
                : _webEnv!.CreateWebResourceResponse(null, 404, "Not Found", "");
        }
        catch
        {
            // 応答を作れなかったときは何も返さない(既定の処理に任せると、要求が外へ出ていく可能性がある)
            try { e.Response = _webEnv!.CreateWebResourceResponse(null, 404, "Not Found", ""); } catch { }
        }
        finally
        {
            deferral.Complete();
        }
    }

    // 外部URIを既定のブラウザ（既定アプリ）で開く
    private static async void LaunchExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u))
            await Windows.System.Launcher.LaunchUriAsync(u);
    }

    // WebView2の右クリックメニューを閲覧向けに整理する
    private void OnContextMenuRequested(CoreWebView2 sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        // 閲覧に必要な既定項目のみ残す（コピー・切り取り・貼り付け・全選択・印刷）。
        // 名前(Name)は言語非依存の識別子のため、日本語表示でもこの判定は有効。
        var keep = new System.Collections.Generic.HashSet<string>
        {
            "copy", "cut", "paste", "pasteAndMatchStyle", "selectAll", "print"
        };
        for (int i = e.MenuItems.Count - 1; i >= 0; i--)
        {
            var item = e.MenuItems[i];
            if (item.Kind == CoreWebView2ContextMenuItemKind.Separator || !keep.Contains(item.Name))
                e.MenuItems.RemoveAt(i);
        }

        // 「再読み込み」はカスタム項目として追加し、ファイルを再読込する。
        // ※WebView2既定のReloadはNavigateToStringページを空白化してしまうため使わない。
        if (_currentPath != null)
        {
            if (e.MenuItems.Count > 0)
                e.MenuItems.Add(sender.Environment.CreateContextMenuItem(
                    "", null, CoreWebView2ContextMenuItemKind.Separator));

            var reload = sender.Environment.CreateContextMenuItem(
                Localizer.Get("ContextMenuReload"), null, CoreWebView2ContextMenuItemKind.Command);
            reload.CustomItemSelected += (s2, a2) =>
                DispatcherQueue.TryEnqueue(async () =>
                {
                    if (_currentPath != null) await OpenFileAsync(_currentPath);
                });
            e.MenuItems.Add(reload);
        }
    }

    // WebView2内で押されたショートカット(template.htmlから通知)を対応するメニュー動作へ振り分ける
    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string msg;
        try { msg = e.TryGetWebMessageAsString(); }
        catch { return; }
        // コードブロックの「コピー」ボタン: 受け取った本文をクリップボードへ設定する
        if (msg != null && msg.StartsWith("copy:"))
        {
            var pkg = new DataPackage();
            pkg.SetText(msg.Substring(5));
            Clipboard.SetContent(pkg);
            return;
        }
        // 文書内の相対パスのリンク(template.html のクリック処理から)
        if (msg != null && msg.StartsWith("link:"))
        {
            OpenDocumentLink(msg.Substring(5));
            return;
        }
        // 文書内の http(s) のリンク(template.html のクリック処理から)。確認してから既定のブラウザで開く
        if (msg != null && msg.StartsWith("external:"))
        {
            OpenExternalLink(msg.Substring(9));
            return;
        }
        switch (msg)
        {
            case "open": OnOpenClick(this, null!); break;
            case "print": OnPrint(this, null!); break;
            case "zoomIn": OnZoomIn(this, null!); break;
            case "zoomOut": OnZoomOut(this, null!); break;
            case "zoomReset": OnZoomReset(this, null!); break;
            case "reload": OnReload(this, null!); break;
            case "about": OnAboutClick(this, null!); break;
            case "find": ShowFindPanel(); break;
        }
    }

    // ---- ページ内検索(#2): 検索欄は WinUI、強調・件数・移動は WebView2 の Find API ----
    // 標準の検索バー(SuppressDefaultFindDialog=false)は別ウィンドウで、WinUI の WebView2 では
    // ウィンドウの移動に付いてこず、動かすと入力もできなくなるため使わない(試作で確認)。
    // セキュリティ: 検索語は FindOptions.FindTerm に渡すだけ。JS に埋め込んだり ExecuteScriptAsync で渡したりしない

    private bool _findEventsHooked;

    // 表示 > 検索(Ctrl+F)
    private void OnFindClick(object sender, RoutedEventArgs e) => ShowFindPanel();

    // 検索欄の案内文・ボタンの説明を表示言語の文字列にする
    private void LocalizeFindPanel()
    {
        FindBox.PlaceholderText = Localizer.Get("FindPlaceholder");
        SetFindButtonText(FindPrevButton, "FindPrevious");
        SetFindButtonText(FindNextButton, "FindNext");
        SetFindButtonText(FindCloseButton, "FindClose");
    }

    // ボタンのツールチップと読み上げ名を同じ文字列にする
    private static void SetFindButtonText(Button button, string key)
    {
        var text = Localizer.Get(key);
        ToolTipService.SetToolTip(button, text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
    }

    // Ctrl+F: 検索欄を出して入力欄にフォーカスを置く。前回の語が残っていれば全選択して検索し直す
    private void ShowFindPanel()
    {
        if (!_webReady) return;
        HookFindEvents();
        // 開いている間に Ctrl+F を押し直したときは検索し直さない(いまの一致位置が次へ進んでしまうため)
        var wasOpen = FindPanel.Visibility == Visibility.Visible;
        FindPanel.Visibility = Visibility.Visible;
        FindBox.Focus(FocusState.Programmatic);
        FindBox.SelectAll();
        if (!wasOpen && FindBox.Text.Length > 0) _ = RunFindAsync();
    }

    // 件数が変わったら表示を更新する(初回の Ctrl+F で 1 回だけ登録する)
    private void HookFindEvents()
    {
        if (_findEventsHooked) return;
        try
        {
            var find = ContentView.CoreWebView2.Find;
            find.MatchCountChanged += (s, e) => DispatcherQueue.TryEnqueue(UpdateFindCount);
            find.ActiveMatchIndexChanged += (s, e) => DispatcherQueue.TryEnqueue(UpdateFindCount);
            _findEventsHooked = true;
        }
        catch (Exception ex) { CrashReporter.Record(ex, "HookFindEvents"); }
    }

    // 入力が変わるたびに検索する(空なら検索を終えて強調を消す)
    private void OnFindTextChanged(object sender, TextChangedEventArgs e) => _ = RunFindAsync();

    // 最後に WebView2 で検索した語と、それが 0 件だったか(0 件の音を減らすため)
    private string _searchedTerm = "";
    private bool _searchedZero;

    // いまの入力で検索する。
    // 0 件のとき WebView2 が既定のビープを鳴らす(止める設定は無い)ため、0 件だった語に文字を足しただけの入力は
    // 検索し直さない(部分一致なので足しても 0 件のまま)。これで音は 0 件になった瞬間の 1 回だけになる
    private async Task RunFindAsync()
    {
        var term = FindBox.Text;
        try
        {
            var core = ContentView.CoreWebView2;
            if (term.Length == 0)
            {
                core.Find.Stop();
                ResetFindState();
                return;
            }
            if (_searchedZero && _searchedTerm.Length > 0 && term.StartsWith(_searchedTerm, StringComparison.OrdinalIgnoreCase))
            {
                FindCount.Text = "0/0";
                return;
            }
            _searchedTerm = term;
            _searchedZero = false;
            var options = core.Environment.CreateFindOptions();
            options.FindTerm = term;
            options.IsCaseSensitive = false;
            options.ShouldHighlightAllMatches = true;
            options.SuppressDefaultFindDialog = true;
            await core.Find.StartAsync(options);
            UpdateFindCount();
        }
        catch (Exception ex)
        {
            // 古いランタイム等で Find API が使えないときは何もしない(落とさない)
            CrashReporter.Record(ex, "RunFindAsync");
        }
    }

    // 件数を「いま何件目/全件」で出す。入力が空なら何も出さない
    private void UpdateFindCount()
    {
        if (FindBox.Text.Length == 0) { FindCount.Text = ""; return; }
        try
        {
            var find = ContentView.CoreWebView2.Find;
            _searchedZero = find.MatchCount == 0;
            if (FindBox.Text != _searchedTerm)
            {
                // 0 件と見て検索を省いた後に件数が増えた(数え終わる前の 0 だった)ときは、いまの入力で検索し直す
                if (!_searchedZero) _ = RunFindAsync(); else FindCount.Text = "0/0";
                return;
            }
            FindCount.Text = $"{Math.Max(find.ActiveMatchIndex, 0)}/{find.MatchCount}";
        }
        catch { FindCount.Text = ""; }
    }

    // ウィンドウの最小幅(論理 400px)を、いまの表示倍率で物理ピクセルにして設定する
    private void ApplyMinimumWidth()
    {
        if (this.AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter) return;
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        presenter.PreferredMinimumWidth = (int)Math.Ceiling(400 * scale);
    }

    // 検索の状態(最後に検索した語・0 件か・件数の表示)を初めに戻す
    private void ResetFindState()
    {
        _searchedTerm = "";
        _searchedZero = false;
        FindCount.Text = "";
    }

    // 入力欄のキー: Enter=次へ、Shift+Enter=前へ、Esc=閉じる
    private void OnFindKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (shift) FindPrevious(); else FindNext();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CloseFindPanel();
            e.Handled = true;
        }
    }

    private void OnFindPrevClick(object sender, RoutedEventArgs e) => FindPrevious();
    private void OnFindNextClick(object sender, RoutedEventArgs e) => FindNext();
    private void OnFindCloseClick(object sender, RoutedEventArgs e) => CloseFindPanel();

    private void FindNext()
    {
        if (FindBox.Text.Length == 0) return;
        try { ContentView.CoreWebView2.Find.FindNext(); } catch (Exception ex) { CrashReporter.Record(ex, "FindNext"); }
    }

    private void FindPrevious()
    {
        if (FindBox.Text.Length == 0) return;
        try { ContentView.CoreWebView2.Find.FindPrevious(); } catch (Exception ex) { CrashReporter.Record(ex, "FindPrevious"); }
    }

    // 検索欄を閉じて強調を消し、本文にフォーカスを戻す(入力した語は次に開いたときのために残す)
    private void CloseFindPanel()
    {
        StopFind();
        ContentView.Focus(FocusState.Programmatic);
    }

    // 別の文書を開く前に検索を終え、検索欄を閉じる(前の文書の件数や強調を残さない)
    private void StopFind()
    {
        try { ContentView.CoreWebView2?.Find.Stop(); } catch { /* 使えない環境では無視 */ }
        FindPanel.Visibility = Visibility.Collapsed;
        ResetFindState();
    }

    // [ファイル]→[印刷]: 印刷プレビュー(ダイアログ)を表示する。
    // WinUIのWebView2射影は ShowPrintUI を公開していないため、JSの window.print() を用いる。
    private async void OnPrint(object s, RoutedEventArgs e)
    {
        if (_webReady)
            try { await ContentView.CoreWebView2.ExecuteScriptAsync("window.print()"); }
            catch { /* 印刷UIの表示失敗は無視 */ }
    }

    // 文字サイズ(ズーム)をWebView2へ適用する
    // WinUI3のWebView2 XAMLコントロールはCoreWebView2Controller(ZoomFactor)を公開していないため、
    // CSS(zoom)をJS経由で適用する（template.html側のwindow.__setZoomフック）
    private async void ApplyZoom(double factor)
    {
        _zoomFactor = factor;
        if (_webReady)
            try
            {
                await ContentView.CoreWebView2.ExecuteScriptAsync(
                    $"window.__setZoom && window.__setZoom('{_zoomFactor.ToString(System.Globalization.CultureInfo.InvariantCulture)}')");
            }
            catch
            {
                // WebView2が利用不可などのスクリプト実行失敗は無視（テーマ/ズームの見た目のみの影響）
            }
    }

    // 保存済み設定を読み込み、各UIへ適用する（読み込み時は永続化しない）
    private void LoadSettings()
    {
        try { _settings = SettingsService.Load(); } catch { _settings = new AppSettings(); }
        ApplyZoom(_settings.ZoomFactor);
        StatusBar.Visibility = _settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        StatusBarItem.IsChecked = _settings.ShowStatusBar;
        ConfirmExternalItem.IsChecked = _settings.ConfirmExternalOpen;
        SetTheme(_settings.Theme);   // テーマを適用する（永続化はしない）
    }

    // 現在のテーマ・文字サイズ・ステータスバー表示状態を設定として保存する
    private void PersistSettings()
    {
        _settings.Theme = _currentTheme;
        _settings.ZoomFactor = _zoomFactor;
        _settings.ShowStatusBar = StatusBar.Visibility == Visibility.Visible;
        try { SettingsService.Save(_settings); } catch { /* 保存失敗は無視 */ }
    }

    // ファイルを読み込み、MarkdownをHTMLに変換してWebView2に表示し、ステータスバーを更新する
    // WebView2の初期化を含め、ここで起きた例外はダイアログで知らせてログに残す(アプリを落とさない)
    public async Task OpenFileAsync(string path)
    {
        try
        {
            await EnsureWebViewAsync();
            var doc = FileService.Load(path);
            _currentPath = path;

            // 相対パスの画像は http://document/ から読む(OnDocumentResourceRequested が文書フォルダの画像だけを返す)
            var html = MarkdownRenderer.BuildHtml(doc.Text, _currentTheme, _template, DocumentImages.BaseUri);
            StopFind();
            ContentView.CoreWebView2.NavigateToString(html);
            // ナビゲーションでCSS状態がリセットされるため、文字サイズを再適用する
            ApplyZoom(_zoomFactor);

            EncodingText.Text = doc.EncodingDisplay;
            // 「混在」だけは言語依存の語のため、表示言語の文字列に差し替える（CRLF/LF/CRは共通表記）
            LineEndingText.Text = doc.LineEnding == LineEndingKind.Mixed
                ? Localizer.Get("LineEndingMixed")
                : LineEndingDetector.ToDisplay(doc.LineEnding);
            // ウィンドウ名(タスクバー)と可視タイトルバーの両方を更新する
            var title = $"MDV — {Path.GetFileName(path)}";
            this.Title = title;
            AppTitleBar.Title = title;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex, "OpenFile");
        }
    }

    // 起動時パス（コマンドライン引数/ファイル関連付け）を受け取り、指定があれば開く
    public async Task OpenInitialAsync(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            await OpenFileAsync(path!);   // 例外は OpenFileAsync 内で処理される
            return;
        }
        try
        {
            await EnsureWebViewAsync();
            ShowEmptyDocument();
        }
        catch (Exception ex)
        {
            // ファイルを指定しない起動でWebView2を初期化できなかったとき
            await ShowErrorAsync(ex, "OpenInitial", "ViewerInitErrorTitle", "ViewerInitErrorMessage");
        }
    }

    // ファイルを開いていないときも空の文書ページを表示する。
    // 何も表示しないとWebView2既定の灰色(ダーク#202020/ライト#F3F3F3)が見え、ウィンドウの背景色と揃わないため。
    // (ページ内のショートカット Ctrl+O 等もこのページで有効になる)
    private void ShowEmptyDocument()
    {
        try
        {
            var baseUri = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets") + Path.DirectorySeparatorChar).AbsoluteUri;
            ContentView.CoreWebView2.NavigateToString(MarkdownRenderer.BuildHtml("", _currentTheme, _template, baseUri));
            ApplyZoom(_zoomFactor);
        }
        catch
        {
            // WebView2が利用できない場合は既定の表示のまま
        }
    }

    // ファイルを開く系のエラーを端末内ログに記録し、ダイアログで通知する
    private async Task ShowErrorAsync(Exception ex, string source,
        string titleKey = "OpenErrorTitle", string messageKey = "OpenErrorGeneric")
    {
        CrashReporter.Record(ex, source);
        try
        {
            var dialog = new ContentDialog
            {
                Title = Localizer.Get(titleKey),
                Content = ex is FileNotFoundException
                    ? Localizer.Get("OpenErrorNotFound")
                    : $"{Localizer.Get(messageKey)}\n{ex.Message}",
                CloseButtonText = Localizer.Get("DialogClose"),
                XamlRoot = this.Content.XamlRoot,
                // Windowsのテーマではなくアプリのテーマに合わせる(Issue #16 の2。About・言語のダイアログと同じ)
                RequestedTheme = (this.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default
            };
            await dialog.ShowAsync();
        }
        catch (Exception dialogEx)
        {
            // 別のダイアログを表示中などでダイアログを出せないときは、記録だけ残す
            CrashReporter.Record(dialogEx, source + ".ShowError");
        }
    }

    // [ヘルプ]→[このアプリについて]: アプリ情報とサードパーティライセンスをダイアログ表示する
    private async void OnAboutClick(object s, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = Localizer.Get("AboutTitle"),
            Content = BuildAboutContent(),
            CloseButtonText = Localizer.Get("DialogClose"),
            XamlRoot = this.Content.XamlRoot,
            // ダイアログをウィンドウの現在テーマに合わせる（ライト/ダーク両対応）
            RequestedTheme = (this.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default
        };
        // 既定の最大幅(548px)では協力者欄やライセンス表が窮屈なので、少し広げる
        dialog.Resources["ContentDialogMaxWidth"] = 580.0;
        await dialog.ShowAsync();
    }

    // Aboutダイアログの中身を組み立てる（アプリ名・版・著作権・MIT・サードパーティ一覧・コントリビューター）
    private FrameworkElement BuildAboutContent()
    {
        // アプリ名・バージョンをパッケージ情報から動的取得（非パッケージ実行時はアセンブリ情報へフォールバック）
        string appName = "MDV";
        string version = "1.0.0.0";
        try
        {
            var pkg = Windows.ApplicationModel.Package.Current;
            appName = string.IsNullOrWhiteSpace(pkg.DisplayName) ? "MDV" : pkg.DisplayName;
            var v = pkg.Id.Version;
            version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
        catch
        {
            var asmVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (asmVersion != null) version = asmVersion.ToString();
        }

        // サードパーティ表記を Assets から読み込む（ビルド時にコピー済み。無ければメッセージを出す）
        string? notices;
        try
        {
            notices = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "THIRD-PARTY-NOTICES.md"));
        }
        catch
        {
            notices = null;
        }

        // 右に余白を取り、ダイアログ全体の縦スクロールバーが文字やライセンス表に重ならないようにする
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 16, 0) };

        panel.Children.Add(new TextBlock
        {
            Text = string.Format(Localizer.Get("AboutVersion"), appName, version),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 18
        });
        panel.Children.Add(new TextBlock
        {
            Text = Localizer.Get("AboutDescription"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8
        });
        panel.Children.Add(new TextBlock { Text = "Copyright (c) 2026 kajiyajp", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = Localizer.Get("AboutLicense"),
            TextWrapping = TextWrapping.Wrap
        });

        // GitHubリンク（クリックで既定ブラウザが開く。オフラインでも下のライセンス本文は読める）。
        // 注: WinUI3デスクトップでは HyperlinkButton.NavigateUri が自動でブラウザを開かないため、
        //     アプリ本体と同じ Launcher.LaunchUriAsync 経由(LaunchExternal)で明示的に開く。
        var githubLink = new HyperlinkButton { Content = Localizer.Get("AboutGitHubLink"), Padding = new Thickness(0) };
        githubLink.Click += (s, e) => LaunchExternal("https://github.com/kajiyajp/MDV");
        panel.Children.Add(githubLink);

        // 端末内のエラー記録(送信はしない)を利用者が確認できるよう、ログフォルダを開くリンクを置く
        var logLink = new HyperlinkButton { Content = Localizer.Get("AboutOpenLogFolder"), Padding = new Thickness(0) };
        logLink.Click += async (s, e) => await CrashReporter.OpenLogFolderAsync();
        panel.Children.Add(logLink);

        panel.Children.Add(new TextBlock
        {
            Text = Localizer.Get("AboutThirdPartyHeader"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 15,
            Margin = new Thickness(0, 6, 0, 0)
        });

        // 通知本文(Markdown)は本文と同じレンダラ・スタイルでWebView2に表示する（表・リンクを整形して見せる）。
        // 読み込めなかった場合はメッセージのみ表示する。
        FrameworkElement noticesContent;
        if (notices != null)
        {
            var noticesView = new WebView2
            {
                Height = 260,
                DefaultBackgroundColor = Microsoft.UI.Colors.Transparent
            };
            noticesView.Loaded += async (s, e) => await ShowNoticesAsync(noticesView, notices);
            noticesContent = noticesView;
        }
        else
        {
            noticesContent = new TextBlock { Text = Localizer.Get("AboutThirdPartyLoadError"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
        }
        // 枠線はテーマリソースを用い、両テーマで視認できるようにする（未定義時は灰色にフォールバック）
        Microsoft.UI.Xaml.Media.Brush strokeBrush =
            Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var b) && b is Microsoft.UI.Xaml.Media.Brush br
                ? br
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
        panel.Children.Add(new Border
        {
            Child = noticesContent,
            BorderBrush = strokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        });

        // コントリビューター（最下部に小さく表示）。名前・リンクは本人の了承を得たものだけを載せる
        (string Name, string Url, string RoleKey)[] contributors =
        [
            ("Coldlapse", "https://github.com/Coldlapse", "AboutCreditColdlapse"),
            ("Alexis Gross", "https://github.com/AlexisGross", "AboutCreditTranslationEs"),
        ];
        var credit = new StackPanel { Spacing = 2, Margin = new Thickness(0, 6, 0, 0), Opacity = 0.75 };
        credit.Children.Add(new TextBlock { Text = Localizer.Get("AboutContributors") + ":", FontSize = 12 });
        foreach (var (name, url, roleKey) in contributors)
        {
            // 名前(リンク)と役割の2列。役割の文が長くてもダイアログの幅で折り返すよう、2列目は残りの幅を使う
            var row = new Grid { ColumnSpacing = 4, Margin = new Thickness(12, 0, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var link = new HyperlinkButton { Content = name, FontSize = 12, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Top };
            link.Click += (s, e) => LaunchExternal(url);
            row.Children.Add(link);
            var role = new TextBlock { Text = "— " + Localizer.Get(roleKey), FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(role, 1);
            row.Children.Add(role);
            credit.Children.Add(row);
        }
        panel.Children.Add(credit);

        // ウィンドウが小さいときだけダイアログ全体を縦スクロールし、内容が溢れても「閉じる」ボタンが隠れないようにする。
        // 高さの上限はダイアログ(ウィンドウの大きさ)に任せる。固定の上限を置くと、ウィンドウが十分大きくても
        // ライセンス表のスクロールと二重になるため
        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxWidth = 560
        };
    }

    // Aboutダイアログ内のWebView2にサードパーティ表記(Markdown)を描画する
    private async Task ShowNoticesAsync(WebView2 view, string markdown)
    {
        try
        {
            await EnsureWebViewAsync();   // 環境(_webEnv)とテンプレートを用意する
            await view.EnsureCoreWebView2Async(_webEnv);
            var core = view.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(
                "vendor", Path.Combine(AppContext.BaseDirectory, "Assets", "vendor"),
                CoreWebView2HostResourceAccessKind.Allow);
            OpenLinksExternally(core, confirm: false);
            // 閲覧専用の小さな表示のため、右クリックメニューとブラウザ既定のショートカットは使わない（Ctrl+C は有効）
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            // 狭いダイアログ内なので、本文より文字を小さく・余白を狭くする
            core.NavigationCompleted += async (s, e) =>
            {
                try
                {
                    await core.ExecuteScriptAsync(
                        "window.__setZoom && window.__setZoom('0.8');" +
                        "document.querySelector('.markdown-body').style.padding = '8px 12px';");
                }
                catch { /* 見た目のみの調整のため失敗は無視 */ }
            };

            var baseUri = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets") + Path.DirectorySeparatorChar).AbsoluteUri;
            view.NavigateToString(MarkdownRenderer.BuildHtml(markdown, _currentTheme, _template, baseUri));
        }
        catch
        {
            // WebView2が利用できない場合は枠が空になるだけで、ダイアログの他の情報は読める
        }
    }

    // [ファイル]→[開く]: ファイルピッカーでMarkdownファイルを選択して開く
    private async void OnOpenClick(object s, RoutedEventArgs e)
    {
        StorageFile? file;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            // WinUI3ではHWND初期化が必要
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            foreach (var ext in new[] { ".md", ".markdown", ".mdown", ".mkd" })
                picker.FileTypeFilter.Add(ext);

            file = await picker.PickSingleFileAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex, "OpenPicker");
            return;
        }
        if (file != null) await OpenFileAsync(file.Path);   // 例外は OpenFileAsync 内で処理される
    }

    // ルートGridへのドラッグ中: コピー操作を許可する
    private void OnDragOver(object s, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    // ルートGridへのドロップ: 最初のファイルを開く
    private async void OnDrop(object s, DragEventArgs e)
    {
        StorageFile? file;
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            file = items.OfType<StorageFile>().FirstOrDefault();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex, "Drop");
            return;
        }
        if (file != null) await OpenFileAsync(file.Path);   // 例外は OpenFileAsync 内で処理される
    }

    private void OnExitClick(object s, RoutedEventArgs e) { PersistSettings(); Application.Current.Exit(); }

    // テーマを切り替え、ウィンドウ・トグルアイコン・WebView2本文へ反映する
    // ウィンドウ(タイトルバー・メニューバーを含む)の背景を、文書の背景色(github-markdown-css の本文色)に合わせる。
    // template.html でもページ全体を同じ色にしているため、ウィンドウと文書の境目が出ない
    private void ApplyWindowBackground()
    {
        var dark = RootGrid.ActualTheme == ElementTheme.Dark;
        RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(dark
            ? Windows.UI.Color.FromArgb(0xFF, 0x0D, 0x11, 0x17)    // github-markdown-dark の本文背景
            : Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));  // github-markdown-light の本文背景
        ApplyCaptionButtonColors(dark);
    }

    // キャプションボタン(最小化・最大化・閉じる)の色をアプリのテーマに合わせる(Issue #9)。
    // 明示しないと OS 側の配色が残ることがあり、OSがダーク・アプリがライトのとき白い記号が白背景に溶けて見えなくなる
    private void ApplyCaptionButtonColors(bool dark)
    {
        var tb = AppWindow.TitleBar;
        var fg = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonForegroundColor = fg;
        tb.ButtonHoverForegroundColor = fg;
        tb.ButtonPressedForegroundColor = fg;
        tb.ButtonInactiveForegroundColor = dark
            ? Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80)
            : Windows.UI.Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E);
        // ホバー・押下時の背景は、前景色を薄く重ねた色にする
        tb.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x18, fg.R, fg.G, fg.B);
        tb.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x10, fg.R, fg.G, fg.B);
    }

    private async void SetTheme(ThemeMode mode)
    {
        _currentTheme = mode;
        ThemeService.ApplyToWindow(this, mode);
        ApplyWindowBackground();
        UpdateThemeToggleIcon();
        ThemeLightItem.IsChecked = mode == ThemeMode.Light;
        ThemeDarkItem.IsChecked = mode == ThemeMode.Dark;
        ThemeSystemItem.IsChecked = mode == ThemeMode.System;
        // WebView2側のCSSも切替
        if (_webReady)
            try
            {
                // WebView2自体の配色(スクロールバー・prefers-color-scheme)もアプリのテーマに合わせる。
                // 未設定だとOSの配色のままになり、例えばOSがダークでアプリがライトのとき
                // 本文だけライト・スクロールバー等はダークという不一致が起きる
                ContentView.CoreWebView2.Profile.PreferredColorScheme = mode switch
                {
                    ThemeMode.Light => CoreWebView2PreferredColorScheme.Light,
                    ThemeMode.Dark => CoreWebView2PreferredColorScheme.Dark,
                    _ => CoreWebView2PreferredColorScheme.Auto
                };
                await ContentView.CoreWebView2.ExecuteScriptAsync(
                    $"window.__setTheme && window.__setTheme('{ThemeService.ToCssClass(mode)}')");
            }
            catch
            {
                // WebView2が利用不可などのスクリプト実行失敗は無視（テーマ/ズームの見た目のみの影響）
            }
    }

    private void OnThemeLight(object s, RoutedEventArgs e) { SetTheme(ThemeMode.Light); PersistSettings(); }
    private void OnThemeDark(object s, RoutedEventArgs e) { SetTheme(ThemeMode.Dark); PersistSettings(); }
    private void OnThemeSystem(object s, RoutedEventArgs e) { SetTheme(ThemeMode.System); PersistSettings(); }
    // 「システムに合わせる」のときも含め、いま表示されているテーマの逆に切り替える(Issue #16)。
    // 設定値(_currentTheme)で判断すると、OSがダークで「システム」のとき最初のクリックがダーク→ダークになり見た目が変わらない
    private void OnThemeToggle(object s, RoutedEventArgs e)
    {
        SetTheme(RootGrid.ActualTheme == ElementTheme.Dark ? ThemeMode.Light : ThemeMode.Dark);
        PersistSettings();
    }

    // テーマ切替アイコンは、押すと切り替わる先(いま表示されているテーマの逆)を表す
    private void UpdateThemeToggleIcon() =>
        ThemeToggle.Content = ThemeService.ToggleIcon(RootGrid.ActualTheme == ElementTheme.Dark);

    // [表示]→[拡大]/[縮小]のショートカットを登録する。WebView2にフォーカスがあるときは template.html の keydown が
    // 処理するが、それだけではメニューバー等にフォーカスがあると効かないため、ウィンドウ側にも登録する。
    // 記号キー(+ / -)は VirtualKey に名前が無いのでキーコードで指定する(187 = VK_OEM_PLUS, 189 = VK_OEM_MINUS)。
    // テンキーの + / - も同じ操作にする。
    // JIS 配列では + は ; キー(187)を Shift 付きで押すため、Ctrl+Shift+187 も登録する(文書側の ev.key === '+' と合わせる)
    private void AddZoomAccelerators()
    {
        foreach (var key in new[] { (Windows.System.VirtualKey)187, Windows.System.VirtualKey.Add })
            ZoomInItem.KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator
                { Key = key, Modifiers = Windows.System.VirtualKeyModifiers.Control });
        ZoomInItem.KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator
            { Key = (Windows.System.VirtualKey)187,
              Modifiers = Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift });
        foreach (var key in new[] { (Windows.System.VirtualKey)189, Windows.System.VirtualKey.Subtract })
            ZoomOutItem.KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator
                { Key = key, Modifiers = Windows.System.VirtualKeyModifiers.Control });
    }

    private void OnZoomIn(object s, RoutedEventArgs e)
    {
        if (_webReady)
        {
            ApplyZoom(Math.Min(3.0, _zoomFactor + 0.1));
            PersistSettings();
        }
    }

    private void OnZoomOut(object s, RoutedEventArgs e)
    {
        if (_webReady)
        {
            ApplyZoom(Math.Max(0.5, _zoomFactor - 0.1));
            PersistSettings();
        }
    }

    private void OnZoomReset(object s, RoutedEventArgs e)
    {
        if (_webReady)
        {
            ApplyZoom(1.0);
            PersistSettings();
        }
    }

    // [表示]→[再読み込み]: 現在開いているファイルを再読込する(例外は OpenFileAsync 内で処理される)
    private async void OnReload(object s, RoutedEventArgs e)
    {
        if (_currentPath != null) await OpenFileAsync(_currentPath);
    }

    private void OnToggleStatusBar(object s, RoutedEventArgs e)
    {
        StatusBar.Visibility = StatusBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        StatusBarItem.IsChecked = StatusBar.Visibility == Visibility.Visible;
        PersistSettings();
    }
}
