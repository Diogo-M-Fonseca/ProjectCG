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
            Vector3.front,
            Vector3.right,
            Vector3.down,
        };
    }
}