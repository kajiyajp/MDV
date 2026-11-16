using MDV.Core.Services;
using Xunit;

/// <summary>文書内の相対パスのリンクを、MDV で開く Markdown ファイルに解決する規則(#17)</summary>
public class DocumentLinksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mdv-links-" + Guid.NewGuid().ToString("N"));
    private readonly string _doc;

    public DocumentLinksTests()
    {
        _doc = Path.Combine(_root, "docs");
        Directory.CreateDirectory(Path.Combine(_doc, "sub"));
        Directory.CreateDirectory(Path.Combine(_doc, "folder.md"));
        foreach (var f in new[] { "other.md", "sub/guide.markdown", "sub/한글 문서.md", "UPPER.MD", "notes.mdown", "old.mkd",
                                  "image.png", "run.bat", "readme.txt" })
            File.WriteAllText(Path.Combine(_doc, f), "# x");
        File.WriteAllText(Path.Combine(_root, "README.md"), "# root");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private (string Path, string? Fragment)? Resolve(string href) => DocumentLinks.Resolve(_doc, href);

    [Theory]
    [InlineData("other.md", "other.md", null)]
    [InlineData("./other.md", "other.md", null)]
    [InlineData("other.md#概要", "other.md", "概要")]
    [InlineData("other.md#%E6%A6%82%E8%A6%81", "other.md", "%E6%A6%82%E8%A6%81")]   // 見出しのデコードはページ側(__scrollToFragment)
    [InlineData("other.md?plain=1#x", "other.md", "x")]
    [InlineData("sub/guide.markdown", "sub/guide.markdown", null)]
    [InlineData("sub/%ED%95%9C%EA%B8%80%20%EB%AC%B8%EC%84%9C.md", "sub/한글 문서.md", null)]
    [InlineData("UPPER.MD", "UPPER.MD", null)]
    [InlineData("notes.mdown", "notes.mdown", null)]
    [InlineData("old.mkd", "old.mkd", null)]
    [InlineData("  other.md  ", "other.md", null)]
    [InlineData("other.md#", "other.md", null)]
    public void Markdown_files_are_opened(string href, string file, string? fragment)
    {
        var link = Resolve(href);
        Assert.NotNull(link);
        Assert.Equal(Path.GetFullPath(Path.Combine(_doc, file)), link!.Value.Path);
        Assert.Equal(fragment, link.Value.Fragment);
    }

    // 文書のフォルダの外の Markdown も開く(クリックした人が読むだけで、文書の側には何も渡らない)
    [Fact]
    public void Markdown_files_above_the_folder_are_opened()
    {
        Assert.Equal(Path.Combine(_root, "README.md"), Resolve("../README.md")!.Value.Path);
        Assert.Equal(Path.Combine(_root, "README.md"), Resolve("sub/../../README.md#top")!.Value.Path);
    }

    [Theory]
    [InlineData("image.png")]          // 画像や他の種類のファイルは開かない(実行ファイルを起動しないため)
    [InlineData("run.bat")]
    [InlineData("readme.txt")]
    [InlineData("missing.md")]         // 存在しない
    [InlineData("folder.md")]          // .md という名前のフォルダ
    [InlineData("sub/")]
    [InlineData("")]
    [InlineData("#only-fragment")]     // 同じページの見出しはページ側で扱う
    [InlineData("/docs/other.md")]     // ルートからのパスは、どこを指すか決められない
    [InlineData("\\\\server\\share\\a.md")]
    [InlineData("C:/x/other.md")]
    [InlineData("other.md%00.png")]
    [InlineData("%E0%A4%A")]           // デコードできない
    public void Other_links_are_not_opened(string href) => Assert.Null(Resolve(href));

    // 絶対パス(ドライブ名付き)は、ファイルが実在しても開かない。開くのは相対パスのリンクだけ
    [Fact]
    public void Absolute_paths_are_not_opened()
    {
        var other = Path.Combine(_doc, "other.md");
        Assert.Null(Resolve(other));
        Assert.Null(Resolve(other.Replace('\\', '/')));
    }

    // 文書のフォルダの外にある文書は、開く前に確認する(ConfirmExternalOpen)
    [Theory]
    [InlineData("other.md", true)]
    [InlineData("sub/guide.markdown", true)]
    [InlineData("../README.md", false)]
    [InlineData("../docs-other/a.md", false)]   // 名前の先頭が同じ別のフォルダ
    public void IsInsideFolder_tells_documents_outside_the_folder(string rel, bool inside) =>
        Assert.Equal(inside, DocumentLinks.IsInsideFolder(_doc, Path.Combine(_doc, rel)));
}
