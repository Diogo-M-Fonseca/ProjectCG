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

        // Definições de particula
        [SerializeField]
        private int res = 1; 
        [SerializeField]
        private int particleCount = 5000; // Numero de particulas
        [SerializeField]
        private float particleSize = 0.1f;
        [SerializeField]
        private Material particleMaterial;
        
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
                // Posição atual
                Vector3 pos = particlePositions[i];
                
                // Dá sample da posição da partícula
                Vector3 samplePos = new Vector3(
                    Mathf.Clamp(pos.x, 1, simulationWidth - 2),
                    Mathf.Clamp(pos.y, 1, gridHeight - 2),
                    Mathf.Clamp(pos.z, 1, simulationHeight - 2)
                );

                // Dá sample da velocidade da grid
                Vector3 fluidVelocity = grid.SampleVelocity(samplePos);
                
                // Aplica as forças da grid
                Vector3 acceleration = fluidVelocity * gridVelocityInfluence;
                
                // Aplica aceleração
                particleVelocities[i] += acceleration * dt;
                
                // Aplica fricção
                particleVelocities[i] *= damping;
                
                // Limita a velocidade máxima
                if (particleVelocities[i].magnitude > maxSpeed)
                {
                    particleVelocities[i] = particleVelocities[i].normalized * maxSpeed;
                }
                
                // Calcula a nova posição após a velocidade ser aplicada
                Vector3 newPos = pos + particleVelocities[i] * dt;
                
                // Guarda a velocidade para colisões
                Vector3 vel = particleVelocities[i];                
                
                // Colisões com X
                if (newPos.x < 1f)
                {
                    newPos.x = 1f;
                    vel.x *= -0.5f;
                }
                else if (newPos.x > simulationWidth - 2f)
                {
                    newPos.x = simulationWidth - 2f;
                    vel.x *= -0.5f;
                }
                
                // Colisões com Y
                if (newPos.y < 1f)
                {
                    newPos.y = 1f;
                    vel.y = Mathf.Max(vel.y * -0.2f, 0f);
                    vel.x *= 0.8f;
                    vel.z *= 0.8f;
                }
                else if (newPos.y > gridHeight - 2f)
                {
                    newPos.y = gridHeight - 2f;
                    vel.y *= -0.5f;
                }
                
                // Colisões com Z
                if (newPos.z < 1f)
                {
                    newPos.z = 1f;
                    vel.z *= -0.5f;
                }
                else if (newPos.z > simulationHeight - 2f)
                {
                    newPos.z = simulationHeight - 2f;
                    vel.z *= -0.5f;
                }
                
                // Valores finais
                particlePositions[i] = newPos;
                particleVelocities[i] = vel;
            }
        }       
        void OnDestroy()
        {
            particlePositionsBuffer?.Release();
            particleVelocitiesBuffer?.Release();
        }
    }
}