using System.Runtime.CompilerServices;

// Test seams in the UI layer stay internal but visible to its EditMode tests —
// the same pattern as Assets/DesignSystem/AssemblyInfo.cs and
// Assets/Scripts/AssemblyInfo.cs.
//
// What needs one today: ReportScreen.SubmitForTests(). EditMode has no panel, so
// a Clickable cannot be driven with a synthetic pointer event, and the submit
// handler is exactly the piece worth testing (three outcomes, three endings).
[assembly: InternalsVisibleTo("MergulhoVirtual.UI.Tests.Editor")]
