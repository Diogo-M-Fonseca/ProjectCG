using UnityEngine;

namespace CGProject
{
    /// <summary>
    /// Script responsavel por aplicar os parametros que afetarão as partículas
    /// </summary>
    public class ParticleManager : MonoBehaviour
    {
        public FluidGrid3D grid;
        
        // Parametros da simulação
        [SerializeField]
        private int simulationWidth = 64;
        [SerializeField]
        private int simulationHeight = 64;
        [SerializeField]
        private int gridHeight = 16;
        
        // Definições de particula
        [SerializeField]
        private int res = 1; 
        private int particleCount = 5000; // Numero de particulas
        [SerializeField]
        private float particleSize = 0.1f;
        [SerializeField]
        private Material particleMaterial;
        private Vector3 gravity = new Vector3(0, -9.81f, 0);

        // Visuais
        private Texture2D velocityTexture;
        private Mesh quadMesh;
        
        // Arrays de data
        Vector3[] particlePositions;
        Vector3[] particleVelocities;
        
        // Buffers
        ComputeBuffer particlePositionsBuffer;
        ComputeBuffer particleVelocitiesBuffer;
        
        Mesh particleMesh;
        
        void Start()
        {
            // Cria a grid
            grid = new FluidGrid3D(simulationWidth, gridHeight, simulationHeight);
            
            // Adiciona velocidade teste
            AddTestVelocity();
            
            // Gera a mesh de particulas
            particleMesh = FluidGenerator.MeshGenerator(res); // Inserir a resolução
            
            // Cria a mesh para o display
            CreateQuadMesh();
            
            // Inicializa os arrays de particulas
            particlePositions = new Vector3[particleCount];
            particleVelocities = new Vector3[particleCount];
            
            // Inicializa partículas dentro dos parametros definidos
            for (int i = 0; i < particleCount; i++)
            {
                // Coloca as particulas no espaço,
                // O mapa terá fisica que as impeça de escapar
                // No entanto mesmo se escaparem de alguma forma
                // Serão puxadas de volta
                particlePositions[i] = new Vector3(
                    Random.Range(1, simulationWidth - 1),
                    Random.Range(1, gridHeight - 1),
                    Random.Range(1, simulationHeight - 1)
                );
                particleVelocities[i] = Vector3.zero;
            }
            
            // Cria os compute buffers
            particlePositionsBuffer = new ComputeBuffer(
                particleCount, 
                sizeof(float) * 3
            );
            
            particleVelocitiesBuffer = new ComputeBuffer(
                particleCount, 
                sizeof(float) * 3
            );
            
            // Define a data inicial
            particlePositionsBuffer.SetData(particlePositions);
            particleVelocitiesBuffer.SetData(particleVelocities);
            
            // Conecta ao shader
            if (particleMaterial != null)
            {
                particleMaterial.SetBuffer("_ParticlePositions", particlePositionsBuffer);
                particleMaterial.SetBuffer("_ParticleVelocities", particleVelocitiesBuffer);
                particleMaterial.SetFloat("_ParticleSize", particleSize);
            }
            else
            {
                Debug.LogError("No particle material assigned!");
            }
            
            // Cria textura para debug
            velocityTexture = new Texture2D(simulationWidth, simulationHeight);
        }
        
        void Update()
        {   
            if (grid == null)
            {
                Debug.LogWarning("Grid is null");
                return;
            }
            
            // Update da simulação
            grid.Step(Time.deltaTime);
            
            // Vai buscar a texture para visualização
            velocityTexture = grid.GetVelocityTexture();
            
            // Update das partículas
            UpdateParticles();
            
            // Envia data para os buffers
            if (particlePositionsBuffer != null && particleVelocitiesBuffer != null)
            {
                particlePositionsBuffer.SetData(particlePositions);
                particleVelocitiesBuffer.SetData(particleVelocities);
                
                // Renderiza as partículas
                if (particleMaterial != null)
                {
                    Graphics.DrawMeshInstancedProcedural(
                        particleMesh, 
                        0, 
                        particleMaterial, 
                        particleMesh.bounds, 
                        particleCount
                    );
                }
            }
        }
        
        void UpdateParticles()
        {
            for (int i = 0; i < particleCount; i++)
            {
                // Converte a posição das partículas para indices
                int gridX = Mathf.FloorToInt(particlePositions[i].x);
                int gridY = Mathf.FloorToInt(particlePositions[i].y);
                int gridZ = Mathf.FloorToInt(particlePositions[i].z);
                
                // Dá clamp às paredes da simulação
                gridX = Mathf.Clamp(gridX, 1, simulationWidth - 2);
                gridY = Mathf.Clamp(gridY, 1, gridHeight - 2);
                gridZ = Mathf.Clamp(gridZ, 1, simulationHeight - 2);
            
                // Usa a velocidade da grid
                Vector3 samplePos = new Vector3(
                    particlePositions[i].x,
                    particlePositions[i].y,
                    particlePositions[i].z);
                   
                // Dá clamp da posição para evitar "out-of-bounds"
                samplePos.x = Mathf.Clamp(samplePos.x, 1, simulationWidth - 2);
                samplePos.y = Mathf.Clamp(samplePos.y, 1, gridHeight - 2);
                samplePos.z = Mathf.Clamp(samplePos.z, 1, simulationHeight - 2);
                
                Vector3 velocity = grid.SampleVelocity(samplePos);

                // Aplica gravidade
                velocity += gravity * Time.deltaTime;
                
                // Update da velocidade
                particleVelocities[i] = velocity * grid.velocityScale;
                
                // Update da posição
                particlePositions[i] += particleVelocities[i] * Time.deltaTime;
                
                // Cantos da simulação
                if (particlePositions[i].x < 0) 
                    particlePositions[i].x = simulationWidth - 1;
                if (particlePositions[i].x >= simulationWidth) 
                    particlePositions[i].x = 0;
                if (particlePositions[i].y < 0) 
                    particlePositions[i].y = gridHeight - 1;
                if (particlePositions[i].y >= gridHeight) 
                    particlePositions[i].y = 0;
                if (particlePositions[i].z < 0) 
                    particlePositions[i].z = simulationHeight - 1;
                if (particlePositions[i].z >= simulationHeight) 
                    particlePositions[i].z = 0;
            }
        }      
        void AddTestVelocity()
        {
            // Adiciona um vortex
            int centerX = simulationWidth / 2;
            int centerZ = simulationHeight / 2;
            int middleY = gridHeight / 2;
            
            for (int x = 1; x < simulationWidth - 1; x++)
            {
                for (int z = 1; z < simulationHeight - 1; z++)
                {
                    // Calcula o vetor pelo centro
                    float dx = x - centerX;
                    float dz = z - centerZ;
                    float distance = Mathf.Sqrt(dx * dx + dz * dz);
                    
                    if (distance < 10f && distance > 0.1f)
                    {
                        // Cria um espaço circular
                        Vector3 velocity = new Vector3(-dz / distance, 0, dx / distance) * 2f;
                        grid.AddVelocity(x, middleY, z, velocity);
                        
                        // Adiciona densidade
                        grid.AddDensity(x, middleY, z, 1f);
                    }
                }
            }
        }
        
        void CreateQuadMesh()
        {
            quadMesh = new Mesh();
            quadMesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0)
            };
            quadMesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            quadMesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };
            quadMesh.RecalculateNormals();
        }
        
        void OnDestroy()
        {
            if (particlePositionsBuffer != null) 
                particlePositionsBuffer.Release();
            if (particleVelocitiesBuffer != null) 
                particleVelocitiesBuffer.Release();
            if (velocityTexture != null)
                Destroy(velocityTexture);
        }
    }
}