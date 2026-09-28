using NUnit.Framework;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Tests
{
    public class MdSnackbarTests
    {
        [Test]
        public void Hierarchy_HasLabelAndActionButton()
        {
            var snackbar = new MdSnackbar();
            Assert.That(snackbar.ClassListContains(MdSnackbar.UssClassName));
            var label = snackbar.Q<Label>(className: MdSnackbar.LabelClassName);
            Assert.That(label, Is.Not.Null);
            Assert.That(label.pickingMode, Is.EqualTo(PickingMode.Ignore),
                "label must not intercept taps (non-modal surface)");
            Assert.That(snackbar.ActionButton.parent, Is.EqualTo(snackbar));
            Assert.That(snackbar.ActionButton.ClassListContains(MdSnackbar.ActionClassName));
        }

        [Test]
        public void Message_RoundTrips()
        {
            var snackbar = new MdSnackbar { Message = "Avistamento enviado." };
            Assert.That(snackbar.Message, Is.EqualTo("Avistamento enviado."));
            Assert.That(snackbar.Q<Label>(className: MdSnackbar.LabelClassName).text,
                Is.EqualTo("Avistamento enviado."));

            snackbar.Message = null;
            Assert.That(snackbar.Message, Is.EqualTo(""));
        }

        [Test]
        public void ActionText_TogglesButtonAndClass()
        {
            var snackbar = new MdSnackbar();
            Assert.That(snackbar.ActionButton.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(snackbar.ClassListContains(MdSnackbar.WithActionClassName), Is.False);

            snackbar.ActionText = "Tentar agora";
            Assert.That(snackbar.ActionButton.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(snackbar.ActionButton.Text, Is.EqualTo("Tentar agora"));
            Assert.That(snackbar.ClassListContains(MdSnackbar.WithActionClassName));

            snackbar.ActionText = "";
            Assert.That(snackbar.ActionButton.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(snackbar.ClassListContains(MdSnackbar.WithActionClassName), Is.False);
        }

        [Test]
        public void Close_RaisesClosedExactlyOnce()
        {
            var snackbar = new MdSnackbar();
            int closed = 0;
            snackbar.Closed += () => closed++;
            snackbar.Close();
            snackbar.Close();
            Assert.That(closed, Is.EqualTo(1));
        }
    }
}
