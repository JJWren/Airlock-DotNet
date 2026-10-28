using Airlock.Web.State;
using Microsoft.AspNetCore.Components;

namespace Airlock.Web.Components.Parts;

/// <summary>
/// U5-NFR-02: a component that shows part of the tab's session and redraws, coalesced to four times a second, only for
/// the kinds of change it names. Public, as Razor makes every component (refinement 1); what it shares of the session is
/// visible to components only.
/// </summary>
public abstract class SessionComponent : ComponentBase, IDisposable
{
    private RenderCoalescer? coalescer;

    [Inject]
    private protected ScanSession Session { get; set; } = default!;

    [Inject]
    private protected TimeProvider Clock { get; set; } = default!;

    private protected SessionState State => Session.State;

    /// <summary>The kinds of change this component redraws for.</summary>
    private protected abstract SessionChange RedrawsFor { get; }

    public override Task SetParametersAsync(ParameterView parameters)
    {
        // Injection happens before the first parameters, so the subscription needs no OnInitialized of its own.
        if (coalescer is null)
        {
            coalescer = new RenderCoalescer(Clock, () => InvokeAsync(StateHasChanged));
            Session.Changed += OnSessionChanged;
        }

        return base.SetParametersAsync(parameters);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && coalescer is not null)
        {
            Session.Changed -= OnSessionChanged;
            coalescer.Dispose();
        }
    }

    private void OnSessionChanged(SessionChange change)
    {
        if ((change & RedrawsFor) != 0)
        {
            coalescer?.Request();
        }
    }
}