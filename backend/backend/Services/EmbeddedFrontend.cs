using System.Reflection;
using Microsoft.AspNetCore.StaticFiles;

public static class EmbeddedFrontend
{
    private const string PREFIX = "frontend/";

    private static readonly Assembly Self = typeof(EmbeddedFrontend).Assembly;

    private static readonly Dictionary<string, string> Files = Self.GetManifestResourceNames()
        .Where(n => n.Replace('\\', '/').StartsWith(PREFIX))
        .ToDictionary(n => n.Replace('\\', '/').Substring(PREFIX.Length), n => n);

    private static readonly FileExtensionContentTypeProvider Types = new FileExtensionContentTypeProvider();

    public static async Task Serve(HttpContext context)
    {
        string path = (context.Request.Path.Value ?? "").TrimStart('/');

        if (!Files.ContainsKey(path))
        {
            path = "index.html";
        }

        if (!Files.TryGetValue(path, out string? name))
        {
            context.Response.StatusCode = 404;
            return;
        }

        if (!Types.TryGetContentType(path, out string? type))
        {
            type = "text/plain";
        }

        context.Response.ContentType = type;

        using (Stream? file = Self.GetManifestResourceStream(name))
        {
            if (file != null)
            {
                await file.CopyToAsync(context.Response.Body);
            }
        }
    }
}