using System.Net;
using System.Text;
using HtmlAgilityPack;

namespace Transcendence.Service.Core.Services.StaticData.Models;

internal static class RuneDescription
{
    internal static string? ToPlainText(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return description;

        var document = new HtmlDocument();
        document.LoadHtml(description);

        var text = new StringBuilder(description.Length);
        AppendText(document.DocumentNode, text);
        return string.Join(' ', text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void AppendText(HtmlNode node, StringBuilder text)
    {
        if (node is HtmlTextNode content)
        {
            text.Append(WebUtility.HtmlDecode(content.Text));
            return;
        }

        if (node.Name is "script" or "style" || node.NodeType == HtmlNodeType.Comment)
            return;

        var separatesText = node.Name is "br" or "p" or "div" or "li";
        if (separatesText)
            text.Append(' ');
        foreach (var child in node.ChildNodes)
            AppendText(child, text);
        if (separatesText)
            text.Append(' ');
    }
}
