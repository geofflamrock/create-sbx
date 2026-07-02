namespace CreateSbx.Widgets;

internal static class MarkupText
{
    /// <summary>Escapes text so it renders literally when passed to <c>Paragraph.FromMarkup</c>
    /// or <c>Text.FromMarkup</c> instead of being interpreted as markup tags.</summary>
    public static string Escape(string text) => text.Replace("[", "[[").Replace("]", "]]");
}
