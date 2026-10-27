using System.Collections.Generic;
using UnityEngine;

namespace MururuBlinkLipSync.Editor
{
    internal sealed class MururuGeneratedAssetSet : ScriptableObject
    {
        public int schema = 1;
        public string folderGuid;
        public MururuGeneratedRoot root;
        public List<Object> assets = new List<Object>();
    }
}
