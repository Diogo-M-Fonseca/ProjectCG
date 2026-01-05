using UnityEngine;

namespace CGProject
{
    /// <summary>
    /// Script responsavel pela geração de octaedros usado como particula para
    /// a simulação de fluídos semi-lagrangiana
    /// </summary>
    public static class FluidGenerator
    {
        static readonly int[] edgeVertex =
        {
            // Topo ao centro
            0,1, 0,2, 0,3, 0,4,

            // Conexões do centro
            1,2, 2,3, 3,4, 4,1,

            // Baixo ao centro
            5,1, 5,2, 5,3, 5,4,
        };
        static readonly int[] faceEdges =
        {
            // Parte de cima
            0,1,4,
            1,2,5,
            2,3,6,
            3,0,7,

            // Parte de baixo
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

            // Dependendo da "meshResolution" escolhida, haverão mais divisões nos triangulos
            // da mesh, ou seja, aumenta a resolução
            int numDiv = Mathf.Max(0, res);
            int triangleSize = numDiv + 2;

            // Formula para o enésimo número triangular
            int numVertFaces = triangleSize * (triangleSize + 1) / 2;

            // Calculo do total de vertices
            int totalVertices = numVertFaces * 8 - (numDiv + 2) * 12 + 6;

            int trisFace = (numDiv + 1) * (numDiv + 1);

            // trisFace * 8 * 3 porque são 8 lados do octaedro e 3 lados de cada triângulo
            int totalIndex = trisFace * 8 * 3;

            Vector3[] vertices = new Vector3[totalVertices];
            int[] triangles = new int[totalIndex];


            // Adiciona os vertices ao array
            for (int i = 0; i < 6; i++) 
            {
                vertices[i] = baseVert[i];
            }
            int nextVertIndex = 6;
            int triangleIndex = 0;

            // Cria as arestas
            Edge[] edges = new Edge[12];
            for (int i = 0; i < edgeVertex.Length; i += 2) 
            {
                int startIdx = edgeVertex[i];
                int endIdx = edgeVertex[i + 1];
                Vector3 start = vertices[startIdx];
                Vector3 end = vertices[endIdx];
            
                // Array para todos os vértices desta aresta
                int[] edgeVerts = new int[numDiv + 2];
                edgeVerts[0] = startIdx;
            
                // Adiciona os vertices subdivididos
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
                
                // A parte de baixo tem de ser criada de forma reversa
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

        // Função de CreateFace de Sebastian Lague
        // Constroi um vertex map para os octaedros
        // Enche os interiores e gera as particulas
        static void CreateFace(Edge sideA, Edge sideB, Edge bottom, bool reverse,
            Vector3[] vertices, int[] triangles, ref int vertexIndex, ref int triangleIndex, // Track positions
            int numDivisions, int numVertsPerFace)
        {
            int numPointsInEdge = sideA.vertexIndices.Length;
            int[] vertexMap = new int[numVertsPerFace];
            int vertexMapIndex = 0;
            
            // Vertex do topo
            vertexMap[vertexMapIndex++] = sideA.vertexIndices[0];
            
            for (int i = 1; i < numPointsInEdge - 1; i++)
            {
                // Vertex do lado A
                vertexMap[vertexMapIndex++] = sideA.vertexIndices[i];
                
                // Adiciona vertex interiores
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
                
                // Vertex do lado B
                vertexMap[vertexMapIndex++] = sideB.vertexIndices[i];
            }
            
            // Adiciona os vertices das arestas de baixo
            for (int i = 0; i < numPointsInEdge; i++)
            {
                vertexMap[vertexMapIndex++] = bottom.vertexIndices[i];
            }
            
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