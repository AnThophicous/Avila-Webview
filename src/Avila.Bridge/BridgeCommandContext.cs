using Avila.Diagnostics;
using Avila.Security;
using Avila.Workers;

namespace Avila.Bridge;

public sealed class BridgeCommandContext
{
    public required AvilaProject Project { get; init; }

    public required string Mode { get; init; }

    public required IRuntimeGateway Runtime { get; init; }

    public required INodeHostGateway NodeHost { get; init; }

    public required INativeShellGateway NativeShell { get; init; }

    public required IWindowGateway Window { get; init; }

    public required IDialogGateway Dialog { get; init; }

    public required IClipboardGateway Clipboard { get; init; }

    public required IBrowserGateway Browser { get; init; }

    public required IRemoteWebViewGateway RemoteWebViews { get; init; }

    public required AvilaWorkerPool Workers { get; init; }

    public required SafeLogger Logger { get; init; }

    public required DiagnosticsCollector Diagnostics { get; init; }
}
