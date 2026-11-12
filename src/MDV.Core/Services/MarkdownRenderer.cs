using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using MDV.Core.Models;

namespace MDV.Core.Services;

/// <summary>Markdownを変換しHTMLテンプレートに組み立てる</summary>
public static class MarkdownRenderer
{
    // GFM(表/タスクリスト等)+数式 を有効化したパイプライン
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        // 見出しの ID を GitHub と同じ規則にする(日本語・韓国語などの文字を残す)。
        // UseAdvancedExtensions も既定の設定(ASCII のみ)で同じ拡張を登録し、先に登録したほうが使われるため、必ずその前に置く
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseAdvancedExtensions()   // 表・タスクリスト・自動リンク等
        .UseMathematics()          // $...$ / $$...$$ を保持
        .Build();

    // Markdig(UseAdvancedExtensions)のDiagrams拡張により、```mermaid フェンスは
    // 既に <pre class="mermaid">...</pre> として出力されるため、変換処理は不要
    // （テンプレート側 mermaid.run({querySelector:'.mermaid'}) がそのまま拾える）。
    public static string RenderBody(string markdown)
    {
        return RemoveTagsThatReachOut(Markdown.ToHtml(markdown ?? string.Empty, Pipeline));
    }

    // CSP では止まらず、表示しただけで外へ通信してしまうタグ(#17。実測で確認したもの):
    //   <meta http-equiv="refresh"> … ページが移動しようとして、WebView が 1 回要求を送り、さらに既定のブラウザでその URL が開く
    //   <link rel="preconnect"> と <iframe src/srcdoc> … 要求は CSP で止まるが、その前に相手のサーバーへ接続してしまう
    // GitHub と同じく、文書の HTML からこれらのタグを取り除く(<link> は rel を問わない)。
    private static readonly Regex TagsThatReachOut = new(
        @"<(?:meta|link|iframe)(?=[\s/>]|$)[^>]*(?:>|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// 変換後の HTML から &lt;meta&gt;・&lt;link&gt;・&lt;iframe&gt; の開始タグを取り除く。
    /// Markdig は文字としての &lt; を &amp;lt; にするので、ここで見つかる "&lt;meta" は文書に書かれた生の HTML のタグだけ。
    /// 取り除いたあとに前後がつながって新しいタグにならないよう、見つからなくなるまで繰り返す。
    /// </summary>
    public static string RemoveTagsThatReachOut(string html)
    {
        string before;
        do
        {
            before = html;
            html = TagsThatReachOut.Replace(html, string.Empty);
        } while (html != before);
        return html;
    }

    public static string BuildHtml(string markdown, ThemeMode theme, string template, string baseDirUri)
    {
        var themeClass = theme switch
        {
            ThemeMode.Light => "theme-light",
            ThemeMode.Dark => "theme-dark",
            _ => "theme-system"
        };
        // CSP の nonce(template.html)。表示のたびに作り直し、文書が値を知ることができないようにする。
        // 本文の {{NONCE}} が置換されないよう、本文を埋め込む前(最初)に置換する
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        return template
            .Replace("{{NONCE}}", nonce)
            .Replace("{{THEME_CLASS}}", themeClass)
            .Replace("{{BASE_HREF}}", baseDirUri)
            .Replace("{{BODY}}", RenderBody(markdown));
    }
}
