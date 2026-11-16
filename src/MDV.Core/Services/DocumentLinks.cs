namespace MDV.Core.Services;

/// <summary>
/// 文書内の相対パスのリンク(例: other.md、../README.md#概要)を、MDV で開く Markdown ファイルに解決する(#17)。
/// 開くのは MDV が扱う拡張子の、実在するファイルだけ。それ以外のリンク(画像・フォルダ・実行ファイルなど)は開かない。
/// </summary>
public static class DocumentLinks
{
    // ファイルを開くダイアログ・ファイルの関連付けと同じ拡張子
    public static readonly string[] MarkdownExtensions = [".md", ".markdown", ".mdown", ".mkd"];

    /// <summary>
    /// href(リンクに書かれたままの値)を、開く Markdown ファイルの絶対パスと、移動先の見出し(# の後ろ。無ければ null)にする。
    /// 開かないリンクは null。
    /// </summary>
    public static (string Path, string? Fragment)? Resolve(string docFolder, string href)
    {
        href = href.Trim();
        string? fragment = null;
        var hash = href.IndexOf('#');
        if (hash >= 0)
        {
            fragment = href[(hash + 1)..];
            href = href[..hash];
        }
        var query = href.IndexOf('?');
        if (query >= 0) href = href[..query];

        string rel;
        try { rel = Uri.UnescapeDataString(href); }
        catch (UriFormatException) { return null; }

        // 空(同じ文書の #見出し)、ルートからのパス(/docs/a.md。どこを指すか決められない)、
        // ドライブ名・スキーム・UNC(:、\\)、NUL は扱わない
        if (rel.Length == 0 || rel[0] is '/' or '\\' || rel.IndexOfAny([':', '\0']) >= 0)
            return null;

        string full;
        try { full = Path.GetFullPath(Path.Combine(docFolder, rel)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }

        if (!MarkdownExtensions.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase) || !File.Exists(full))
            return null;
        return (full, string.IsNullOrEmpty(fragment) ? null : fragment);
    }

    /// <summary>path が文書のフォルダ(とその下)にあるか。外にある文書は開く前に確認する(ConfirmExternalOpen)</summary>
    public static bool IsInsideFolder(string docFolder, string path)
    {
        var root = Path.GetFullPath(docFolder);
        if (!Path.EndsInDirectorySeparator(root)) root += Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
