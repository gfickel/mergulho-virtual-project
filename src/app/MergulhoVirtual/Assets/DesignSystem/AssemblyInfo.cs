using System.Runtime.CompilerServices;

// Test seams (e.g. MdCircularProgress.Arc angles) stay internal but visible to
// the design-system test assemblies — same pattern as Assets/Scripts/AssemblyInfo.cs.
[assembly: InternalsVisibleTo("MergulhoVirtual.DesignSystem.Tests.Editor")]
[assembly: InternalsVisibleTo("MergulhoVirtual.DesignSystem.Tests.Runtime")]
