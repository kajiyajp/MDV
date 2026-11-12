using MDV.Core.Models;
using MDV.Core.Services;
using Xunit;

public class MarkdownRendererTests
{
    [Fact] public void Table_is_rendered()
    {
        var html = MarkdownRenderer.RenderBody("| a | b |\n|---|---|\n| 1 | 2 |");
        Assert.Contains("<table", html);
    }

    [Fact] public void Task_list_is_rendered()
    {
        var html = MarkdownRenderer.RenderBody("- [x] done\n- [ ] todo");
        Assert.Contains("type=\"checkbox\"", html);
    }

    [Fact] public void Fenced_code_has_language_class()
    {
        var html = MarkdownRenderer.RenderBody("```csharp\nvar x = 1;\n```");
        Assert.Contains("language-csharp", html);
    }

    [Fact] public void Mermaid_block_has_mermaid_class()
    {
        // Markdig 1.3.2 (UseAdvancedExtensions の Diagrams拡張) は
        // ```mermaid フェンスを <pre class="mermaid">...</pre> として出力する
        // （<pre><code class="language-mermaid"> ではない）。
        var html = MarkdownRenderer.RenderBody("```mermaid\ngraph TD; A-->B;\n```");
        Assert.Contains("class=\"mermaid\"", html);
        Assert.Contains("graph TD; A-->B;", html);
        Assert.DoesNotContain("language-mermaid", html);
    }

    [Fact] public void Math_dollar_is_preserved_for_katex()
    {
        // KaTeXはクライアント側でレンダリングするため、数式の中身がHTMLに残ればよい
        var html = MarkdownRenderer.RenderBody("$E=mc^2$");
        Assert.Contains("E=mc^2", html);
    }

    [Fact] public void Math_renders_as_katex_span_with_backslash_paren_delimiter()
    {
        // Markdig 1.3.2 (UseMathematics) は $...$ を解釈し、$ をHTMLに残さず
        // <span class="math">\(...\)</span> 形式で出力する。
        // テンプレート側KaTeXの delimiters は \( \) を含む必要があり、この契約を固定する。
        var html = MarkdownRenderer.RenderBody("$E=mc^2$");
        Assert.Contains("class=\"math\"", html);
        Assert.Contains("\\(E=mc^2\\)", html);
    }

    [Fact] public void BuildHtml_substitutes_placeholders()
    {
        var template = "<html class=\"{{THEME_CLASS}}\"><base href=\"{{BASE_HREF}}\"><body>{{BODY}}</body></html>";
        var html = MarkdownRenderer.BuildHtml("# Hi", ThemeMode.Dark, template, "file:///C:/docs/");
        Assert.Contains("theme-dark", html);
        Assert.Contains("file:///C:/docs/", html);
        Assert.Contains("<h1", html);
        Assert.DoesNotContain("{{BODY}}", html);
        Assert.DoesNotContain("{{THEME_CLASS}}", html);
        Assert.DoesNotContain("{{BASE_HREF}}", html);
    }

    // CSP の nonce は表示のたびに変わり、ポリシーとスクリプトの両方に同じ値が入る
    [Fact] public void BuildHtml_fills_a_new_nonce_each_time()
    {
        var template = "<meta content=\"script-src 'nonce-{{NONCE}}'\"><script nonce=\"{{NONCE}}\"></script>{{BODY}}";
        string NonceOf(string html)
        {
            var policy = System.Text.RegularExpressions.Regex.Match(html, "'nonce-([^']+)'").Groups[1].Value;
            var script = System.Text.RegularExpressions.Regex.Match(html, "<script nonce=\"([^\"]+)\"").Groups[1].Value;
            Assert.Equal(policy, script);
            return policy;
        }
        var a = NonceOf(MarkdownRenderer.BuildHtml("x", ThemeMode.Light, template, "file:///C:/docs/"));
        var b = NonceOf(MarkdownRenderer.BuildHtml("x", ThemeMode.Light, template, "file:///C:/docs/"));
        Assert.True(a.Length >= 16);
        Assert.NotEqual(a, b);
    }

    // 文書に {{NONCE}} と書いても nonce の値に置き換わらない(文書のスクリプトが nonce を得られない)
    [Fact] public void BuildHtml_does_not_put_the_nonce_into_the_document()
    {
        var template = "<script nonce=\"{{NONCE}}\"></script><body>{{BODY}}</body>";
        var html = MarkdownRenderer.BuildHtml("<script nonce=\"{{NONCE}}\">x()</script>", ThemeMode.Light, template, "file:///C:/docs/");
        Assert.Contains("<script nonce=\"{{NONCE}}\">x()</script>", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "\\{\\{NONCE\\}\\}"));
    }

    // 見出しの ID は GitHub と同じ規則にする。期待値は GitHub の Markdown API(2026-10-25)の出力と照合したもの。
    // 既定(ASCII のみ)では日本語・韓国語などの見出しが "section" になり、目次のリンク(#見出し)が合わなかった
    [Theory]
    [InlineData("第一章", "第一章")]
    [InlineData("日本語の見出し", "日本語の見出し")]
    [InlineData("첫째 절", "첫째-절")]
    [InlineData("中文标题", "中文标题")]
    [InlineData("한글 English 混合", "한글-english-混合")]
    [InlineData("見出し：コロン", "見出しコロン")]
    [InlineData("全角　スペース", "全角スペース")]
    [InlineData("Café déjà vu", "café-déjà-vu")]
    [InlineData("Hello, World!", "hello-world")]
    [InlineData("1. Introduction", "1-introduction")]
    [InlineData("C# and .NET", "c-and-net")]
    [InlineData("API Reference (v2)", "api-reference-v2")]
    [InlineData("`code` in heading", "code-in-heading")]
    public void Heading_id_follows_github(string heading, string id)
    {
        var html = MarkdownRenderer.RenderBody("## " + heading);
        Assert.Contains($"<h2 id=\"{id}\">", html);
    }

    [Fact] public void Duplicate_headings_get_numbered_ids_like_github()
    {
        var html = MarkdownRenderer.RenderBody("## 概要\n\n## 概要\n\n## 概要");
        Assert.Contains("<h2 id=\"概要\">", html);
        Assert.Contains("<h2 id=\"概要-1\">", html);
        Assert.Contains("<h2 id=\"概要-2\">", html);
    }
}
