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
        [SerializeField] 
        private float gridVelocityInfluence = 20.0f;
        [SerializeField] 
        private float damping = 0.99f;
        [SerializeField] 
        private float maxSpeed = 10f;
        [SerializeField] 
        private float buoyancy = 3f;

        // Definições de particula
        [SerializeField]
        private int res = 1; 
        [SerializeField]
        private int particleCount = 5000; // Numero de particulas
        [SerializeField]
        private float particleSize = 0.1f;
        [SerializeField]
        private Material particleMaterial;
        private readonly Vector3 gravity = new Vector3(0, -9.81f, 0);

        // Visuais
        private Texture2D velocityTexture;
        private Mesh quadMesh;
        
        // Arrays de data
        private Vector3[] particlePositions;
        private Vector3[] particleVelocities;
        
        // Buffers
        private ComputeBuffer particlePositionsBuffer;
        private ComputeBuffer particleVelocitiesBuffer;
        
        private Mesh particleMesh;
        
        void Start()
        {
            // Cria a grid
            grid = new FluidGrid3D(simulationWidth, gridHeight, simulationHeight);
            
            // Adiciona velocidade teste
            AddTestVelocity();
            
            // Gera a mesh de particulas
            particleMesh = FluidGenerator.MeshGenerator(res); // Inserir a resolução
            
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
                    Random.Range(1, simulationWidth - 2),
                    Random.Range(1, gridHeight - 2),
                    Random.Range(1, simulationHeight - 2)
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
                particleMaterial.SetFloat("_ParticleSize", particleSize);
            }
            else
            {
                Debug.LogError("No particle material assigned!");
            }
            
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // Update da simulação
            grid.Step(dt);

            // Update das partículas
            UpdateParticles(dt);

            particlePositionsBuffer.SetData(particlePositions);

            Graphics.DrawMeshInstancedProcedural(
                particleMesh,
                0,
                particleMaterial,
                new Bounds(Vector3.zero, Vector3.one * 1000f),
                particleCount);
        }

        void UpdateParticles(float dt)
        {
            for (int i = 0; i < particleCount; i++)
            {
                Vector3 pos = particlePositions[i];
                // Clamp sampling position inside grid
                Vector3 samplePos = new Vector3(
                    Mathf.Clamp(pos.x, 1, simulationWidth - 2),
                    Mathf.Clamp(pos.y, 1, gridHeight - 2),
                    Mathf.Clamp(pos.z, 1, simulationHeight - 2)
                );

                // Faz sample da velocidade do fluido
                Vector3 fluidVelocity = grid.SampleVelocity(samplePos);

                // Calcula as forças com a gravidade
                Vector3 acceleration =fluidVelocity * gridVelocityInfluence + Vector3.up * buoyancy + gravity;

                // implementa as velocidade
                particleVelocities[i] += acceleration * dt;

                // Fricção (damping)
                particleVelocities[i] *= damping;

                // Limitar velocidade maxima
                particleVelocities[i] = Vector3.ClampMagnitude(particleVelocities[i], maxSpeed);

                // Aplicar posição
                particlePositions[i] += particleVelocities[i] * dt;

                // Colisões das barreiras 
                Vector3 vel = particleVelocities[i];

                // Limites X
                if (pos.x < 1f)
                {
                    pos.x = 1f;
                    vel.x *= -0.5f;
                }
                else if (pos.x > simulationWidth - 2f)
                {
                    pos.x = simulationWidth - 2f;
                    vel.x *= -0.5f;
                }

                // Limites Y
                if (pos.y < 1f)
                {
                    pos.y = 1f;
                    vel.y *= -0.5f;
                }
                else if (pos.y > gridHeight - 2f)
                {
                    pos.y = gridHeight - 2f;
                    vel.y *= -0.5f;
                }

                // Limites Z
                if (pos.z < 1f)
                {
                    pos.z = 1f;
                    vel.z *= -1f;
                }
                else if (pos.z > simulationHeight - 2f)
                {
                    pos.z = simulationHeight - 2f;
                    vel.z *= -1f;
                }

                particlePositions[i] = pos;
                particleVelocities[i] = vel;
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
                        Vector3 velocity = new Vector3(-dz, 0, dx).normalized * 2f;
                        grid.AddVelocity(x, middleY, z, velocity);
                        
                        // Adiciona densidade
                        grid.AddDensity(x, middleY, z, 1f);
                    }
                }
            }
        }
        
        
        void OnDestroy()
        {
            particlePositionsBuffer?.Release();
            particleVelocitiesBuffer?.Release();
        }
    }
}