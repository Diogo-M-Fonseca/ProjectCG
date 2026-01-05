using UnityEngine;
using System.Collections.Generic;

namespace CGProject
{
    public class ParticleManager_old : MonoBehaviour
    {
        public FluidGrid3D_old grid;

        [Header("Grid Settings")]
        [SerializeField] private int simulationWidth = 16;
        [SerializeField] private int simulationHeight = 16;
        [SerializeField] private int gridHeight = 16;

        [Header("Particle Settings")]
        [SerializeField] private int particleCount = 1000;
        [SerializeField] private float particleSize = 0.1f;
        [SerializeField] private Material particleMaterial;
        [SerializeField] private int meshResolution = 1;
        [SerializeField] private float particleMass = 1.0f;
        private List<int> tempNearby = new List<int>(64); // Lista temporária para partículas próximas

        [Header("SPH Settings")]
        [SerializeField] private float pressureMultiplier = 300.0f; // Multiplicador da força de pressão
        [SerializeField] private float targetDensity = 20.0f; // Densidade alvo para o fluido
        [SerializeField] private float smoothingRadius = 0.4f; // Raio de suavização para os cálculos SPH
        [SerializeField] private float viscosityStrength = 1.0f; // Força da viscosidade

        [Header("Movement Settings")]
        [SerializeField] private float gridVelocityInfluence = 0.0f; // Influência da velocidade da grelha
        [SerializeField] private float damping = 0.98f; // Amortecimento da velocidade
        [SerializeField] private float maxSpeed = 8.0f; // Velocidade máxima das partículas
        [SerializeField] private float particleGravityScale = 1.0f; // Escala da gravidade aplicada às partículas
        [SerializeField] private float velocityThreshold = 0.02f; // Limiar para considerar partícula em movimento

        [Header("Colision Settings")]
        [SerializeField] private float wallBounce = 0.2f; // Coeficiente de restituição nas paredes
        [SerializeField] private float wallFriction = 0.9f; // Atrito nas paredes
        [SerializeField] private float wallMargin = 0.5f; // Margem das paredes para colisão

        [Header("Repulsion Settings")]
        [SerializeField] private bool useArtificialRepulsion = true; // Ativa repulsão artificial
        [SerializeField] private float repulsionStrength = 80.0f; // Força da repulsão artificial
        [SerializeField] private float repulsionRadius = 0.15f; // Raio da repulsão artificial

        // Valores em cache para comparação de parâmetros
        private float lastPressureMultiplier;
        private float lastTargetDensity;
        private float lastSmoothingRadius;
        private float lastRepulsionStrength;
        private bool lastUseArtificialRepulsion;

        // Arrays de dados das partículas
        private Vector3[] particlePositions;
        private Vector3[] particleVelocities;
        private float[] densities;
        private Vector3[] pressureForces;

        // Grelha espacial para optimização
        private List<int>[,,] spatialGrid;
        private int gridCellsX, gridCellsY, gridCellsZ;
        private float spatialCellSize;

        // Buffers para renderização
        private ComputeBuffer particlePositionsBuffer;
        private ComputeBuffer particleVelocitiesBuffer;
        private Mesh particleMesh;

        private const float GRAVITY = -9.81f; // Constante gravitacional

        void Start()
        {
            InitializeSimulation();
            CacheParameters();
        }

        void CacheParameters()
        {
            // Armazena os valores atuais dos parâmetros para comparação
            lastPressureMultiplier = pressureMultiplier;
            lastTargetDensity = targetDensity;
            lastSmoothingRadius = smoothingRadius;
            lastRepulsionStrength = repulsionStrength;
            lastUseArtificialRepulsion = useArtificialRepulsion;
        }

        void CheckParameterChanges()
        {            
            // Verifica se os parâmetros foram alterados e reinicializa se necessário
            if (Mathf.Abs(lastPressureMultiplier - pressureMultiplier) > 0.01f)
            {
                lastPressureMultiplier = pressureMultiplier;
            }

            if (Mathf.Abs(lastTargetDensity - targetDensity) > 0.01f)
            {
                lastTargetDensity = targetDensity;
            }

            if (Mathf.Abs(lastSmoothingRadius - smoothingRadius) > 0.01f)
            {
                lastSmoothingRadius = smoothingRadius;
                // Reinicializa a grelha espacial quando o raio muda
                InitializeSpatialGrid();
            }

            if (Mathf.Abs(lastRepulsionStrength - repulsionStrength) > 0.01f)
            {
                lastRepulsionStrength = repulsionStrength;
            }

            if (lastUseArtificialRepulsion != useArtificialRepulsion)
            {
                lastUseArtificialRepulsion = useArtificialRepulsion;
            }
        }

        void InitializeSimulation()
        {            
            // Inicializa a grelha de fluido e cria o mesh para as partículas
            grid = new FluidGrid3D_old(simulationWidth, gridHeight, simulationHeight);
            particleMesh = FluidGenerator.MeshGenerator(meshResolution);

            // Aloca arrays para dados das partículas
            particlePositions = new Vector3[particleCount];
            particleVelocities = new Vector3[particleCount];
            densities = new float[particleCount];
            pressureForces = new Vector3[particleCount];

            // Inicializa estruturas
            InitializeSpatialGrid();
            InitializeParticles();

            // Cria buffers de computação para renderização
            particlePositionsBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            particleVelocitiesBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            particlePositionsBuffer.SetData(particlePositions);
            particleVelocitiesBuffer.SetData(particleVelocities);

            // Configura o material com os buffers
            if (particleMaterial != null)
            {
                particleMaterial.SetBuffer("_ParticlePositions", particlePositionsBuffer);
                particleMaterial.SetBuffer("_ParticleVelocities", particleVelocitiesBuffer);
                particleMaterial.SetFloat("_ParticleSize", particleSize);
            }                
        }

        void InitializeSpatialGrid()
        {
            // Configura a grelha espacial para optimizar pesquisas de vizinhança
            spatialCellSize = smoothingRadius * 1.5f;
            gridCellsX = Mathf.Max(1, Mathf.CeilToInt(simulationWidth / spatialCellSize));
            gridCellsY = Mathf.Max(1, Mathf.CeilToInt(gridHeight / spatialCellSize));
            gridCellsZ = Mathf.Max(1, Mathf.CeilToInt(simulationHeight / spatialCellSize));

            spatialGrid = new List<int>[gridCellsX, gridCellsY, gridCellsZ];

            // Inicializa todas as células da grelha
            for (int x = 0; x < gridCellsX; x++)
                for (int y = 0; y < gridCellsY; y++)
                    for (int z = 0; z < gridCellsZ; z++)
                        spatialGrid[x, y, z] = new List<int>();
        }

        void InitializeParticles()
        {
            // Inicializa as partículas numa configuração de gota
            float margin = wallMargin + 1.0f;

            // Centro da gota
            Vector3 dropletCenter = new Vector3(
                simulationWidth * 0.5f,
                gridHeight * 0.7f,
                simulationHeight * 0.5f
            );

            float dropletRadius = Mathf.Min(simulationWidth, gridHeight, simulationHeight) * 0.3f;

            for (int i = 0; i < particleCount; i++)
            {
                // Posiciona partículas aleatoriamente dentro de uma esfera
                Vector3 randomDir = Random.onUnitSphere;
                float randomDist = Random.Range(0f, dropletRadius);
                Vector3 pos = dropletCenter + randomDir * randomDist;

                // Garante que está dentro dos limites
                pos.x = Mathf.Clamp(pos.x, margin, simulationWidth - margin);
                pos.y = Mathf.Clamp(pos.y, margin, gridHeight - margin);
                pos.z = Mathf.Clamp(pos.z, margin, simulationHeight - margin);

                particlePositions[i] = pos;
                particleVelocities[i] = Vector3.zero; // Velocidade inicial zero
            }
        }

        void Update()
        {
            // Verifica alterações de parâmetros a cada frame
            CheckParameterChanges();

            // Usa um delta time limitado para estabilidade
            float dt = Mathf.Min(Time.deltaTime, 0.033f);

            // Atualiza a grelha de fluido
            if (grid != null)
            {
                grid.Step(dt);
            }

            // Atualiza as partículas
            UpdateParticles(dt);

            // Atualiza os buffers para renderização
            particlePositionsBuffer.SetData(particlePositions);
            particleVelocitiesBuffer.SetData(particleVelocities);

            // Desenha as partículas
            Graphics.DrawMeshInstancedProcedural(
                particleMesh,
                0,
                particleMaterial,
                new Bounds(new Vector3(simulationWidth, gridHeight, simulationHeight) * 0.5f, 
                          new Vector3(simulationWidth, gridHeight, simulationHeight)),
                particleCount);
        }

        void UpdateParticles(float dt)
        {
            // Atualiza a grelha espacial com as novas posições
            UpdateSpatialGrid();

            // Calcula densidades usando SPH
            CalculateDensitiesSPH();

            // Calcula forças de pressão usando SPH
            CalculatePressureForcesSPH();

            // Aplica repulsão artificial se ativada
            if (useArtificialRepulsion)
            {
                ApplyArtificialRepulsion(dt);
            }

            // Atualiza cada partícula individualmente
            for (int i = 0; i < particleCount; i++)
            {
                UpdateParticleSPH(i, dt);
            }
        }

        void UpdateSpatialGrid()
        {
            // Limpa a grelha espacial
            for (int x = 0; x < gridCellsX; x++)
                for (int y = 0; y < gridCellsY; y++)
                    for (int z = 0; z < gridCellsZ; z++)
                        spatialGrid[x, y, z].Clear();

            // Reinsere todas as partículas na grelha
            for (int i = 0; i < particleCount; i++)
            {
                Vector3 pos = particlePositions[i];
                int gridX = Mathf.Clamp((int)(pos.x / spatialCellSize), 0, gridCellsX - 1);
                int gridY = Mathf.Clamp((int)(pos.y / spatialCellSize), 0, gridCellsY - 1);
                int gridZ = Mathf.Clamp((int)(pos.z / spatialCellSize), 0, gridCellsZ - 1);

                spatialGrid[gridX, gridY, gridZ].Add(i);
            }
        }

        List<int> GetParticlesInRadius(Vector3 position, float radius)
        {
            // Retorna partículas dentro de um raio, usando a grelha espacial para eficiência
            tempNearby.Clear();
            float radiusSq = radius * radius;

            // Calcula célula da grelha
            int gridX = Mathf.Clamp((int)(position.x / spatialCellSize), 0, gridCellsX - 1);
            int gridY = Mathf.Clamp((int)(position.y / spatialCellSize), 0, gridCellsY - 1);
            int gridZ = Mathf.Clamp((int)(position.z / spatialCellSize), 0, gridCellsZ - 1);

            // Verifica células vizinhas (3x3x3)
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                    {
                        int cx = gridX + x;
                        int cy = gridY + y;
                        int cz = gridZ + z;

                        // Verifica limites
                        if (cx < 0 || cy < 0 || cz < 0 ||
                            cx >= gridCellsX || cy >= gridCellsY || cz >= gridCellsZ)
                            continue;

                        // Adiciona partículas dentro do raio
                        foreach (int j in spatialGrid[cx, cy, cz])
                        {
                            if ((particlePositions[j] - position).sqrMagnitude < radiusSq)
                                tempNearby.Add(j);
                        }
                    }

            return tempNearby;
        }

        float SmoothingKernel(float distance, float radius)
        {
            // Kernel polinomial padrão para SPH
            if (distance >= radius) return 0;

            float r = radius;
            float value = r * r - distance * distance;
            return 315f / (64f * Mathf.PI * Mathf.Pow(r, 9)) * value * value * value;
        }

        float SmoothingKernelDerivative(float distance, float radius)
        {
            // Derivada do kernel polinomial
            if (distance >= radius) return 0;
            if (distance < 0.0001f) return 0; // Evita divisão por zero

            float r = radius;
            float value = r - distance;
            return -45f / (Mathf.PI * Mathf.Pow(r, 6)) * value * value;
        }

        float ViscosityKernel(float distance, float radius)
        {
            // Kernel para viscosidade
            if (distance >= radius) return 0;

            float r = radius;
            return 45f / (Mathf.PI * Mathf.Pow(r, 6)) * (r - distance);
        }

        void CalculateDensitiesSPH()
        {
            // Calcula densidades usando o método SPH
            float radius = smoothingRadius;
            float radiusSq = radius * radius;

            for (int i = 0; i < particleCount; i++)
            {
                // Densidade inicial da própria partícula
                float density = particleMass * SmoothingKernel(0, radius);
                Vector3 posI = particlePositions[i];

                // Obtém partículas vizinhas
                List<int> nearby = GetParticlesInRadius(posI, radius);

                // Adiciona contribuição de cada partícula vizinha
                foreach (int j in nearby)
                {
                    if (i == j) continue;

                    Vector3 delta = posI - particlePositions[j];
                    float distSq = delta.sqrMagnitude;

                    if (distSq < radiusSq)
                    {
                        float dist = Mathf.Sqrt(distSq);
                        density += particleMass * SmoothingKernel(dist, radius);
                    }
                }

                // Garante densidade mínima para evitar divisão por zero
                densities[i] = Mathf.Max(density, 0.001f);
            }
        }

        void CalculatePressureForcesSPH()
        {
            // Calcula forças de pressão e viscosidade usando SPH
            float radius = smoothingRadius;
            float radiusSq = radius * radius;

            for (int i = 0; i < particleCount; i++)
            {
                Vector3 pressureForce = Vector3.zero;
                Vector3 viscosityForce = Vector3.zero;
                Vector3 posI = particlePositions[i];
                Vector3 velI = particleVelocities[i];

                float densityI = densities[i];
                float pressureI = pressureMultiplier * (densityI - targetDensity);

                // Obtém partículas vizinhas
                List<int> nearby = GetParticlesInRadius(posI, radius);

                foreach (int j in nearby)
                {
                    if (i == j) continue;

                    Vector3 delta = posI - particlePositions[j];
                    float distSq = delta.sqrMagnitude;

                    if (distSq < radiusSq && distSq > 0.0001f)
                    {
                        float dist = Mathf.Sqrt(distSq);
                        Vector3 dir = delta / dist;

                        float densityJ = densities[j];
                        float pressureJ = pressureMultiplier * (densityJ - targetDensity);

                        // Força de pressão simétrica
                        float sharedPressure = (pressureI + pressureJ) / (2f * densityJ);
                        pressureForce -= dir * particleMass * sharedPressure * 
                                        SmoothingKernelDerivative(dist, radius);

                        // Força de viscosidade
                        Vector3 velJ = particleVelocities[j];
                        viscosityForce += (velJ - velI) * ViscosityKernel(dist, radius) * 
                                         particleMass / densityJ;
                    }
                }

                // Combina forças de pressão e viscosidade
                pressureForces[i] = (pressureForce / Mathf.Max(densityI, 0.001f)) +
                                   (viscosityForce * viscosityStrength / Mathf.Max(densityI, 0.001f));
            }
        }

        void ApplyArtificialRepulsion(float dt)
        {
            // Aplica força de repulsão artificial
            if (!useArtificialRepulsion) return;

            float minDistance = repulsionRadius;
            float minDistanceSq = minDistance * minDistance;

            for (int i = 0; i < particleCount; i++)
            {
                List<int> nearby = GetParticlesInRadius(particlePositions[i], minDistance * 2f);

                foreach (int j in nearby)
                {
                    if (i >= j) continue; // Evita duplicação de cálculos

                    Vector3 delta = particlePositions[i] - particlePositions[j];
                    float distSq = delta.sqrMagnitude;

                    if (distSq < minDistanceSq && distSq > 0.0001f)
                    {
                        float dist = Mathf.Sqrt(distSq);
                        Vector3 dir = delta / dist;

                        // Força proporcional à proximidade
                        float force = repulsionStrength * (1f - dist / minDistance);

                        // Aplica força em direções opostas
                        particleVelocities[i] += dir * force * dt;
                        particleVelocities[j] -= dir * force * dt;
                    }
                }
            }
        }

        void UpdateParticleSPH(int i, float dt)
        {
            // Atualiza uma partícula individual usando SPH
            Vector3 pos = particlePositions[i];

            // Aceleração básica (gravidade)
            Vector3 acceleration = new Vector3(0, GRAVITY, 0) * particleGravityScale;

            // Adiciona forças SPH
            acceleration += pressureForces[i];

            // Atualiza velocidade usando aceleração
            particleVelocities[i] += acceleration * dt;

            // Aplica amortecimento
            particleVelocities[i] *= Mathf.Pow(damping, dt * 60f);

            // Limita velocidade máxima
            float speed = particleVelocities[i].magnitude;
            if (speed > maxSpeed)
            {
                particleVelocities[i] *= maxSpeed / speed;
            }

            // Atualiza posição
            Vector3 newPos = pos + particleVelocities[i] * dt;

            // Processa colisões com paredes
            HandleWallCollisions(ref newPos, ref particleVelocities[i]);

            particlePositions[i] = newPos;
        }

        void HandleWallCollisions(ref Vector3 position, ref Vector3 velocity)
        {
            // Processa colisões com as paredes
            float margin = wallMargin;

            // Paredes em X
            if (position.x < margin)
            {
                position.x = margin;
                velocity.x = Mathf.Abs(velocity.x) * wallBounce;
                velocity.y *= wallFriction;
                velocity.z *= wallFriction;
            }
            else if (position.x > simulationWidth - margin)
            {
                position.x = simulationWidth - margin;
                velocity.x = -Mathf.Abs(velocity.x) * wallBounce;
                velocity.y *= wallFriction;
                velocity.z *= wallFriction;
            }

            // Paredes em y (chão e teto)
            if (position.y < margin)
            {
                position.y = margin;
                velocity.y = Mathf.Abs(velocity.y) * wallBounce;
                velocity.x *= wallFriction;
                velocity.z *= wallFriction;
            }
            else if (position.y > gridHeight - margin)
            {
                position.y = gridHeight - margin;
                velocity.y = -Mathf.Abs(velocity.y) * wallBounce;
                velocity.x *= wallFriction;
                velocity.z *= wallFriction;
            }

            // Paredes em Z
            if (position.z < margin)
            {
                position.z = margin;
                velocity.z = Mathf.Abs(velocity.z) * wallBounce;
                velocity.x *= wallFriction;
                velocity.y *= wallFriction;
            }
            else if (position.z > simulationHeight - margin)
            {
                position.z = simulationHeight - margin;
                velocity.z = -Mathf.Abs(velocity.z) * wallBounce;
                velocity.x *= wallFriction;
                velocity.y *= wallFriction;
            }

            // Garante que as partículas permanecem dentro dos limites
            position.x = Mathf.Clamp(position.x, 0, simulationWidth);
            position.y = Mathf.Clamp(position.y, 0, gridHeight);
            position.z = Mathf.Clamp(position.z, 0, simulationHeight);
        }

        void OnDestroy()
        {
            // Liberta recursos do shader
            if (particlePositionsBuffer != null)
            {
                particlePositionsBuffer.Release();
            }
            if (particleVelocitiesBuffer != null)
            {
                particleVelocitiesBuffer.Release();
            }
        }

        public void ResetSimulation()
        {
            // Reinicializa as partículas para a posição inicial
            InitializeParticles();
            particlePositionsBuffer.SetData(particlePositions);
        }

        void OnGUI()
        {
            // Interface gráfica simples
            if (!Application.isPlaying) return;

            GUILayout.BeginArea(new Rect(10, 10, 400, 200));
            GUILayout.Label("CONTROLO DA SIMULAÇÃO DE FLUIDO");
            GUILayout.Label($"Partículas: {particleCount}");
            GUILayout.Label($"FPS: {1f/Time.deltaTime:F1}");

            // Calcula estatísticas
            float avgDensity = 0f;
            int movingParticles = 0;

            for (int i = 0; i < particleCount; i++)
            {
                avgDensity += densities[i];
                if (particleVelocities[i].magnitude > velocityThreshold)
                    movingParticles++;
            }

            avgDensity /= particleCount;

            // Estatisticas no ecrã
            GUILayout.Label($"Densidade Média: {avgDensity:F2} (Alvo: {targetDensity})");
            GUILayout.Label($"Partículas em Movimento: {movingParticles}/{particleCount}");
            GUILayout.Label($"Multiplicador de Pressão: {pressureMultiplier}");
            GUILayout.Label($"Repulsão: {(useArtificialRepulsion ? "LIGADA" : "DESLIGADA")} ({repulsionStrength})");

            // Botão para reiniciar
            if (GUILayout.Button("Reiniciar Simulação"))
            {
                ResetSimulation();
            }

            GUILayout.EndArea();
        }
    }
}