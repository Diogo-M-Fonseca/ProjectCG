using UnityEngine;
using System.Collections.Generic;

namespace CGProject
{
    /// <summary>
    /// Script responsável por gerenciar partículas SPH com influência da grid de fluidos
    /// </summary>
    public class ParticleManager : MonoBehaviour
    {
        public FluidGrid3D grid;
        
        // Parâmetros da simulação
        [Header("Grid Settings")]
        [SerializeField] private int simulationWidth = 64;
        [SerializeField] private int simulationHeight = 64;
        [SerializeField] private int gridHeight = 16;
        
        [Header("Particle Settings")]
        [SerializeField] private int particleCount = 5000;
        [SerializeField] private float particleSize = 0.1f;
        [SerializeField] private Material particleMaterial;
        [SerializeField] private int meshResolution = 1;
        [SerializeField] private float particleMass = 1.0f;
        private List<int> tempNearby = new List<int>(64);

        [Header("SPH Parameters")]
        [SerializeField] private float pressureMultiplier = 100.0f; // Aumentado
        [SerializeField] private float targetDensity = 25.0f;      // Aumentado
        [SerializeField] private float smoothingRadius = 0.5f;     // Aumentado
        [SerializeField] private float viscosityStrength = 0.5f;
        
        [Header("Movement Parameters")]
        [SerializeField] private float gridVelocityInfluence = 2.0f;
        [SerializeField] private float damping = 0.99f;
        [SerializeField] private float maxSpeed = 8.0f;
        [SerializeField] private float particleGravityScale = 0.1f;
        
        [Header("Initialization")]
        [SerializeField] private bool addInitialVelocity = true;
        [SerializeField] private float initialVelocityStrength = 3.0f;
        [SerializeField] private InitializationMethod initializationMethod = InitializationMethod.Grid3D;
        
        [Header("Collision Settings")]
        [SerializeField] private float wallBounce = 0.7f;
        [SerializeField] private float wallFriction = 0.92f;
        [SerializeField] private float wallMargin = 0.5f;
        
        [Header("Repulsion Settings")]
        [SerializeField] private bool useArtificialRepulsion = true;
        [SerializeField] private float repulsionStrength = 80.0f;  // Aumentado
        [SerializeField] private float repulsionRadius = 0.15f;    // Aumentado
        
        // Enum para métodos de inicialização
        public enum InitializationMethod
        {
            Grid3D,
            PoissonDisk,
            RandomWithSpacing
        }
        
        // Arrays de dados
        private Vector3[] particlePositions;
        private Vector3[] particleVelocities;
        private float[] densities;
        private Vector3[] pressureForces;
        
        // Grid espacial para otimização
        private List<int>[,,] spatialGrid;
        private int gridCellsX, gridCellsY, gridCellsZ;
        private float spatialCellSize;
        
        // Buffers para renderização
        private ComputeBuffer particlePositionsBuffer;
        private Mesh particleMesh;
        
        void Start()
        {
            InitializeSimulation();
        }

        void InitializeSimulation()
        {
            // Cria a grid de fluidos
            grid = new FluidGrid3D(simulationWidth, gridHeight, simulationHeight);
            
            // Adiciona velocidade inicial se necessário
            if (addInitialVelocity)
            {
                AddInitialGridVelocity();
            }
            
            // Gera a mesh das partículas
            particleMesh = FluidGenerator.MeshGenerator(meshResolution);
            
            // Inicializa arrays
            particlePositions = new Vector3[particleCount];
            particleVelocities = new Vector3[particleCount];
            densities = new float[particleCount];
            pressureForces = new Vector3[particleCount];
            
            // Inicializa grid espacial
            InitializeSpatialGrid();
            
            // Inicializa partículas baseado no método escolhido
            switch (initializationMethod)
            {
                case InitializationMethod.Grid3D:
                    InitializeParticlesGrid3D();
                    break;
                case InitializationMethod.PoissonDisk:
                    InitializeParticlesPoissonDisk();
                    break;
                case InitializationMethod.RandomWithSpacing:
                    InitializeParticlesRandomWithSpacing();
                    break;
            }
            
            // Cria buffers para renderização
            InitializeComputeBuffers();
        }

        void InitializeSpatialGrid()
        {
            spatialCellSize = smoothingRadius * 2f;
            gridCellsX = Mathf.CeilToInt(simulationWidth / spatialCellSize);
            gridCellsY = Mathf.CeilToInt(gridHeight / spatialCellSize);
            gridCellsZ = Mathf.CeilToInt(simulationHeight / spatialCellSize);
            
            spatialGrid = new List<int>[gridCellsX, gridCellsY, gridCellsZ];
            
            for (int x = 0; x < gridCellsX; x++)
                for (int y = 0; y < gridCellsY; y++)
                    for (int z = 0; z < gridCellsZ; z++)
                        spatialGrid[x, y, z] = new List<int>();
        }

        void InitializeParticlesGrid3D()
        {
            // Espaçamento maior para garantir que não se sobreponham
            float spacing = smoothingRadius * 2.2f;
            
            // Calcula quantas partículas cabem em cada dimensão
            int gridX = Mathf.FloorToInt((simulationWidth - 4) / spacing);
            int gridY = Mathf.FloorToInt((gridHeight - 4) / spacing);
            int gridZ = Mathf.FloorToInt((simulationHeight - 4) / spacing);
            
            int maxParticlesInGrid = gridX * gridY * gridZ;
            
            Debug.Log($"Grid 3D: {gridX}x{gridY}x{gridZ} = {maxParticlesInGrid} células disponíveis");
            
            if (maxParticlesInGrid < particleCount)
            {
                Debug.LogWarning($"Não cabem {particleCount} partículas na grid. Ajustando espaçamento...");
                spacing = Mathf.Pow((simulationWidth - 4) * (gridHeight - 4) * (simulationHeight - 4) / (float)particleCount, 1f/3f);
                gridX = Mathf.FloorToInt((simulationWidth - 4) / spacing);
                gridY = Mathf.FloorToInt((gridHeight - 4) / spacing);
                gridZ = Mathf.FloorToInt((simulationHeight - 4) / spacing);
            }
            
            int particlesCreated = 0;
            
            // Cria uma grid organizada
            for (int x = 0; x < gridX && particlesCreated < particleCount; x++)
            {
                for (int y = 0; y < gridY && particlesCreated < particleCount; y++)
                {
                    for (int z = 0; z < gridZ && particlesCreated < particleCount; z++)
                    {
                        // Posição central na célula com pequena variação aleatória
                        Vector3 pos = new Vector3(
                            2 + x * spacing + Random.Range(-0.05f, 0.05f) * spacing,
                            2 + y * spacing + Random.Range(-0.05f, 0.05f) * spacing,
                            2 + z * spacing + Random.Range(-0.05f, 0.05f) * spacing
                        );
                        
                        // Garante que está dentro dos limites
                        pos.x = Mathf.Clamp(pos.x, 2f, simulationWidth - 3f);
                        pos.y = Mathf.Clamp(pos.y, 2f, gridHeight - 3f);
                        pos.z = Mathf.Clamp(pos.z, 2f, simulationHeight - 3f);
                        
                        particlePositions[particlesCreated] = pos;
                        particleVelocities[particlesCreated] = Vector3.zero;
                        particlesCreated++;
                    }
                }
            }
            
            Debug.Log($"Criadas {particlesCreated} partículas com Grid 3D");
            
            // Se não criou todas, preenche o resto aleatoriamente
            if (particlesCreated < particleCount)
            {
                Debug.Log($"Preenchendo {particleCount - particlesCreated} partículas restantes aleatoriamente");
                for (int i = particlesCreated; i < particleCount; i++)
                {
                    particlePositions[i] = new Vector3(
                        Random.Range(2, simulationWidth - 3),
                        Random.Range(2, gridHeight - 3),
                        Random.Range(2, simulationHeight - 3)
                    );
                    particleVelocities[i] = Vector3.zero;
                }
            }
        }

        void InitializeParticlesPoissonDisk()
        {
            float minDistance = smoothingRadius * 1.8f;
            int maxAttempts = 30;
            
            List<Vector3> placedParticles = new List<Vector3>();
            
            // Define os limites da área de spawn
            Vector3 minBounds = new Vector3(2, 2, 2);
            Vector3 maxBounds = new Vector3(simulationWidth - 3, gridHeight - 3, simulationHeight - 3);
            
            // Lista de pontos ativos para o algoritmo
            List<Vector3> activeList = new List<Vector3>();
            
            // Primeiro ponto aleatório
            Vector3 firstPoint = new Vector3(
                Random.Range(minBounds.x, maxBounds.x),
                Random.Range(minBounds.y, maxBounds.y),
                Random.Range(minBounds.z, maxBounds.z)
            );
            
            placedParticles.Add(firstPoint);
            activeList.Add(firstPoint);
            
            int particlesCreated = 0;
            
            while (activeList.Count > 0 && particlesCreated < particleCount)
            {
                // Escolhe um ponto aleatório da lista ativa
                int randomIndex = Random.Range(0, activeList.Count);
                Vector3 currentPoint = activeList[randomIndex];
                
                bool pointAdded = false;
                
                // Tenta adicionar k pontos ao redor do ponto atual
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    // Gera um ponto em uma casca esférica ao redor do ponto atual
                    Vector3 randomDirection = Random.onUnitSphere;
                    float randomDistance = Random.Range(minDistance, minDistance * 2f);
                    Vector3 candidate = currentPoint + randomDirection * randomDistance;
                    
                    // Verifica se está dentro dos limites
                    if (candidate.x < minBounds.x || candidate.x > maxBounds.x ||
                        candidate.y < minBounds.y || candidate.y > maxBounds.y ||
                        candidate.z < minBounds.z || candidate.z > maxBounds.z)
                    {
                        continue;
                    }
                    
                    // Verifica se está longe o suficiente de todos os outros pontos
                    bool valid = true;
                    foreach (Vector3 existingPoint in placedParticles)
                    {
                        if (Vector3.Distance(candidate, existingPoint) < minDistance)
                        {
                            valid = false;
                            break;
                        }
                    }
                    
                    if (valid)
                    {
                        placedParticles.Add(candidate);
                        activeList.Add(candidate);
                        pointAdded = true;
                        particlesCreated++;
                        
                        if (particlesCreated >= particleCount)
                            break;
                    }
                }
                
                // Se não conseguiu adicionar nenhum ponto, remove da lista ativa
                if (!pointAdded)
                {
                    activeList.RemoveAt(randomIndex);
                }
            }
            
            // Preenche o array com as partículas criadas
            for (int i = 0; i < particleCount; i++)
            {
                if (i < placedParticles.Count)
                {
                    particlePositions[i] = placedParticles[i];
                }
                else
                {
                    // Preenche o resto aleatoriamente
                    particlePositions[i] = new Vector3(
                        Random.Range(minBounds.x, maxBounds.x),
                        Random.Range(minBounds.y, maxBounds.y),
                        Random.Range(minBounds.z, maxBounds.z)
                    );
                }
                particleVelocities[i] = Vector3.zero;
            }
            
            Debug.Log($"Criadas {placedParticles.Count} partículas com Poisson Disk");
        }

        void InitializeParticlesRandomWithSpacing()
        {
            float minSpacing = smoothingRadius * 1.5f;
            float minSpacingSq = minSpacing * minSpacing;
            int maxAttempts = 50; // Aumentado
            
            for (int i = 0; i < particleCount; i++)
            {
                bool positionValid = false;
                int attempts = 0;
                
                while (!positionValid && attempts < maxAttempts)
                {
                    // Tenta uma posição aleatória
                    Vector3 candidatePos = new Vector3(
                        Random.Range(2, simulationWidth - 3),
                        Random.Range(2, gridHeight - 3),
                        Random.Range(2, simulationHeight - 3)
                    );
                    
                    // Verifica se está longe o suficiente de outras partículas
                    positionValid = true;
                    for (int j = 0; j < i; j++)
                    {
                        float distSq = (candidatePos - particlePositions[j]).sqrMagnitude;
                        if (distSq < minSpacingSq)
                        {
                            positionValid = false;
                            break;
                        }
                    }
                    
                    if (positionValid)
                    {
                        particlePositions[i] = candidatePos;
                        particleVelocities[i] = Vector3.zero;
                    }
                    
                    attempts++;
                }
                
                // Se não encontrou posição válida, tenta outra abordagem
                if (!positionValid)
                {
                    // Tenta posições mais sistemáticas
                    int gridSize = Mathf.CeilToInt(Mathf.Pow(particleCount, 1f/3f));
                    float gridSpacingX = (simulationWidth - 4) / (float)gridSize;
                    float gridSpacingY = (gridHeight - 4) / (float)gridSize;
                    float gridSpacingZ = (simulationHeight - 4) / (float)gridSize;
                    
                    int gridX = i % gridSize;
                    int gridY = (i / gridSize) % gridSize;
                    int gridZ = i / (gridSize * gridSize);
                    
                    Vector3 gridPos = new Vector3(
                        2 + gridX * gridSpacingX + Random.Range(-0.2f, 0.2f) * gridSpacingX,
                        2 + gridY * gridSpacingY + Random.Range(-0.2f, 0.2f) * gridSpacingY,
                        2 + gridZ * gridSpacingZ + Random.Range(-0.2f, 0.2f) * gridSpacingZ
                    );
                    
                    gridPos.x = Mathf.Clamp(gridPos.x, 2, simulationWidth - 3);
                    gridPos.y = Mathf.Clamp(gridPos.y, 2, gridHeight - 3);
                    gridPos.z = Mathf.Clamp(gridPos.z, 2, simulationHeight - 3);
                    
                    particlePositions[i] = gridPos;
                    particleVelocities[i] = Vector3.zero;
                }
            }
            
            Debug.Log($"Criadas {particleCount} partículas com Random com Spacing");
        }

        void InitializeComputeBuffers()
        {
            particlePositionsBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            particlePositionsBuffer.SetData(particlePositions);
            
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

        void AddInitialGridVelocity()
        {
            int centerX = simulationWidth / 2;
            int centerZ = simulationHeight / 2;
            
            for (int x = 1; x < simulationWidth - 1; x++)
            {
                for (int y = 1; y < gridHeight - 1; y++)
                {
                    for (int z = 1; z < simulationHeight - 1; z++)
                    {
                        float dx = x - centerX;
                        float dz = z - centerZ;
                        float distance = Mathf.Sqrt(dx * dx + dz * dz);
                        
                        if (distance < 15f && distance > 1f)
                        {
                            Vector3 velocity = new Vector3(-dz, Mathf.Sin(distance * 0.3f) * 0.3f, dx)
                                .normalized * initialVelocityStrength;
                            grid.AddVelocity(x, y, z, velocity);
                        }
                    }
                }
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // Atualiza a grid de fluidos
            grid.Step(dt);

            // Atualiza as partículas
            UpdateParticles(dt);

            // Atualiza buffer de posições
            particlePositionsBuffer.SetData(particlePositions);

            // Renderiza as partículas
            Graphics.DrawMeshInstancedProcedural(
                particleMesh,
                0,
                particleMaterial,
                new Bounds(new Vector3(simulationWidth, gridHeight, simulationHeight) * 0.5f, new Vector3(simulationWidth, gridHeight, simulationHeight)),
                particleCount); 
        }

        void UpdateParticles(float dt)
        {
            // Atualiza grid espacial
            UpdateSpatialGrid();
            
            // Calcula SPH
            CalculateDensitiesSPH();
            CalculatePressureForcesSPH();
            
            // Aplica repulsão artificial se habilitada
            if(useArtificialRepulsion)
            {
                ApplyArtificialRepulsion(dt);
            }
            
            // Atualiza todas as partículas
            for (int i = 0; i < particleCount; i++)
            {
                UpdateParticleSPH(i, dt);
            }
        }

        void UpdateSpatialGrid()
        {
            // Limpa a grid
            for (int x = 0; x < gridCellsX; x++)
                for (int y = 0; y < gridCellsY; y++)
                    for (int z = 0; z < gridCellsZ; z++)
                        spatialGrid[x, y, z].Clear();
            
            // Adiciona partículas à grid
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
            tempNearby.Clear();
            float radiusSq = radius * radius;

            int gridX = Mathf.Clamp((int)(position.x / spatialCellSize), 0, gridCellsX - 1);
            int gridY = Mathf.Clamp((int)(position.y / spatialCellSize), 0, gridCellsY - 1);
            int gridZ = Mathf.Clamp((int)(position.z / spatialCellSize), 0, gridCellsZ - 1);

            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                    {
                        int cx = gridX + x;
                        int cy = gridY + y;
                        int cz = gridZ + z;

                        if (cx < 0 || cy < 0 || cz < 0 ||
                            cx >= gridCellsX || cy >= gridCellsY || cz >= gridCellsZ)
                            continue;

                        foreach (int j in spatialGrid[cx, cy, cz])
                        {
                            if ((particlePositions[j] - position).sqrMagnitude < radiusSq)
                                tempNearby.Add(j);
                        }
                    }

            return tempNearby;
        }

        // Funções de kernel SPH
        float SmoothingKernel(float distance, float radius)
        {
            if (distance >= radius) return 0;
            
            float r = radius;
            float h3 = r * r * r;
            float h6 = h3 * h3;
            float h9 = h6 * h3;
            
            float value = r * r - distance * distance;
            return 315f / (64f * Mathf.PI * h9) * value * value * value;
        }

        float SmoothingKernelDerivative(float distance, float radius)
        {
            if (distance >= radius) return 0;
            
            float r = radius;
            float h = r;
            float h2 = h * h;
            float h4 = h2 * h2;
            
            float value = r - distance;
            return -45f / (Mathf.PI * h4) * value * value;
        }

        float ViscosityKernel(float distance, float radius)
        {
            if (distance >= radius) return 0;
            
            float r = radius;
            float h = r;
            float h3 = h * h * h;
            float h6 = h3 * h3;
            
            return 45f / (Mathf.PI * h6) * (r - distance);
        }

        void CalculateDensitiesSPH()
        {
            float radius = smoothingRadius;
            float radiusSq = radius * radius;
            
            for (int i = 0; i < particleCount; i++)
            {
                float density = 0;
                Vector3 posI = particlePositions[i];
                
                List<int> nearby = GetParticlesInRadius(posI, radius);
                
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

                density += particleMass * SmoothingKernel(0, radius); ;
                densities[i] = Mathf.Max(density, 0.001f);
            }
        }

        void CalculatePressureForcesSPH()
        {
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
                
                List<int> nearby = GetParticlesInRadius(posI, radius);
                
                foreach (int j in nearby)
                {
                    if (i == j) continue;
                    
                    Vector3 delta = posI - particlePositions[j];
                    float distSq = delta.sqrMagnitude;
                    
                    if (distSq < radiusSq)
                    {
                        float dist = Mathf.Sqrt(distSq);
                        Vector3 dir = delta / Mathf.Max(dist, 0.0001f);
                        
                        float densityJ = densities[j];
                        float pressureJ = pressureMultiplier * (densityJ - targetDensity);
                        
                        float sharedPressure = (pressureI + pressureJ) * 0.5f;
                        float slope = SmoothingKernelDerivative(dist, radius);
                        pressureForce -= dir * sharedPressure * slope / Mathf.Max(densityJ, 0.001f);

                        Vector3 velJ = particleVelocities[j];
                        viscosityForce += (velJ - velI) * ViscosityKernel(dist, radius);
                    }
                }
                
                pressureForces[i] = (pressureForce / Mathf.Max(densityI, 0.001f)) + 
                                   (viscosityForce * viscosityStrength / Mathf.Max(densityI, 0.001f));
            }
        }

        void ApplyArtificialRepulsion(float dt)
        {
            float minDistance = repulsionRadius;

            for (int i = 0; i < particleCount; i++)
            {
                if (densities[i] < targetDensity * 1.2f)
                    continue;

                List<int> nearby = GetParticlesInRadius(particlePositions[i], minDistance * 2f);

                foreach (int j in nearby)
                {
                    if (i >= j) continue;

                    Vector3 delta = particlePositions[i] - particlePositions[j];
                    float dist = delta.magnitude;

                    if (dist < minDistance && dist > 0.001f)
                    {
                        Vector3 dir = delta / dist;
                        float force = repulsionStrength * (1f - dist / minDistance);

                        particleVelocities[i] += dir * force * dt;
                        particleVelocities[j] -= dir * force * dt;
                    }
                }
            }
        }

        void UpdateParticleSPH(int i, float dt)
        {
            Vector3 pos = particlePositions[i];

            float invCellSize = 1f / grid.GetCellSize();

            Vector3 samplePos = pos * invCellSize;

            samplePos = new Vector3(
                Mathf.Clamp(samplePos.x, 1, simulationWidth - 2),
                Mathf.Clamp(samplePos.y, 1, gridHeight - 2),
                Mathf.Clamp(samplePos.z, 1, simulationHeight - 2)
            );

            Vector3 fluidVelocity = grid.SampleVelocity(samplePos);
            Vector3 acceleration = (fluidVelocity - particleVelocities[i]) * gridVelocityInfluence;

            // Adiciona força SPH
            acceleration += pressureForces[i];

            // Gravidade escalada
            acceleration += new Vector3(0, -9.81f, 0) * particleGravityScale;
            
            particleVelocities[i] += acceleration * dt;
            particleVelocities[i] *= Mathf.Pow(damping, dt * 60f);
            
            // Limita velocidade máxima
            float speed = particleVelocities[i].magnitude;
            if (speed > maxSpeed)
            {
                particleVelocities[i] *= maxSpeed / speed;
            }
            
            Vector3 newPos = pos + particleVelocities[i] * dt;
            
            // Colisões com paredes
            HandleWallCollisions(ref newPos, ref particleVelocities[i]);
            
            particlePositions[i] = newPos;
        }

        void HandleWallCollisions(ref Vector3 position, ref Vector3 velocity)
        {
            // Paredes em X
            if (position.x < wallMargin)
            {
                position.x = wallMargin;
                velocity.x = Mathf.Abs(velocity.x) * wallBounce;
                velocity.y *= wallFriction;
                velocity.z *= wallFriction;
            }
            else if (position.x > simulationWidth - 1 - wallMargin)
            {
                position.x = simulationWidth - 1 - wallMargin;
                velocity.x = -Mathf.Abs(velocity.x) * wallBounce;
                velocity.y *= wallFriction;
                velocity.z *= wallFriction;
            }
            
            // Paredes em Y
            if (position.y < wallMargin)
            {
                position.y = wallMargin;
                velocity.y = Mathf.Abs(velocity.y) * wallBounce * 0.5f;
                velocity.x *= wallFriction * 0.9f;
                velocity.z *= wallFriction * 0.9f;
            }
            else if (position.y > gridHeight - 1 - wallMargin)
            {
                position.y = gridHeight - 1 - wallMargin;
                velocity.y = -Mathf.Abs(velocity.y) * wallBounce;
                velocity.x *= wallFriction;
                velocity.z *= wallFriction;
            }
            
            // Paredes em Z
            if (position.z < wallMargin)
            {
                position.z = wallMargin;
                velocity.z = Mathf.Abs(velocity.z) * wallBounce;
                velocity.x *= wallFriction;
                velocity.y *= wallFriction;
            }
            else if (position.z > simulationHeight - 1 - wallMargin)
            {
                position.z = simulationHeight - 1 - wallMargin;
                velocity.z = -Mathf.Abs(velocity.z) * wallBounce;
                velocity.x *= wallFriction;
                velocity.y *= wallFriction;
            }
        }

        void OnDestroy()
        {
            particlePositionsBuffer?.Release();
        }

        public void ResetSimulation()
        {
            grid?.Reset();
            
            // Reinicializa as partículas
            switch (initializationMethod)
            {
                case InitializationMethod.Grid3D:
                    InitializeParticlesGrid3D();
                    break;
                case InitializationMethod.PoissonDisk:
                    InitializeParticlesPoissonDisk();
                    break;
                case InitializationMethod.RandomWithSpacing:
                    InitializeParticlesRandomWithSpacing();
                    break;
            }
            
            // Atualiza o buffer
            if (particlePositionsBuffer != null)
            {
                particlePositionsBuffer.SetData(particlePositions);
            }
        }

        public void AddVelocityAtPosition(Vector3 worldPosition, Vector3 velocity)
        {
            int gridX = Mathf.Clamp((int)worldPosition.x, 0, simulationWidth - 1);
            int gridY = Mathf.Clamp((int)worldPosition.y, 0, gridHeight - 1);
            int gridZ = Mathf.Clamp((int)worldPosition.z, 0, simulationHeight - 1);
            
            grid?.AddVelocity(gridX, gridY, gridZ, velocity);
        }

        public void AddDensityAtPosition(Vector3 worldPosition, float density)
        {
            int gridX = Mathf.Clamp((int)worldPosition.x, 0, simulationWidth - 1);
            int gridY = Mathf.Clamp((int)worldPosition.y, 0, gridHeight - 1);
            int gridZ = Mathf.Clamp((int)worldPosition.z, 0, simulationHeight - 1);
            
            grid?.AddDensity(gridX, gridY, gridZ, density);
        }
    }
}