using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MururuBlinkLipSync.Editor
{
    // Unattached outputs. Dispose only when discarding them, never after adoption.
    internal sealed class MururuMouthReturnControllerBundle : IDisposable
    {
        readonly List<Object> objects = new List<Object>();
        internal IReadOnlyList<Object> OwnedObjects => objects.AsReadOnly();
        internal AnimatorControllerLayer PhaseLayer { get; set; }
        internal MururuMouthReturnFxRoutesPlan Plan { get; set; }
        internal IReadOnlyCollection<string> BridgeShapes { get; set; }
        internal T Own<T>(T value) where T : Object { objects.Add(value); return value; }
        public void Dispose()
        {
            foreach (var value in objects.AsEnumerable().Reverse()) if (value) Object.DestroyImmediate(value);
            objects.Clear();
        }
    }
}
