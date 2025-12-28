using System.Numerics;
using UnityEngine;

namespace CGProject
{
    /// <summary>
    /// Script responsible for the generation of octahedrons to be later
    /// used as each particle for semi-lagrangian fluid simulation
    /// </summary>
    public static class FluidGenerator
    {
        static readonly int[] edgeVertex =
        {
            // Top to Middle
            0,1, 0,2, 0,3, 0,4,

            // Middle connections
            1,2, 2,3, 3,4, 4,1,

            // Bottom to Middle
            5,1, 5,2, 5,3, 5,4,
        };
        static readonly int[] faceEdges =
        {
            // Top faces (pointing up)
            0,1,4,
            1,2,5,
            2,3,6,
            3,0,7,

            // Bottom faces (pointing down)
            8,9,4,
            9,10,5,
            10,11,6,
            11,8,7,
        };
        static readonly Vector3[] baseVert =
        {
            Vector3.up,
            Vector3.left,
            Vector3.back,
            Vector3.forward,
            Vector3.right,
            Vector3.down,
        };
        public static Mesh MeshGenerator(int res)
        {
            Mesh mesh = new Mesh();

            // Depending on the "res" picked, there will be more divisions
            // On the triangles of the mesh, which is increasing resolution
            int numDiv = MathF.Max(0, res);
            int triangleSize = numDiv + 2;

            // Triangular number formula
            int numVertFaces = triangleSize * (triangleSize + 1) / 2;

            return mesh;
        }
    }
}