using NUnit.Framework;

namespace MururuBlinkLipSync.Editor.Tests
{
    // Guards the hand-authored data against edits that break its own constants.
    internal sealed class MururuNeutralMouthDataTests
    {
        [Test]
        public void NeutralPositions_MatchVertexCount()
        {
            Assert.AreEqual(
                MururuNeutralMouthData.VertexCount,
                MururuNeutralMouthData.NeutralPositions.Length);
        }

        [Test]
        public void TriangleIndexCount_IsMultipleOfThree()
        {
            Assert.AreEqual(0, MururuNeutralMouthData.TriangleIndexCount % 3);
        }

        [Test]
        public void NoneAdjustmentIndices_AreInRange()
        {
            foreach (int index in MururuNeutralMouthData.NoneAdjustmentIndices)
            {
                Assert.That(
                    index,
                    Is.InRange(0, MururuNeutralMouthData.VertexCount - 1));
            }
        }
    }
}
