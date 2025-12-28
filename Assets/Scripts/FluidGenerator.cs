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
            int numDiv = Mathf.Max(0, res);
            int triangleSize = numDiv + 2;

            // Triangular number formula
            int numVertFaces = triangleSize * (triangleSize + 1) / 2;

            // Total vertices calculation
            int totalVertices = numVertFaces * 8 - (numDiv + 2) * 12 + 6;

            int trisFace = (numDiv + 1) * (numDiv + 1);

            // trisFace * 8 * 3 because 8 sides of a octahedron and 3 sides of a triangle
            int totalIndex = trisFace * 8 * 3;

            Vector3[] vertices = new Vector3[totalVertices];
            int[] triangles = new int[totalIndex];


            // Adding the vertices to the array
            for (int i = 0; i < 6; i++) 
            {
                vertices[i] = baseVert[i];
            }
            int nextVertIndex = 6;
            int triangleIndex = 0;

            // Creating edges
            Edge[] edges = new Edge[12];
            for (int i = 0; i < edgeVertex.Length; i += 2) 
            {
                int startIdx = edgeVertex[i];
                int endIdx = edgeVertex[i + 1];
                Vector3 start = vertices[startIdx];
                Vector3 end = vertices[endIdx];
            
                // Array for all vertices along this edge
                int[] edgeVerts = new int[numDiv + 2];
                edgeVerts[0] = startIdx;
            
                // Adding subdivided vertices
                for (int div = 0; div < numDiv; div++) 
                {
                    float t = (div + 1f) / (numDiv + 1f);
                    edgeVerts[div + 1] = nextVertIndex;
                    vertices[nextVertIndex] = Vector3.Slerp(start, end, t);
                    nextVertIndex++;
                }
                edgeVerts[numDiv + 1] = endIdx;
                edges[i / 2] = new Edge(edgeVerts);
            }

            for (int face = 0; face < 8; face++)
            {
                Edge sideA = edges[faceEdges[face * 3]];
                Edge sideB = edges[faceEdges[face * 3 + 1]];
                Edge bottom = edges[faceEdges[face * 3 + 2]];
                
                // Bottom faces need reversed winding
                bool reverse = face >= 4;
                
                CreateFace(sideA, sideB, bottom, reverse, vertices, triangles, 
                ref nextVertIndex, ref triangleIndex,
                numDiv, numVertFaces);
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();

            return mesh;
        }

        // Sebastian Lague's CreateFace function
        // Builds the vertex map for the octahedrons
        // Fills in interior vertices (sideA, sideB, bottom)
        // Generates the particles
        static void CreateFace(Edge sideA, Edge sideB, Edge bottom, bool reverse,
            Vector3[] vertices, int[] triangles, ref int vertexIndex, ref int triangleIndex, // Track positions
            int numDivisions, int numVertsPerFace)
        {
            int numPointsInEdge = sideA.vertexIndices.Length;
            int[] vertexMap = new int[numVertsPerFace];
            int vertexMapIndex = 0;
            
            // Top vertex
            vertexMap[vertexMapIndex++] = sideA.vertexIndices[0];
            
            for (int i = 1; i < numPointsInEdge - 1; i++)
            {
                // Side A vertex
                vertexMap[vertexMapIndex++] = sideA.vertexIndices[i];
                
                // Adding inner vertices
                Vector3 sideAVertex = vertices[sideA.vertexIndices[i]];
                Vector3 sideBVertex = vertices[sideB.vertexIndices[i]];
                int numInnerPoints = i - 1;
                
                for (int j = 0; j < numInnerPoints; j++)
                {
                    float t = (j + 1f) / (numInnerPoints + 1f);
                    vertexMap[vertexMapIndex++] = vertexIndex;
                    vertices[vertexIndex] = Vector3.Slerp(sideAVertex, sideBVertex, t);
                    vertexIndex++;
                }
                
                // Side B vertex
                vertexMap[vertexMapIndex++] = sideB.vertexIndices[i];
            }
            
            // Add bottom edge vertices
            for (int i = 0; i < numPointsInEdge; i++)
            {
                vertexMap[vertexMapIndex++] = bottom.vertexIndices[i];
            }
            
            // Triangulate (same logic, but writing to triangles array)
            int numRows = numDivisions + 1;
            for (int row = 0; row < numRows; row++)
            {
                int topVertex = ((row + 1) * (row + 1) - row - 1) / 2;
                int bottomVertex = ((row + 2) * (row + 2) - row - 2) / 2;
                
                int numTrianglesInRow = 1 + 2 * row;
                for (int column = 0; column < numTrianglesInRow; column++)
                {
                    int v0, v1, v2;
                    
                    if (column % 2 == 0)
                    {
                        v0 = topVertex;
                        v1 = bottomVertex + 1;
                        v2 = bottomVertex;
                        topVertex++;
                        bottomVertex++;
                    }
                    else
                    {
                        v0 = topVertex;
                        v1 = bottomVertex;
                        v2 = topVertex - 1;
                    }
                    
                    triangles[triangleIndex++] = vertexMap[v0];
                    triangles[triangleIndex++] = reverse ? vertexMap[v2] : vertexMap[v1];
                    triangles[triangleIndex++] = reverse ? vertexMap[v1] : vertexMap[v2];
                }
            }
        }
        public class Edge 
        {
            public int[] vertexIndices;
            public Edge(int[] indices) { vertexIndices = indices; }
        }
    }
}