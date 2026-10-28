using Airlock.Application.Logging;
using Airlock.Domain.Scanning;
using Airlock.Web.Logging;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Airlock.Web.Interop;

/// <summary>
/// <c>wwwroot/js/airlock.js</c>, the page's script (U5 business logic model, the <c>airlock.js</c> contract), one module
/// per circuit. A failed call into the browser is logged at Error and the page carries on (U5-NFR-12).
/// </summary>
internal sealed class AirlockJs(IJSRuntime runtime, ILogger<AirlockJs> logger) : IAsyncDisposable
{
    /// <summary>The module's address until a component hands over its fingerprinted one (refinement 5).</summary>
    public const string ModulePath = "js/airlock.js";

    private readonly Lock gate = new();
    private string address = ModulePath;
    private Task<IJSObjectReference>? module;

    /// <summary>Only components can read <c>Assets</c>, so <c>MainLayout</c> hands over the address before the first call.</summary>
    public void UseModuleAddress(string fingerprinted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprinted);
        lock (gate)
        {
            if (module is null)
            {
                address = fingerprinted;
            }
        }
    }

    /// <summary>UR-07: the panel opens the file dialog in the browser; picked files are reported to the sink.</summary>
    public ValueTask<bool> BindArtifactPickerAsync(ElementReference panel, ElementReference fileInput, DotNetObjectReference<UploadSink> sink) =>
        InvokeVoidAsync("bindArtifactPicker", panel, fileInput, sink);

    public ValueTask<bool> UnstageAsync(string stagedId) => InvokeVoidAsync("unstage", stagedId);

    /// <summary>UR-10: returns once the upload has started; its progress and answer arrive through the sink.</summary>
    public async ValueTask<UploadStart> SubmitScanAsync(ScanSubmissionTarget target, DotNetObjectReference<UploadSink> sink) =>
        await InvokeVoidAsync("submitScan", target, sink) ? UploadStart.Started : UploadStart.Failed;

    /// <summary>UR-33: the files the page uploaded for that scan, again; <see cref="UploadStart.NoFiles"/> when they are gone.</summary>
    public async ValueTask<UploadStart> ResubmitScanAsync(ScanId scanId, DotNetObjectReference<UploadSink> sink)
    {
        var (answered, held) = await InvokeAsync<bool>("resubmitScan", scanId.ToString(), sink);
        return !answered ? UploadStart.Failed : held ? UploadStart.Started : UploadStart.NoFiles;
    }

    /// <summary>UR-17: the view keeps to the newest line unless the analyst has scrolled up.</summary>
    public ValueTask<bool> FollowTerminalAsync(ElementReference lines) => InvokeVoidAsync("followTerminal", lines);

    /// <summary>UR-31: the clipboard, with a fallback outside secure contexts.</summary>
    public async ValueTask<bool> CopyTextAsync(string text)
    {
        var (answered, copied) = await InvokeAsync<bool>("copyText", text);
        return answered && copied;
    }

    public ValueTask<bool> DownloadTextAsync(string fileName, string text) => InvokeVoidAsync("downloadText", fileName, text);

    /// <summary>UR-34: the browser's IANA time zone, or null.</summary>
    public async ValueTask<string?> TimeZoneAsync()
    {
        var (answered, zone) = await InvokeAsync<string?>("timeZone");
        return answered ? zone : null;
    }

    /// <summary>UR-05: OFFLINE and the connection bar follow Blazor's reconnection states.</summary>
    public ValueTask<bool> WatchConnectionAsync() => InvokeVoidAsync("watchConnection");

    public async ValueTask DisposeAsync()
    {
        Task<IJSObjectReference>? loaded;
        lock (gate)
        {
            loaded = module;
        }

        if (loaded is null)
        {
            return;
        }

        try
        {
            await (await loaded).DisposeAsync();
        }
        catch (Exception exception) when (IsBrowserFailure(exception))
        {
            // The circuit has gone, and the module with it.
        }
    }

    private async ValueTask<bool> InvokeVoidAsync(string identifier, params object?[] args)
    {
        try
        {
            var loaded = await ModuleAsync();
            await loaded.InvokeVoidAsync(identifier, args);
            return true;
        }
        catch (Exception exception) when (IsBrowserFailure(exception))
        {
            logger.BrowserCallFailed(identifier, ExternalText.Clean(exception.Message));
            return false;
        }
    }

    private async ValueTask<(bool Answered, T? Value)> InvokeAsync<T>(string identifier, params object?[] args)
    {
        try
        {
            var loaded = await ModuleAsync();
            return (true, await loaded.InvokeAsync<T>(identifier, args));
        }
        catch (Exception exception) when (IsBrowserFailure(exception))
        {
            logger.BrowserCallFailed(identifier, ExternalText.Clean(exception.Message));
            return (false, default);
        }
    }

    // Blazor resolves the import against the document's base address, so the module loads from Airlock's own root.
    private Task<IJSObjectReference> ModuleAsync()
    {
        lock (gate)
        {
            return module ??= runtime.InvokeAsync<IJSObjectReference>("import", "./" + address).AsTask();
        }
    }

    private static bool IsBrowserFailure(Exception exception) =>
        exception is JSException or JSDisconnectedException or TaskCanceledException or InvalidOperationException;
}