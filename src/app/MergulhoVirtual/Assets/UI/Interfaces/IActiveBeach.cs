using System;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// "Which beach is the user at right now" — the read side of the beach the
    /// app considers active: whatever GPS resolved, or the manual override if one
    /// is set (<see cref="IBeachOverride"/> is the write side of the same state).
    ///
    /// <para>This is what the Praias landing's "Você está em" card reports, so it
    /// must be the honest answer and nothing else. It is deliberately NOT
    /// <c>IConditionsService.Current.BeachName</c>: the conditions service falls
    /// back to a default beach when GPS has nothing, which is fine for fetching a
    /// forecast and a lie under the words "você está em". <b>Null is a real,
    /// common value</b> — the user is off every polygon, GPS has not fixed yet, or
    /// there is no GPS at all — and the screen must have a state for it rather
    /// than inventing a beach.</para>
    /// </summary>
    public interface IActiveBeach
    {
        /// <summary>
        /// places.json <c>name</c> key of the active beach, or null when none is
        /// resolved. A lookup key, never a label — render
        /// <see cref="BeachInfo.DisplayName"/>.
        /// </summary>
        string ActiveBeachKey { get; }

        /// <summary>Raised with the new key (or null) whenever it changes.</summary>
        event Action<string> ActiveBeachChanged;
    }
}
