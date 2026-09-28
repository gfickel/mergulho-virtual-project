using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace MergulhoVirtual.DesignSystem.Tests
{
    /// <summary>
    /// Enforces the design-system contract at the stylesheet level, so the
    /// designer can re-theme by touching tokens only:
    ///  - light and dark themes define the exact same variable set,
    ///  - every var(--md-*) used by a component resolves to a defined token,
    ///  - no hard-coded colors in component USS (rgba(0,0,0,0) is the one
    ///    allowed literal, for "transparent").
    /// </summary>
    public class TokenDisciplineTests
    {
        static string DesignSystemPath => Path.Combine(Application.dataPath, "DesignSystem");

        static readonly Regex VarDefinition = new(@"(--md-[a-z0-9-]+)\s*:", RegexOptions.Compiled);
        static readonly Regex VarUsage = new(@"var\((--md-[a-z0-9-]+)\)", RegexOptions.Compiled);
        static readonly Regex HexColor = new(@"#[0-9a-fA-F]{3,8}\b", RegexOptions.Compiled);
        static readonly Regex RgbaLiteral = new(@"rgba?\(([^)]*)\)", RegexOptions.Compiled);

        static HashSet<string> DefinitionsIn(params string[] files) =>
            files.SelectMany(f => VarDefinition.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
                 .ToHashSet();

        /// <summary>
        /// Token files that are swapped per theme: the generated M3 color roles plus the
        /// hand-maintained brand extensions (success/warning — see DESIGN_IMPLEMENTATION §3.2).
        /// </summary>
        static string[] ThemeColorFiles(string colorTheme) => new[]
        {
            Path.Combine(DesignSystemPath, "Tokens", $"_colors-{colorTheme}.uss"),
            Path.Combine(DesignSystemPath, "Tokens", $"_brand-{colorTheme}.uss"),
        };

        static string[] TokenFiles(string colorTheme) => ThemeColorFiles(colorTheme).Concat(new[]
        {
            Path.Combine(DesignSystemPath, "Tokens", "_shape.uss"),
            Path.Combine(DesignSystemPath, "Tokens", "_state.uss"),
            Path.Combine(DesignSystemPath, "Tokens", "_motion.uss"),
        }).ToArray();

        static string UiScreensPath => Path.Combine(Application.dataPath, "UI");

        static IEnumerable<string> ComponentUssFiles()
        {
            var files = Directory.GetFiles(Path.Combine(DesignSystemPath, "Components"), "*.uss", SearchOption.AllDirectories)
                .Append(Path.Combine(DesignSystemPath, "Gallery", "Gallery.uss"));
            // App-layer screens (Assets/UI) follow the same token discipline as components.
            if (Directory.Exists(UiScreensPath))
                files = files.Concat(Directory.GetFiles(UiScreensPath, "*.uss", SearchOption.AllDirectories));
            return files;
        }

        [Test]
        public void LightAndDarkThemes_DefineIdenticalTokenSets()
        {
            var light = DefinitionsIn(ThemeColorFiles("light"));
            var dark = DefinitionsIn(ThemeColorFiles("dark"));
            Assert.That(light.SetEquals(dark), Is.True,
                "themes drifted — only in light: [" + string.Join(", ", light.Except(dark)) +
                "], only in dark: [" + string.Join(", ", dark.Except(light)) + "]");
            Assert.That(light, Is.Not.Empty);
        }

        [Test]
        public void EveryVarUsedByComponents_IsDefinedInTokens()
        {
            var defined = DefinitionsIn(TokenFiles("light"));
            var failures = new List<string>();
            foreach (var file in ComponentUssFiles())
            {
                foreach (Match m in VarUsage.Matches(File.ReadAllText(file)))
                {
                    if (!defined.Contains(m.Groups[1].Value))
                        failures.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
                }
            }
            Assert.That(failures, Is.Empty, "undefined tokens referenced:\n" + string.Join("\n", failures));
        }

        [Test]
        public void ComponentUss_HasNoHardcodedColors()
        {
            var failures = new List<string>();
            foreach (var file in ComponentUssFiles())
            {
                string text = File.ReadAllText(file);
                foreach (Match m in HexColor.Matches(text))
                    failures.Add($"{Path.GetFileName(file)}: {m.Value}");
                foreach (Match m in RgbaLiteral.Matches(text))
                {
                    // rgba(0, 0, 0, 0) = transparent is the single allowed literal.
                    var args = m.Groups[1].Value.Split(',').Select(s => s.Trim()).ToArray();
                    if (!(args.Length == 4 && args.All(a => a == "0")))
                        failures.Add($"{Path.GetFileName(file)}: {m.Value}");
                }
            }
            Assert.That(failures, Is.Empty,
                "hard-coded colors in component USS (use tokens):\n" + string.Join("\n", failures));
        }

        [Test]
        public void ThemeFiles_ImportTheSameComponentStylesheets()
        {
            var import = new Regex("@import url\\(\"([^\"]+)\"\\);");
            // _colors-* and _brand-* are the per-theme token files; everything else must match.
            string Normalize(string path) =>
                string.Join("\n", import.Matches(File.ReadAllText(Path.Combine(DesignSystemPath, path)))
                    .Select(m => m.Groups[1].Value)
                    .Where(u => !u.Contains("_colors-") && !u.Contains("_brand-")));
            Assert.That(Normalize("Theme-Light.tss"), Is.EqualTo(Normalize("Theme-Dark.tss")),
                "Theme-Light.tss and Theme-Dark.tss import lists drifted " +
                "(they must differ only in _colors-* and _brand-*)");

            // ...and each theme must actually import its own side of those two.
            foreach (var theme in new[] { "light", "dark" })
            {
                string text = File.ReadAllText(Path.Combine(DesignSystemPath, $"Theme-{char.ToUpper(theme[0])}{theme.Substring(1)}.tss"));
                foreach (var token in new[] { $"_colors-{theme}.uss", $"_brand-{theme}.uss" })
                    Assert.That(text, Does.Contain(token), $"Theme-{theme} does not import {token}");
            }
        }
    }
}
