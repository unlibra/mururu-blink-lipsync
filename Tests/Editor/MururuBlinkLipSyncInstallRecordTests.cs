using NUnit.Framework;
using UnityEngine;

namespace MururuBlinkLipSync.Editor.Tests
{
    internal sealed class MururuBlinkLipSyncInstallRecordTests
    {
        private GameObject go;

        [TearDown]
        public void TearDown()
        {
            if (go != null)
                Object.DestroyImmediate(go);
        }

        // -1 marks records whose mouth slot was never computed.
        // MururuLipSyncMaterialSlots relies on it to fall back to slot 0.
        [Test]
        public void NewRecord_MouthMaterialIndexIsUncomputed()
        {
            go = new GameObject("MururuBlinkLipSync");

            MururuBlinkLipSyncInstallRecord record =
                go.AddComponent<MururuBlinkLipSyncInstallRecord>();

            Assert.AreEqual(-1, record.mouthMaterialIndex);
        }
    }
}
