using Microsoft.Extensions.FileProviders;

namespace UptimeUrpo.Api.Endpoints;

public static class DashboardFiles
{
    public static void UseDashboardFiles(this WebApplication app)
    {
        // Published assets live in wwwroot. Native local runs can serve Vite's built dist.
        var published = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        var development = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../../frontend/dist"));
        var directory = File.Exists(Path.Combine(published, "index.html")) ? published : development;
        if (!File.Exists(Path.Combine(directory, "index.html")))
        {
            return; // API-only development remains possible before the first frontend build.
        }

        var files = new PhysicalFileProvider(directory);
        app.Lifetime.ApplicationStopped.Register(files.Dispose);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Context.Response.Headers.CacheControl = context.File.Name == "index.html"
                    ? "no-cache" : "public, max-age=3600";
            }
        });
        // Hash-based UI routes all request '/'. No catch-all can turn an API 404 into HTML.
    }
}
