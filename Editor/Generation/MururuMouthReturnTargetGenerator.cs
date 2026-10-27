// Editor-only target generation. Inputs and project assets are never modified.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MururuBlinkLipSync.Editor
{
    internal sealed class MururuMouthReturnTargets
    {
        public int[] SurfaceMapping, Triangles;
        public Vector3[] RawNeutral, Neutral;
        public float NeutralOffsetY, MaxBindingDistance;
        public readonly Dictionary<string, Vector3[]> Targets = new Dictionary<string, Vector3[]>(StringComparer.Ordinal);
        public readonly Dictionary<string, bool> DirectSurface = new Dictionary<string, bool>(StringComparer.Ordinal);
    }

    internal static class MururuMouthReturnTargetGenerator
    {
        internal static bool IsSupportedSourceShape(string name) => Array.IndexOf(Names, name) >= 0;

        internal static HashSet<string> UnknownMouthShapes(Mesh body)
        {
            var a = MouthMotion(body);
            int[] mouth = Enumerable.Range(0, a.Length).Where(i => Math.Abs(a[i].z) > .005f).ToArray();
            var result = new HashSet<string>(StringComparer.Ordinal);
            var delta = new Vector3[body.vertexCount];
            for (int shape = 0; shape < body.blendShapeCount; shape++)
            {
                string name = body.GetBlendShapeName(shape);
                if (MururuMouthReturnFxRoutesPose.Properties.Contains(name)) continue;
                for (int frame = 0; frame < body.GetBlendShapeFrameCount(shape); frame++)
                {
                    body.GetBlendShapeFrameVertices(shape, frame, delta, null, null);
                    if (mouth.Any(i => !Finite(delta[i]) || delta[i].sqrMagnitude > 1e-12f))
                    { result.Add(name); break; }
                }
            }
            return result;
        }

        internal static bool TryGenerate(
            Mesh body,
            out MururuMouthReturnTargets targets,
            out MururuMouthReturnUnsupportedReason reason)
        {
            targets = null;
            if (!body) { reason = MururuMouthReturnUnsupportedReason.MissingBody; return false; }
            // Imported meshes remain accessible in the Editor even with Read/Write
            // disabled. Do not change the importer or require runtime CPU mesh data.
            if (!body.isReadable && !UnityEditor.AssetDatabase.Contains(body))
            { reason = MururuMouthReturnUnsupportedReason.BodyNotReadable; return false; }

            // A changed built-in Neutral is a package defect, not an unsupported
            // purchaser Mesh. Do not hide that exception behind a fallback.
            Vector3[] neutral = MururuNeutralMouthData.NeutralPositions;
            if (neutral == null || neutral.Length != 84 || neutral.Any(p => !Finite(p)))
                throw new InvalidOperationException("Invalid built-in Neutral mouth data.");
            try { targets = GenerateValidated(body, neutral); }
            catch (ArgumentException) { reason = MururuMouthReturnUnsupportedReason.UnrecognizedContent; return false; }
            if (targets.Targets.Count == 0) { targets = null; reason = MururuMouthReturnUnsupportedReason.UnrecognizedContent; return false; }
            reason = MururuMouthReturnUnsupportedReason.None;
            return true;
        }

        private static readonly string[] Names = {
            "mouth_A", "mouth_I", "mouth_U", "mouth_E", "mouth_O", "mouth_good", "mouth_good2",
            "mouth_excited", "mouth_bad", "mouth_impassive", "mouth_serious", "mouth_grin",
            "mouth_smirk", "mouth_cynical", "mouth_pout", "mouth_angry", "mouth_clench", "mouth_circle",
            "mouth_triangle", "mouth_square", "mouth_square2", "mouth_malon", "mouth_drool", "mouth_oops",
            "mouth_tongue", "mouth_tongue_good" };

        // Topology correspondence only: ranks in the user's own UV-sorted vertices.
        // Line ranks use the smallest UV of every group of seam copies.
        static readonly int[] LineRing = { 8, 6, 4, 3, 1, 0, 2, 5, 7, 9 };
        static readonly int[] SurfaceRing = { 79, 78, 70, 65, 61, 54, 50, 46, 41, 33, 32, 30, 28, 29,
            31, 38, 44, 48, 53, 57, 58, 63, 67, 73, 80, 82, 83, 81 };
        struct Binding { public int A, B, C; public Vector3 W; public float Distance; }

        // Validate the geometry used by each endpoint, independently of unrelated Body content.
        private static MururuMouthReturnTargets GenerateValidated(Mesh body, Vector3[] authoredNeutral)
        {
            if (body == null || authoredNeutral == null || authoredNeutral.Length != 84)
                throw new ArgumentException("Expected Body and authored Neutral84.");
            var vertices = body.vertices; var uv = body.uv;
            if (uv.Length != vertices.Length) throw new ArgumentException("Invalid mesh data.");
            var aDelta = MouthMotion(body);
            var mapping = Enumerable.Range(0, vertices.Length).Where(i => aDelta[i].z > .005f)
                .OrderBy(i => uv[i].x).ThenBy(i => uv[i].y).ThenBy(i => i).ToArray();
            if (mapping.Length != 84) throw new ArgumentException("Expected 84 AIUEO vertices.");
            // Seam copies are identical in position; use exact equality, never merge
            // almost-equal unrelated vertices. Modified seam deltas are rejected below.
            var lineGroups = Enumerable.Range(0, vertices.Length).Where(i => aDelta[i].z < -.005f)
                .GroupBy(i => vertices[i]).Select(g => g.OrderBy(i => uv[i].x).ThenBy(i => uv[i].y).ThenBy(i => i).ToArray())
                .OrderBy(g => uv[g[0]].x).ThenBy(g => uv[g[0]].y).ToArray();
            var line = lineGroups.Select(g => g[0]).ToArray();
            var lineLookup = new Dictionary<int, int>();
            for (int i = 0; i < lineGroups.Length; ++i) foreach (int v in lineGroups[i]) lineLookup.Add(v, i);
            var surfaceLookup = mapping.Select((v,i) => new {v,i}).ToDictionary(p => p.v, p => p.i);
            var surfaceTriangles = new List<int>(); var lineTriangles = new List<int>();
            for (int sm = 0; sm < body.subMeshCount; ++sm)
            {
                var triangles = body.GetTriangles(sm);
                for (int j = 0; j < triangles.Length; j += 3)
                {
                    int x = triangles[j], y = triangles[j+1], z = triangles[j+2];
                    if (surfaceLookup.ContainsKey(x) && surfaceLookup.ContainsKey(y) && surfaceLookup.ContainsKey(z))
                        surfaceTriangles.AddRange(new[] { surfaceLookup[x], surfaceLookup[y], surfaceLookup[z] });
                    if (lineLookup.ContainsKey(x) && lineLookup.ContainsKey(y) && lineLookup.ContainsKey(z))
                        lineTriangles.AddRange(new[] { lineLookup[x], lineLookup[y], lineLookup[z] });
                }
            }
            if (surfaceTriangles.Count != 246 || mapping.Any(i => !Finite(vertices[i]))) throw new ArgumentException("Unsupported mouth topology.");
            var neutral = (Vector3[])authoredNeutral.Clone();
            var linePoints = line.Select(i => vertices[i]).ToArray();
            Binding[] bindings = null; double[] surfaceP = null; int[] anchors = null;
            bool ribbon = lineGroups.Length == 10 && lineTriangles.Count == 24 && linePoints.All(Finite)
                && HasBoundary(lineTriangles, LineRing, linePoints)
                && HasBoundary(surfaceTriangles, SurfaceRing, mapping.Select(i => vertices[i]).ToArray());
            if (ribbon) try
            {
                bindings = neutral.Select(p => Bind(p, linePoints, lineTriangles)).ToArray();
                surfaceP = Perimeter(SurfaceRing.Select(i => neutral[i]).ToArray());
                anchors = Anchors(surfaceP, Perimeter(LineRing.Select(i => linePoints[i]).ToArray()));
            }
            catch (ArgumentException) { ribbon = false; }
            var bank = new MururuMouthReturnTargets { SurfaceMapping = mapping, Triangles = surfaceTriangles.ToArray(),
                RawNeutral = neutral, MaxBindingDistance = bindings == null ? 0 : bindings.Max(b => b.Distance) };
            foreach (string name in new[] { "default" }.Concat(Names))
            {
                Vector3[] delta;
                try { delta = name == "default" ? new Vector3[vertices.Length] : Delta(body, name); }
                catch (ArgumentException) { continue; }
                bool direct = mapping.Max(i => vertices[i].z + delta[i].z) > .05f;
                if (!direct && (!ribbon || lineGroups.Any(group =>
                    group.Any(i => Vector3.Distance(delta[i], delta[group[0]]) > 1e-6f)))) continue;
                var target = new Vector3[84];
                for (int j = 0; j < target.Length; ++j)
                {
                    if (direct) target[j] = vertices[mapping[j]] + delta[mapping[j]];
                    else
                    {
                        var b = bindings[j];
                        target[j] = neutral[j] + delta[line[b.A]]*b.W.x + delta[line[b.B]]*b.W.y + delta[line[b.C]]*b.W.z;
                    }
                }
                if (!direct)
                    for (int j = 0; j < 10; ++j)
                    {
                        int start = anchors[j], stop = j+1 < 10 ? anchors[j+1] : 28;
                        for (int k = start; k < stop; ++k)
                        {
                            double denominator = (stop < 28 ? surfaceP[stop] : 1) - surfaceP[start];
                            float t = (float)((surfaceP[k]-surfaceP[start])/denominator);
                            int a = line[LineRing[j]], b = line[LineRing[(j+1)%10]];
                            target[SurfaceRing[k]] = Vector3.LerpUnclamped(vertices[a]+delta[a], vertices[b]+delta[b], t);
                        }
                    }
                if (target.All(Finite)) { bank.Targets.Add(name, target); bank.DirectSurface.Add(name, direct); }
            }
            if (bank.Targets.TryGetValue("default", out var defaultTarget))
                bank.NeutralOffsetY = (defaultTarget.Min(p=>p.y)+defaultTarget.Max(p=>p.y)-neutral.Min(p=>p.y)-neutral.Max(p=>p.y))*.5f;
            bank.Neutral = neutral.Select(p => p + Vector3.up*bank.NeutralOffsetY).ToArray();
            return bank;
        }

        static bool HasBoundary(List<int> triangles, int[] ring, Vector3[] positions)
        {
            int[] weld = positions.Select(p => Array.IndexOf(positions, p)).ToArray();
            Func<int, int, long> edge = (a, b) => ((long)Math.Min(weld[a], weld[b]) << 32) | (uint)Math.Max(weld[a], weld[b]);
            var counts = new Dictionary<long, int>();
            for (int i = 0; i < triangles.Count; i += 3)
                for (int j = 0; j < 3; j++)
                {
                    long key = edge(triangles[i+j], triangles[i+(j+1)%3]);
                    counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
                }
            if (counts.Values.Any(n => n > 2)) return false;
            var expected = new HashSet<long>(Enumerable.Range(0, ring.Length).Select(i => edge(ring[i], ring[(i+1)%ring.Length])));
            return expected.SetEquals(counts.Where(p => p.Value == 1).Select(p => p.Key));
        }

        static Vector3[] MouthMotion(Mesh body)
        {
            Vector3[] surfaceOnly = null;
            foreach (string name in Names.Take(5))
            {
                Vector3[] delta;
                try { delta = Delta(body, name); }
                catch (ArgumentException) { continue; }
                if (delta.Count(p => p.z > .005f) != 84) continue;
                if (delta.Any(p => p.z < -.005f)) return delta;
                if (surfaceOnly == null) surfaceOnly = delta;
            }
            return surfaceOnly ?? throw new ArgumentException("No usable mouth surface motion.");
        }

        static Vector3[] Delta(Mesh body, string name)
        {
            int index = body.GetBlendShapeIndex(name);
            if (index < 0 || body.GetBlendShapeFrameCount(index) != 1 || body.GetBlendShapeFrameWeight(index,0) != 100)
                throw new ArgumentException("Expected one 100% frame: " + name);
            var d = new Vector3[body.vertexCount];
            body.GetBlendShapeFrameVertices(index,0,d,new Vector3[d.Length],new Vector3[d.Length]);
            if (d.Any(p => !Finite(p))) throw new ArgumentException("Non-finite shape: " + name);
            return d;
        }
        static bool Finite(Vector3 p) => !(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.x)||float.IsInfinity(p.y)||float.IsInfinity(p.z));
        static double Dot(Vector2 a, Vector2 b) => (double)a.x*b.x+(double)a.y*b.y;
        static Binding Bind(Vector3 p, Vector3[] points, List<int> triangles)
        {
            var best = new Binding(); double bestDistance = double.PositiveInfinity;
            for (int i = 0; i < triangles.Count; i+=3)
            {
                int a=triangles[i], b=triangles[i+1], c=triangles[i+2];
                Vector2 q=p, x=points[a], y=points[b], z=points[c], e=y-x, f=z-x, h=q-x;
                double det=(double)e.x*f.y-(double)e.y*f.x;
                var candidates = new List<Vector3>();
                if (Math.Abs(det)>1e-20)
                {
                    double v=((double)h.x*f.y-(double)h.y*f.x)/det, w=((double)e.x*h.y-(double)e.y*h.x)/det;
                    if (v>=0 && w>=0 && v+w<=1) candidates.Add(new Vector3((float)(1-v-w),(float)v,(float)w));
                }
                var ps=new[]{x,y,z};
                for (int k=0;k<3;++k)
                {
                    int l=(k+1)%3; var edge=ps[l]-ps[k]; double den=Dot(edge,edge);
                    if (den<=0) continue;
                    float t=(float)Math.Max(0,Math.Min(1,Dot(q-ps[k],edge)/den));
                    Vector3 weights=Vector3.zero; weights[k]=1-t; weights[l]=t; candidates.Add(weights);
                }
                foreach (Vector3 weights in candidates)
                {
                    Vector2 projected=x*weights.x+y*weights.y+z*weights.z;
                    double distance=Dot(projected-q,projected-q);
                    if (distance < bestDistance) { bestDistance=distance; best=new Binding{A=a,B=b,C=c,W=weights,Distance=(float)Math.Sqrt(distance)}; }
                }
            }
            if (double.IsInfinity(bestDistance)) throw new ArgumentException("Degenerate ribbon.");
            return best;
        }
        static double[] Perimeter(Vector3[] points)
        {
            double[] lengths=new double[points.Length], p=new double[points.Length]; double total=0;
            for(int i=0;i<points.Length;++i) { var d=points[(i+1)%points.Length]-points[i]; lengths[i]=Math.Sqrt((double)d.x*d.x+(double)d.y*d.y+(double)d.z*d.z); total+=lengths[i]; }
            if(total<=0) throw new ArgumentException("Zero perimeter.");
            for(int i=1;i<points.Length;++i) p[i]=p[i-1]+lengths[i-1]/total;
            return p;
        }
        static int[] Anchors(double[] surface, double[] line)
        {
            int n=surface.Length,k=line.Length; var costs=new double[k,n];var previous=new int[k,n];
            for(int j=0;j<k;++j)for(int i=0;i<n;++i)costs[j,i]=double.PositiveInfinity;
            costs[0,0]=0;
            for(int j=1;j<k;++j)for(int i=j;i<n-(k-1-j);++i)
                for(int p=0;p<i;++p)
                {
                    double c=costs[j-1,p]+Math.Pow(surface[i]-line[j],2);
                    if(c<costs[j,i]) {costs[j,i]=c;previous[j,i]=p;}
                }
            int end=0;for(int i=1;i<n;++i)if(costs[k-1,i]<costs[k-1,end])end=i;
            if(double.IsInfinity(costs[k-1,end]))throw new ArgumentException("No monotone boundary mapping.");
            var result=new int[k];result[k-1]=end;
            for(int j=k-1;j>0;--j)result[j-1]=previous[j,result[j]];
            return result;
        }
    }
}
