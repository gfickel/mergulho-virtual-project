namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Persisted first-run state. The only thing it carries today is whether the
    /// Início welcome card (Tela 6) has been dismissed, but it is deliberately
    /// named for the concern and not for the card, so later one-shot coach marks
    /// land here instead of growing a second preferences seam.
    ///
    /// Engine-free on purpose: the ViewModel must stay plain C# and unit-testable,
    /// so the PlayerPrefs read/write lives in an Assembly-CSharp adapter (see
    /// UiServiceAdapters) keyed by <see cref="OnboardingPrefKeys"/>.
    /// </summary>
    public interface IOnboardingState
    {
        /// <summary>True once the user has closed the Início welcome card.</summary>
        bool WelcomeDismissed { get; }

        /// <summary>Records the dismissal; must persist across app restarts.</summary>
        void DismissWelcome();
    }

    /// <summary>
    /// The single place the persistence keys are spelled. The adapter reads/writes
    /// these; nothing else should hard-code the strings.
    /// </summary>
    public static class OnboardingPrefKeys
    {
        /// <summary>PlayerPrefs int (0/1) — Início welcome card dismissed.</summary>
        public const string WelcomeDismissed = "mv.onboarding.home-welcome-dismissed.v1";
    }
}
