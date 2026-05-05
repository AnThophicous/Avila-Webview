using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Avila.Runtime;

public sealed record DevErrorSnapshot(
    string Title,
    string Kind,
    string Message,
    string File,
    int Line,
    int Column,
    string Stack,
    string Url)
{
    public string Location => string.IsNullOrWhiteSpace(File)
        ? "(unknown location)"
        : Column > 0
            ? $"{File}:{Line}:{Column}"
            : Line > 0
                ? $"{File}:{Line}"
                : File;

    public static DevErrorSnapshot FromException(string title, Exception exception)
    {
        var trace = new StackTrace(exception, true);
        var frame = trace.GetFrames()?.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate.GetFileName()));

        return new DevErrorSnapshot(
            title,
            exception.GetType().Name,
            exception.Message,
            frame?.GetFileName() ?? "",
            frame?.GetFileLineNumber() ?? 0,
            frame?.GetFileColumnNumber() ?? 0,
            exception.ToString(),
            "");
    }

    public static DevErrorSnapshot FromFrontendJson(JsonElement root)
    {
        static string ReadString(JsonElement parent, string name)
        {
            return parent.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
        }

        static int ReadInt(JsonElement parent, string name)
        {
            return parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
        }

        return new DevErrorSnapshot(
            "Inspector Console",
            ReadString(root, "kind"),
            ReadString(root, "message"),
            ReadString(root, "filename"),
            ReadInt(root, "lineno"),
            ReadInt(root, "colno"),
            ReadString(root, "stack"),
            ReadString(root, "url"));
    }

    public static DevErrorSnapshot FromConsoleMessage(string title, string level, string message, string source, int line, int column)
    {
        return new DevErrorSnapshot(
            title,
            level,
            message,
            source,
            line,
            column,
            "",
            "");
    }
}

public static class DevErrorPageBuilder
{
    public static string Build(DevErrorSnapshot snapshot)
    {
        var title = HtmlEncode(snapshot.Title);
        var kind = HtmlEncode(snapshot.Kind);
        var message = HtmlEncode(snapshot.Message);
        var location = HtmlEncode(snapshot.Location);
        var stack = HtmlEncode(snapshot.Stack);
        var url = HtmlEncode(snapshot.Url);

        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>{{title}}</title>
  <style>
    :root {
      color-scheme: dark;
      font-family: "Segoe UI", system-ui, sans-serif;
      background: #191b20;
      color: #e7e7eb;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      background:
        radial-gradient(circle at top left, rgba(124, 92, 255, 0.16), transparent 30%),
        linear-gradient(180deg, #1a1c21, #111216);
      color: #e7e7eb;
    }
    .wrap {
      min-height: 100vh;
      padding: 36px;
      display: grid;
      place-items: center;
    }
    .card {
      width: min(1040px, 100%);
      border: 1px solid #2f313a;
      border-radius: 16px;
      background: rgba(24, 26, 31, 0.92);
      box-shadow: 0 24px 70px rgba(0, 0, 0, 0.4);
      overflow: hidden;
    }
    .header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      padding: 18px 22px;
      border-bottom: 1px solid #2f313a;
      background: rgba(255, 255, 255, 0.02);
    }
    .eyebrow {
      font-size: 12px;
      letter-spacing: .14em;
      text-transform: uppercase;
      color: #a78bfa;
      margin-bottom: 6px;
    }
    h1 {
      margin: 0;
      font-size: 22px;
      letter-spacing: -0.02em;
    }
    .status {
      color: #fda4af;
      font-size: 13px;
      border: 1px solid rgba(253, 164, 175, 0.22);
      background: rgba(253, 164, 175, 0.08);
      padding: 6px 10px;
      border-radius: 999px;
      white-space: nowrap;
    }
    .body {
      display: grid;
      grid-template-columns: 1.1fr 1.4fr;
      gap: 0;
    }
    .panel {
      padding: 22px;
    }
    .panel + .panel {
      border-left: 1px solid #2f313a;
    }
    .label {
      font-size: 12px;
      text-transform: uppercase;
      letter-spacing: .12em;
      color: #9ca3af;
      margin-bottom: 8px;
    }
    .message {
      font-size: 18px;
      line-height: 1.5;
      margin: 0 0 14px;
    }
    .meta {
      display: grid;
      gap: 10px;
    }
    .row {
      padding: 12px 14px;
      border: 1px solid #30323b;
      border-radius: 10px;
      background: #15171c;
    }
    .row span {
      display: block;
      font-size: 12px;
      color: #9ca3af;
      margin-bottom: 4px;
    }
    .row strong {
      font-weight: 600;
      word-break: break-word;
    }
    pre {
      margin: 0;
      max-height: 520px;
      overflow: auto;
      padding: 16px;
      border-radius: 12px;
      border: 1px solid #30323b;
      background: #101216;
      color: #d7d9e6;
      font: 12px/1.7 Consolas, "Courier New", monospace;
      white-space: pre-wrap;
      word-break: break-word;
    }
    .footer {
      padding: 16px 22px 20px;
      border-top: 1px solid #2f313a;
      color: #a7a9b4;
      font-size: 13px;
    }
    .footer code {
      color: #f9fafb;
    }
    @media (max-width: 900px) {
      .body { grid-template-columns: 1fr; }
      .panel + .panel { border-left: 0; border-top: 1px solid #2f313a; }
      .header { flex-direction: column; align-items: flex-start; }
    }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card">
      <div class="header">
        <div>
          <div class="eyebrow">Avila Inspector Console</div>
          <h1>{{title}}</h1>
        </div>
        <div class="status">{{kind}}</div>
      </div>
      <div class="body">
        <div class="panel">
          <div class="label">Message</div>
          <p class="message">{{message}}</p>

          <div class="meta">
            <div class="row">
              <span>Location</span>
              <strong>{{location}}</strong>
            </div>
            <div class="row">
              <span>Source URL</span>
              <strong>{{url}}</strong>
            </div>
          </div>
        </div>
        <div class="panel">
          <div class="label">Stack Trace</div>
          <pre>{{stack}}</pre>
        </div>
      </div>
      <div class="footer">
        Fix the issue and restart the app. If this came from frontend code, inspect <code>{{location}}</code>.
      </div>
    </section>
  </div>
</body>
</html>
""";
    }

    private static string HtmlEncode(string value) => WebUtility.HtmlEncode(value ?? "");
}

public sealed class DevErrorForm : Form
{
    public DevErrorForm(DevErrorSnapshot snapshot)
    {
        Text = snapshot.Title;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 26, 31);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 10f);
        MinimumSize = new Size(900, 620);
        Width = 1120;
        Height = 760;

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 84,
            Padding = new Padding(24, 20, 24, 16),
            BackColor = Color.FromArgb(30, 32, 39)
        };

        var title = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 32,
            Text = "Avila Inspector Console",
            Font = new Font("Segoe UI Semibold", 18f),
            ForeColor = Color.White
        };

        var subtitle = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Bottom,
            Height = 24,
            Text = $"{snapshot.Kind}  |  {snapshot.Location}",
            ForeColor = Color.FromArgb(170, 173, 186)
        };

        header.Controls.Add(subtitle);
        header.Controls.Add(title);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(24),
            BackColor = BackColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 210f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var summary = CreateSummaryPanel(snapshot);
        var stack = CreateStackPanel(snapshot);

        root.Controls.Add(summary, 0, 0);
        root.SetColumnSpan(summary, 1);
        root.Controls.Add(stack, 1, 0);
        root.SetRowSpan(stack, 2);
        root.Controls.Add(CreateHintPanel(snapshot), 0, 1);

        Controls.Add(root);
        Controls.Add(header);
    }

    private static Control CreateSummaryPanel(DevErrorSnapshot snapshot)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            BackColor = Color.FromArgb(27, 29, 35),
            BorderStyle = BorderStyle.FixedSingle
        };

        panel.Controls.Add(CreateRow("Message", snapshot.Message, DockStyle.Top));
        panel.Controls.Add(CreateRow("Location", snapshot.Location, DockStyle.Top));
        panel.Controls.Add(CreateRow("Source URL", string.IsNullOrWhiteSpace(snapshot.Url) ? "(local)" : snapshot.Url, DockStyle.Top));
        return panel;
    }

    private static Control CreateStackPanel(DevErrorSnapshot snapshot)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            BackColor = Color.FromArgb(18, 20, 24),
            BorderStyle = BorderStyle.FixedSingle
        };

        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "Stack Trace",
            ForeColor = Color.FromArgb(160, 162, 172)
        };

        var stack = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = panel.BackColor,
            ForeColor = Color.Gainsboro,
            Font = new Font("Consolas", 10f),
            Text = snapshot.Stack
        };

        panel.Controls.Add(stack);
        panel.Controls.Add(label);
        return panel;
    }

    private static Control CreateHintPanel(DevErrorSnapshot snapshot)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            BackColor = Color.FromArgb(27, 29, 35),
            BorderStyle = BorderStyle.FixedSingle
        };

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Fix the issue and run again. If this came from frontend code, the file and line are shown above.",
            ForeColor = Color.FromArgb(170, 173, 186)
        };
        panel.Controls.Add(hint);
        return panel;
    }

    private static Control CreateRow(string title, string value, DockStyle dock)
    {
        var panel = new Panel
        {
            Dock = dock,
            Height = 62,
            Padding = new Padding(0, 0, 0, 10)
        };

        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 16,
            Text = title,
            ForeColor = Color.FromArgb(160, 162, 172)
        };

        var text = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(17, 19, 24),
            ForeColor = Color.White,
            Text = value,
            Font = new Font("Consolas", 9.5f)
        };

        panel.Controls.Add(text);
        panel.Controls.Add(label);
        return panel;
    }
}
