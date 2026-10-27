using UnityEngine;

namespace MururuBlinkLipSync.Editor
{
    // Hand-authored neutral-mouth shape data only. The generated mesh's topology,
    // UVs, skinning and other attributes are reconstructed from the user's Mururu Body.
    internal static class MururuNeutralMouthData
    {
        internal const int VertexCount = 84;
        internal const int TriangleIndexCount = 246;
        internal const float HideSelfDeltaZ = -0.063f;
        internal const float NoneAdjustmentY = 0.000013947487f;

        // The Body's hidden AIUEO surface has the neutral mouth shape, offset
        // backward. After aligning centroids, the largest per-vertex distance to
        // NeutralPositions measured on Mururu / MururuChild / MururuQ was 1.08mm
        // for the correct vertex order, and 2.8mm or more for the tested
        // mismatched orders.
        internal const float ShapeToleranceMeters = 0.002f;

        // NeutralPositions are absolute Body-space positions, so the hidden
        // surface must also sit where it does on standard Mururu / MururuQ.
        // MururuChild measures (0, -836, -45)mm here, which triggers the
        // confirmation warning. The user may still continue.
        internal static readonly Vector3 ExpectedHiddenSurfaceOffset =
            new Vector3(0.000001828f, 0.000042376f, -0.032680541f);

        internal const float OffsetToleranceMeters = 0.003f;

        // Indices are in UV-sorted generated-vertex order.
        internal static readonly int[] NoneAdjustmentIndices = { 13, 55 };

        // Positions are stored in the same deterministic UV-sorted order used when
        // extracting the AIUEO mouth surface from the user's Body.
        internal static readonly Vector3[] NeutralPositions =
        {
            new Vector3(0.0020773015f, 1.3879354f, 0.0633905604f),
            new Vector3(0.00206051348f, 1.3879354f, 0.0633905604f),
            new Vector3(0.00197458873f, 1.3879354f, 0.0633905604f),
            new Vector3(0.00204372546f, 1.3879354f, 0.0633905604f),
            new Vector3(0.00187187595f, 1.3879354f, 0.0633905604f),
            new Vector3(0.00142946607f, 1.3879354f, 0.0634247139f),
            new Vector3(0.00146015047f, 1.3879354f, 0.0634268001f),
            new Vector3(0.000987056294f, 1.3879354f, 0.0634729192f),
            new Vector3(0.000876575417f, 1.3879354f, 0.0634976253f),
            new Vector3(0.000739439332f, 1.3879354f, 0.0635236278f),
            new Vector3(0.00065017608f, 1.38793731f, 0.0635495856f),
            new Vector3(0.000491822779f, 1.38794494f, 0.0635977536f),
            new Vector3(0.000423777063f, 1.38794875f, 0.0636054799f),
            new Vector3(4.27759824e-06f, 1.38796961f, 0.0636319146f),
            new Vector3(-2.90212793e-05f, 1.38796961f, 0.0636319146f),
            new Vector3(-0.000467785139f, 1.38794231f, 0.0635715276f),
            new Vector3(-0.000490920444f, 1.38793993f, 0.0635660365f),
            new Vector3(-0.000687167165f, 1.3879354f, 0.0635250807f),
            new Vector3(-0.000734675385f, 1.3879354f, 0.0635168403f),
            new Vector3(-0.000906548987f, 1.3879354f, 0.0634729192f),
            new Vector3(-0.000978430151f, 1.3879354f, 0.063461937f),
            new Vector3(-0.00147516595f, 1.3879354f, 0.0634103268f),
            new Vector3(-0.00141772558f, 1.3879354f, 0.0634103268f),
            new Vector3(-0.00185702089f, 1.3879354f, 0.0633905604f),
            new Vector3(-0.0020437832f, 1.3879354f, 0.0633905604f),
            new Vector3(-0.00196255231f, 1.3879354f, 0.0633905604f),
            new Vector3(-0.00205593323f, 1.3879354f, 0.0633905604f),
            new Vector3(-0.00206808327f, 1.3879354f, 0.0633905604f),
            new Vector3(0.00309196208f, 1.3880192f, 0.0629829466f),
            new Vector3(0.00312450621f, 1.38828611f, 0.063180685f),
            new Vector3(0.00287593249f, 1.38740599f, 0.0628798082f),
            new Vector3(0.00281219045f, 1.3885529f, 0.0633784533f),
            new Vector3(0.00258564297f, 1.38717151f, 0.0627766401f),
            new Vector3(0.00202129106f, 1.38713932f, 0.062837258f),
            new Vector3(0.0020773015f, 1.3879354f, 0.0633905604f),
            new Vector3(0.00206051348f, 1.3879354f, 0.0633905604f),
            new Vector3(0.00197458873f, 1.3879354f, 0.0633905604f),
            new Vector3(0.00204372546f, 1.3879354f, 0.0633905604f),
            new Vector3(0.00216026371f, 1.38862336f, 0.0636070296f),
            new Vector3(0.00187187595f, 1.3879354f, 0.0633905604f),
            new Vector3(0.00142946607f, 1.3879354f, 0.0634247139f),
            new Vector3(0.00137584854f, 1.38717461f, 0.0628978685f),
            new Vector3(0.00146015047f, 1.3879354f, 0.0634268001f),
            new Vector3(0.000987056294f, 1.3879354f, 0.0634729192f),
            new Vector3(0.00143690221f, 1.38869381f, 0.0638355836f),
            new Vector3(0.000876575417f, 1.3879354f, 0.0634976253f),
            new Vector3(0.00103951432f, 1.38718891f, 0.0629592463f),
            new Vector3(0.000739439332f, 1.3879354f, 0.0635236278f),
            new Vector3(0.00106306956f, 1.38872111f, 0.0639041662f),
            new Vector3(0.00065017608f, 1.38793731f, 0.0635495856f),
            new Vector3(0.000703178113f, 1.38722503f, 0.0630205944f),
            new Vector3(0.000491822779f, 1.38794494f, 0.0635977536f),
            new Vector3(0.000423777063f, 1.38794875f, 0.0636054799f),
            new Vector3(0.000689235283f, 1.38874853f, 0.0639847293f),
            new Vector3(3.05083104e-05f, 1.38725984f, 0.0631418601f),
            new Vector3(4.27759824e-06f, 1.38796961f, 0.0636319146f),
            new Vector3(-2.90212793e-05f, 1.38796961f, 0.0636319146f),
            new Vector3(-5.8431644e-05f, 1.38880324f, 0.064016901f),
            new Vector3(-0.000784311444f, 1.38870788f, 0.0639199615f),
            new Vector3(-0.000467785139f, 1.38794231f, 0.0635715276f),
            new Vector3(-0.000490920444f, 1.38793993f, 0.0635660365f),
            new Vector3(-0.000634865137f, 1.38722348f, 0.062973544f),
            new Vector3(-0.000687167165f, 1.3879354f, 0.0635250807f),
            new Vector3(-0.00114725041f, 1.38866007f, 0.0638264343f),
            new Vector3(-0.000734675385f, 1.3879354f, 0.0635168403f),
            new Vector3(-0.000967550324f, 1.38718069f, 0.062888667f),
            new Vector3(-0.000906548987f, 1.3879354f, 0.0634729192f),
            new Vector3(-0.00151019124f, 1.38861251f, 0.0637319461f),
            new Vector3(-0.000978430151f, 1.3879354f, 0.063461937f),
            new Vector3(-0.00147516595f, 1.3879354f, 0.0634103268f),
            new Vector3(-0.00130023796f, 1.3871417f, 0.0628037602f),
            new Vector3(-0.00141772558f, 1.3879354f, 0.0634103268f),
            new Vector3(-0.00185702089f, 1.3879354f, 0.0633905604f),
            new Vector3(-0.00221073814f, 1.38850272f, 0.0634533241f),
            new Vector3(-0.0020437832f, 1.3879354f, 0.0633905604f),
            new Vector3(-0.00196255231f, 1.3879354f, 0.0633905604f),
            new Vector3(-0.00205593323f, 1.3879354f, 0.0633905604f),
            new Vector3(-0.00206808327f, 1.3879354f, 0.0633905604f),
            new Vector3(-0.00194791681f, 1.3870666f, 0.0626976714f),
            new Vector3(-0.00246605976f, 1.38704669f, 0.0625915825f),
            new Vector3(-0.00289872498f, 1.38839293f, 0.0631746724f),
            new Vector3(-0.00285335048f, 1.38723266f, 0.0625969023f),
            new Vector3(-0.00313072139f, 1.38812053f, 0.0629697666f),
            new Vector3(-0.0030851406f, 1.38784802f, 0.0627648905f),
        };
    }
}
