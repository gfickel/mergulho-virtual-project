using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace MergulhoVirtual.DesignSystem.Tests
{
    /// <summary>
    /// Enforces corner-radius correctness at the stylesheet level.
    /// <para>
    /// UI Toolkit does not resolve an over-large <c>border-radius</c> the way CSS
    /// does. CSS scales every radius by one shared factor, preserving the stadium
    /// shape; UI Toolkit clamps each axis independently
    /// (<c>rx = min(r, width/2)</c>, <c>ry = min(r, height/2)</c>). So the
    /// familiar "just use a huge radius for a pill" idiom silently draws an
    /// ELLIPSE on anything wider than it is tall, and is only correct on a square.
    /// That is exactly how the hero beach selector shipped as a lens.
    /// </para>
    /// <para>These tests pin the three rules that follow from it — see
    /// <c>Tokens/_shape.uss</c> for the full explanation.</para>
    /// </summary>
    public class ShapeDisciplineTests
    {
        static string AssetsPath => Application.dataPath;
        static string DesignSystemPath => Path.Combine(AssetsPath, "DesignSystem");

        // ---- USS parsing -------------------------------------------------

        static readonly Regex Comment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
        static readonly Regex Rule = new(@"([^{}]+)\{([^{}]*)\}", RegexOptions.Compiled);
        static readonly Regex PxValue = new(@"^(-?[0-9]*\.?[0-9]+)px$", RegexOptions.Compiled);
        static readonly Regex TokenUsage = new(@"^var\((--md-sys-shape-corner-[a-z-]+)\)$", RegexOptions.Compiled);

        /// <summary>A parsed USS rule: its selector text and its declarations.</summary>
        class UssRule
        {
            public string File;
            public string Selector;
            public Dictionary<string, string> Declarations;
            public override string ToString() => $"{File}: {Selector}";
        }

        static IEnumerable<UssRule> RulesIn(string file)
        {
            string text = Comment.Replace(File.ReadAllText(file), " ");
            foreach (Match rule in Rule.Matches(text))
            {
                var declarations = new Dictionary<string, string>();
                foreach (var part in rule.Groups[2].Value.Split(';'))
                {
                    int colon = part.IndexOf(':');
                    if (colon <= 0) continue;
                    declarations[part.Substring(0, colon).Trim()] = part.Substring(colon + 1).Trim();
                }
                yield return new UssRule
                {
                    File = Path.GetFileName(file),
                    Selector = Regex.Replace(rule.Groups[1].Value.Trim(), @"\s+", " "),
                    Declarations = declarations,
                };
            }
        }

        /// <summary>Same scan set as TokenDisciplineTests: components, gallery and app screens.</summary>
        static IEnumerable<string> StyleSheets()
        {
            var files = Directory.GetFiles(Path.Combine(DesignSystemPath, "Components"), "*.uss", SearchOption.AllDirectories)
                .Append(Path.Combine(DesignSystemPath, "Gallery", "Gallery.uss"));
            string uiPath = Path.Combine(AssetsPath, "UI");
            if (Directory.Exists(uiPath))
                files = files.Concat(Directory.GetFiles(uiPath, "*.uss", SearchOption.AllDirectories));
            return files;
        }

        /// <summary>Token name -> px, read from the shape scale itself so the tests follow a retune.</summary>
        static Dictionary<string, float> ShapeTokens()
        {
            var tokens = new Dictionary<string, float>();
            string text = Comment.Replace(File.ReadAllText(Path.Combine(DesignSystemPath, "Tokens", "_shape.uss")), " ");
            foreach (Match m in Regex.Matches(text, @"(--md-sys-shape-corner-[a-z-]+)\s*:\s*([^;]+);"))
            {
                string value = m.Groups[2].Value.Trim();
                var px = PxValue.Match(value);
                tokens[m.Groups[1].Value] = px.Success ? float.Parse(px.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) : 0f;
            }
            return tokens;
        }

        static readonly string[] RadiusProperties =
        {
            "border-radius",
            "border-top-left-radius", "border-top-right-radius",
            "border-bottom-left-radius", "border-bottom-right-radius",
        };

        /// <summary>Length in px, or null when the value is not a single px literal or shape token.</summary>
        static float? Length(string value, Dictionary<string, float> tokens)
        {
            if (value == null) return null;
            value = value.Trim();
            var px = PxValue.Match(value);
            if (px.Success)
                return float.Parse(px.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var token = TokenUsage.Match(value);
            if (token.Success && tokens.TryGetValue(token.Groups[1].Value, out float resolved))
                return resolved;
            return null;
        }

        // ---- The rules ---------------------------------------------------

        /// <summary>
        /// corner-full is a CIRCLE token. On a non-square element its 1000px
        /// clamps per-axis to width/2 x height/2 — an ellipse. Every rule that
        /// uses it must therefore declare a width and a height that are equal.
        /// </summary>
        [Test]
        public void CornerFull_IsOnlyUsedOnDeclaredSquares()
        {
            var failures = new List<string>();
            foreach (var file in StyleSheets())
            foreach (var rule in RulesIn(file))
            {
                bool usesFull = RadiusProperties.Any(p =>
                    rule.Declarations.TryGetValue(p, out var v) && v.Contains("--md-sys-shape-corner-full"));
                if (!usesFull) continue;

                rule.Declarations.TryGetValue("width", out string w);
                rule.Declarations.TryGetValue("height", out string h);
                if (w == null || h == null)
                    failures.Add($"{rule} — uses corner-full but does not declare both width and height. " +
                                 "corner-full only produces a circle on a square; a pill needs border-radius: <height/2>px.");
                else if (w != h)
                    failures.Add($"{rule} — uses corner-full on a {w} x {h} element, which renders as an ellipse. " +
                                 "Use border-radius: <height/2>px, or MdShape.KeepStadium if the height is content-driven.");
            }
            Assert.That(failures, Is.Empty,
                "corner-full misuse (see Tokens/_shape.uss):\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// On a non-square element, a radius larger than half its own declared
        /// height cannot render as authored: the vertical axis clamps to
        /// height/2 while the horizontal one keeps growing toward width/2, so
        /// the corners go elliptical.
        /// <para>
        /// Declared squares are exempt and must be: there both clamps land on
        /// the same number, so an over-large radius is the intended perfect
        /// circle (that is the whole legitimate use of corner-full, and
        /// <see cref="CornerFull_IsOnlyUsedOnDeclaredSquares"/> polices it).
        /// </para>
        /// </summary>
        [Test]
        public void NoRadiusExceedsHalfItsOwnDeclaredHeight()
        {
            var tokens = ShapeTokens();
            var failures = new List<string>();
            foreach (var file in StyleSheets())
            foreach (var rule in RulesIn(file))
            {
                if (!rule.Declarations.TryGetValue("height", out string heightValue)) continue;
                float? height = Length(heightValue, tokens);
                if (height == null || height <= 0f) continue;

                // A declared square clamps uniformly -> circle, not ellipse.
                if (rule.Declarations.TryGetValue("width", out string widthValue) &&
                    widthValue == heightValue)
                    continue;

                foreach (var property in RadiusProperties)
                {
                    if (!rule.Declarations.TryGetValue(property, out string radiusValue)) continue;
                    float? radius = Length(radiusValue, tokens);
                    if (radius == null) continue;
                    if (radius > height / 2f + 0.001f)
                        failures.Add($"{rule} — {property}: {radiusValue} on height: {heightValue}. " +
                                     $"Anything over {height / 2f}px clamps per-axis into an ellipse.");
                }
            }
            Assert.That(failures, Is.Empty,
                "radii larger than half their element's height (see Tokens/_shape.uss):\n" +
                string.Join("\n", failures));
        }

        /// <summary>
        /// The pills whose height IS authored in USS carry the literal half of it.
        /// This is the drift guard for the cheap half of the fix: change one of
        /// these heights without changing its radius and the shape stops being a
        /// stadium, which is otherwise only visible in a screenshot.
        /// </summary>
        [TestCase("DesignSystem/Components/MdButton/MdButton.uss", ".md-button__container")]
        [TestCase("DesignSystem/Components/MvHeroHeader/MvHeroHeader.uss", ".mv-hero-header__selector-container")]
        [TestCase("DesignSystem/Components/MdProgress/MdProgress.uss", ".md-linear-progress")]
        [TestCase("DesignSystem/Components/MdBottomSheet/MdBottomSheet.uss", ".md-bottom-sheet__handle")]
        [TestCase("UI/Screens/PraiaDetalheScreen.uss", ".mv-praia__sos")]
        public void AuthoredPill_HasExactlyHalfHeightRadius(string relativePath, string selector)
        {
            var tokens = ShapeTokens();
            var rule = RulesIn(Path.Combine(AssetsPath, relativePath))
                .FirstOrDefault(r => r.Selector == selector);
            Assert.That(rule, Is.Not.Null, $"{selector} not found in {relativePath}");

            Assert.That(rule.Declarations.ContainsKey("height"), Is.True, $"{selector} declares no height");
            Assert.That(rule.Declarations.ContainsKey("border-radius"), Is.True, $"{selector} declares no border-radius");

            float? height = Length(rule.Declarations["height"], tokens);
            float? radius = Length(rule.Declarations["border-radius"], tokens);
            Assert.That(height, Is.Not.Null, $"{selector} height is not a px literal");
            Assert.That(radius, Is.Not.Null, $"{selector} border-radius is not a px literal");
            Assert.That(radius.Value, Is.EqualTo(height.Value / 2f).Within(0.001f),
                $"{selector} is {height}px tall, so a stadium needs border-radius: {height.Value / 2f}px " +
                $"(found {radius}px). See Tokens/_shape.uss.");
        }

        /// <summary>
        /// MvTag is the only pill with no authored height, so it must NOT try to
        /// express its radius in USS — it is pinned at runtime from the resolved
        /// height by MdShape.KeepStadium. Guards against someone "tidying up" by
        /// putting corner-full back on the root.
        /// </summary>
        [Test]
        public void MvTagRoot_LeavesItsRadiusToTheRuntimeHelper()
        {
            var rule = RulesIn(Path.Combine(DesignSystemPath, "Components", "MvTag", "MvTag.uss"))
                .FirstOrDefault(r => r.Selector == ".mv-tag");
            Assert.That(rule, Is.Not.Null, ".mv-tag rule not found");
            foreach (var property in RadiusProperties)
                Assert.That(rule.Declarations.ContainsKey(property), Is.False,
                    $".mv-tag declares {property}. A tag's height is padding + the label's line box, " +
                    "so its radius comes from MdShape.KeepStadium; a USS radius here cannot be a stadium.");
        }
    }
}
