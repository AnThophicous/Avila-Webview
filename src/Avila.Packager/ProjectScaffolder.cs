using System.Text.Json;
using Avila.Core;
using Avila.Security;

namespace Avila.Packager;

public sealed class ProjectScaffolder
{
    public async Task<string> InitAsync(string name, string template = "vanilla", string? url = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name is required.", nameof(name));
        }

        var slug = Slugify(name);
        var projectDirectory = Path.GetFullPath($"{slug}.avw");
        if (Directory.Exists(projectDirectory) || File.Exists(projectDirectory))
        {
            throw new IOException($"Project already exists: {projectDirectory}");
        }

        Directory.CreateDirectory(Path.Combine(projectDirectory, "src"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "assets"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "native"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "plugins"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "build"));

        var browserMode = !string.IsNullOrWhiteSpace(url) || template.Equals("browser-app", StringComparison.OrdinalIgnoreCase) || template.Equals("browser", StringComparison.OrdinalIgnoreCase);
        if (browserMode && string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("Browser app mode requires --url <site>.");
        }

        var manifest = CreateDefaultManifest(name, slug, browserMode, url);
        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "avila.json"),
            JsonSerializer.Serialize(manifest, ManifestLoader.JsonOptions),
            cancellationToken).ConfigureAwait(false);

        if (browserMode)
        {
            await File.WriteAllTextAsync(
                Path.Combine(projectDirectory, "README.md"),
                TemplateBrowserReadme(name, url),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "src", "index.html"), TemplateIndexHtml(name), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "src", "styles.css"), TemplateStylesCss(), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "src", "main.js"), TemplateMainJs(), cancellationToken).ConfigureAwait(false);
        }

        return projectDirectory;
    }

    private static AvilaManifest CreateDefaultManifest(string name, string slug, bool browserMode, string? url)
    {
        var browserUrl = string.IsNullOrWhiteSpace(url) ? "" : url.Trim();
        string[] allowedOrigins = [];
        if (Uri.TryCreate(browserUrl, UriKind.Absolute, out var uri))
        {
            allowedOrigins = [GetOrigin(uri)];
        }

        return new AvilaManifest
        {
            Mode = browserMode ? "browser-app" : "appview",
            App = new AppManifest
            {
                Id = $"com.example.{slug.Replace("-", "")}",
                Name = name,
                Version = "1.0.0",
                Entry = browserMode ? "" : "src/index.html",
                Icon = ""
            },
            Window = new WindowManifest(),
            Security = new SecurityManifest(),
            Browser = new BrowserManifest
            {
                Url = browserUrl,
                AllowedOrigins = allowedOrigins,
                Navigation = true,
                ExternalOrigins = "open-system-browser",
                Downloads = true,
                Popups = true
            },
            Permissions = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["app.info"] = true,
                ["system.ping"] = true,
                ["window.setTitle"] = true,
                ["window.setSize"] = true,
                ["window.center"] = true,
                ["window.setDraggable"] = true,
                ["window.setMica"] = true,
                ["window.setRoundedCorners"] = true,
                ["browser.back"] = false,
                ["browser.forward"] = false,
                ["browser.reload"] = false,
                ["browser.stop"] = false,
                ["browser.canGoBack"] = false,
                ["browser.canGoForward"] = false,
                ["browser.setZoom"] = false,
                ["browser.find"] = false,
                ["browser.openDevTools"] = false,
                ["browser.navigate"] = browserMode,
                ["dialog.openFile"] = true,
                ["clipboard.writeText"] = true,
                ["fs.readFile"] = false,
                ["fs.writeFile"] = false,
                ["network.fetch"] = false,
                ["node.run"] = false,
                ["os.exec"] = false
            },
            Performance = new PerformanceManifest(),
            Package = new PackageManifest(),
            Frontend = new FrontendManifest
            {
                Framework = "vanilla",
                Language = "js",
                Adapter = "static",
                Source = "src",
                Dist = "dist"
            },
            Node = new NodeManifest
            {
                Enabled = false,
                Mode = "dev-only",
                AllowedScripts = ["dev", "build"]
            },
            Build = new BuildManifest
            {
                OutputName = EngineIdentity.ResolveProcessName(null, ToPascalName(name))
            }
        };
    }

    private static string Slugify(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug) ? "avila-app" : slug;
    }

    private static string ToPascalName(string value)
    {
        var parts = value.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var name = string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        return string.IsNullOrWhiteSpace(name) ? "AvilaApp" : name;
    }

    private static string TemplateIndexHtml(string name) => $$"""
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>{{name}}</title>
    <link rel="stylesheet" href="./styles.css" />
    <script defer src="./main.js"></script>
  </head>
  <body>
    <header class="titlebar" data-avila-drag>
      <div class="brand">
        <span class="mark"></span>
        <span>{{name}}</span>
      </div>
      <button class="primary" id="ping" data-avila-no-drag>Ping</button>
    </header>

    <main class="shell">
      <aside class="sidebar">
        <span class="item active">Overview</span>
        <span class="item">Bridge</span>
        <span class="item">Window</span>
      </aside>

      <section class="content">
        <p class="eyebrow">Desktop engine</p>
        <h1>Fast desktop apps with web UI.</h1>
        <p class="muted">Secure bridge, native window controls, and a production EXE pipeline.</p>
        <pre id="output">Starting...</pre>
      </section>
    </main>
  </body>
</html>
""";

    private static string TemplateStylesCss() => """
:root {
  color-scheme: dark;
  font-family: "Segoe UI", system-ui, sans-serif;
  background: #1e1f24;
  color: #e8e8ec;
}

* {
  box-sizing: border-box;
}

body {
  margin: 0;
  min-height: 100vh;
  background: #1e1f24;
}

.titlebar {
  height: 44px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 14px;
  border-bottom: 1px solid #30313a;
  background: #25262c;
}

.brand,
.item {
  display: flex;
  align-items: center;
  gap: 10px;
}

.mark {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: #a78bfa;
}

.primary {
  border: 0;
  border-radius: 6px;
  padding: 7px 14px;
  background: #7c5cff;
  color: white;
  font: inherit;
  cursor: pointer;
}

.shell {
  min-height: calc(100vh - 44px);
  display: grid;
  grid-template-columns: 220px 1fr;
}

.sidebar {
  padding: 12px;
  border-right: 1px solid #30313a;
  background: #222329;
}

.item {
  height: 34px;
  padding: 0 10px;
  color: #a7a9b4;
  border-radius: 5px;
}

.item.active {
  color: #ffffff;
  background: #2c2d35;
}

.content {
  padding: 44px;
}

.eyebrow {
  margin: 0 0 12px;
  color: #a78bfa;
  font-size: 12px;
  letter-spacing: .14em;
  text-transform: uppercase;
}

h1 {
  margin: 0 0 12px;
  font-size: 36px;
  letter-spacing: -0.04em;
}

.muted {
  margin: 0 0 24px;
  color: #a7a9b4;
}

pre {
  max-width: 720px;
  min-height: 150px;
  padding: 16px;
  border: 1px solid #333540;
  border-radius: 8px;
  background: #18191d;
  color: #d9dbE7;
  overflow: auto;
}
""";

    private static string TemplateMainJs() => """
const output = document.querySelector("#output");
const ping = document.querySelector("#ping");

async function boot() {
  const info = await avila.app.info();
  const pong = await avila.system.ping();
  await avila.window.setMica(true);
  await avila.window.setRoundedCorners(true);

  output.textContent = JSON.stringify({ info, pong }, null, 2);
}

ping.addEventListener("click", async () => {
  const pong = await avila.system.ping();
  output.textContent = JSON.stringify(pong, null, 2);
});

boot().catch(error => {
  output.textContent = error.message;
});
""";

    private static string TemplateBrowserReadme(string name, string? url) => $$"""
# {{name}}

BrowserApp mode opens your site inside an Avila window.

Start URL:
{{(string.IsNullOrWhiteSpace(url) ? "(set browser.url in avila.json)" : url)}}

Rules:

- `window.avila` is not injected into remote pages.
- Native APIs are off by default.
- Allowed origins stay in `browser.allowedOrigins`.
- External origins can open in the system browser.
- `app.icon` starts empty so the app can ship with its own identity.
- `build.outputName` can be customized before packaging.
"""; 

    private static string GetOrigin(Uri uri)
    {
        return uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }
}
