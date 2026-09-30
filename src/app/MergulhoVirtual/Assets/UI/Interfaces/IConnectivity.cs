namespace MergulhoVirtual.UI
{
    /// <summary>
    /// "Does this device have a network link right now" — the one bit that decides
    /// whether a failed load is reported as offline or as an error.
    ///
    /// <para><b>It is a link check, not a reachability check.</b> The implementation
    /// wraps <c>Application.internetReachability</c>, which answers "is there a
    /// carrier or Wi-Fi attached", not "can we reach the backend". So
    /// <c>IsOnline == true</c> alongside a failed fetch is a perfectly ordinary
    /// combination (captive portal, DNS, the API being down) and the copy for that
    /// case must not claim the connection is fine — it says the load failed and
    /// stops there.</para>
    ///
    /// <para><b>Poll it; there is no event.</b> Unity exposes no reachability
    /// callback, so a change is only ever observed by asking. Callers read it while
    /// rendering, and the app shell's existing ~60 s tick re-renders the screens
    /// that care. Adding a polling timer here would be a second mechanism for the
    /// same job.</para>
    /// </summary>
    public interface IConnectivity
    {
        /// <summary>False when the device reports no network link at all.</summary>
        bool IsOnline { get; }
    }
}
