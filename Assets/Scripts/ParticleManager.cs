using UnityEngine;
using System.Collections.Generic;

namespace CGProject
{
    /// <summary>
    /// Gestor principal da simulação de fluidos utilizando o método FLIP (Fluid Implicit Particle)
    /// combinado com interações entre partículas via SPH (Smoothed Particle Hydrodynamics).
    /// Esta classe gere a inicialização, atualização e renderização de partículas num ambiente 3D.
    /// </summary>
    public class ParticleManager : MonoBehaviour
    {
        // Configuração da grelha de simulação
        [Header("Configuração da Grelha")]
        [SerializeField] private int simulationWidth = 8;
        [SerializeField] private int simulationHeight = 8;
        [SerializeField] private int gridHeight = 16;
        [SerializeField, Range(0, 1)] float flipBlend = 0.95f; // Proporção de mistura entre FLIP e PIC
        [SerializeField] private int pressureIterations = 40; // Iterações do solver de pressão

        // Configuração das partículas
        [Header("Configuração das Partículas")]
        [SerializeField] private int particleCount = 500; // Número total de partículas
        [SerializeField] private float particleSize = 0.1f; // Tamanho visual das partículas
        [SerializeField] private Material particleMaterial; // Material para renderização
        [SerializeField] private int meshResolution = 1; // Resolução da mesh do octaedro
        [SerializeField] private float gravityScale = 20.0f; // Intensidade da gravidade
        
        // Configuração de colisões e densidade das partículas
        [Header("Colisões e Densidade das Partículas")]
        [SerializeField] private bool enableParticleCollisions = true; // Ativa colisões entre partículas
        [SerializeField] private float particleCollisionRadius = 0.12f; // Raio para colisões
        [SerializeField] private float particleStiffness = 0.1f; // Rigidez das colisões
        [SerializeField] private float particleDamping = 0.95f; // Amortecimento das colisões
        [SerializeField] private bool enableSPHDensity = true; // Ativa cálculo de densidade via SPH
        [SerializeField] private float smoothingRadius = 0.3f; // Raio de suavização para SPH
        [SerializeField] private float targetDensity = 2.0f; // Densidade alvo para pressão
        [SerializeField] private float pressureMultiplier = 1.0f; // Multiplicador da força de pressão
        [SerializeField] private float viscosityStrength = 0.1f; // Força da viscosidade

        // Shaders de computação
        [Header("Shaders de Computação")]
        [SerializeField] private ComputeShader gridComputeShader; // Shader para cálculos da grelha
        [SerializeField] private ComputeShader particleComputeShader; // Shader para interações entre partículas

        // Buffers para simulação FLIP
        private ComputeBuffer particlePositionsBuffer;
        private ComputeBuffer particleVelocitiesBuffer;
        private ComputeBuffer gridVelocityIntBuffer;
        private ComputeBuffer gridVelocityFloatBuffer;
        private ComputeBuffer gridWeight;
        private ComputeBuffer divergence;
        private ComputeBuffer pressure;
        private ComputeBuffer pressureTemp;
        
        // Buffers para interações entre partículas
        private ComputeBuffer particleDensityBuffer;
        private ComputeBuffer particleForcesBuffer;
        private ComputeBuffer spatialGridBuffer;
        private ComputeBuffer spatialGridCountBuffer;
        private ComputeBuffer particleNeighborsBuffer;

        // Arrays para armazenamento de dados das partículas
        private Vector3[] particlePositions;
        private Vector3[] particleVelocities;
        private float[] particleDensities;

        // Mesh para renderização
        private Mesh particleMesh;

        // Dimensões da grelha
        private int gridCellsX, gridCellsY, gridCellsZ;
        private float cellSize = 1.0f;
        private bool isInitialized = false;
        
        // Particionamento espacial para otimização de colisões
        private int spatialGridSizeX, spatialGridSizeY, spatialGridSizeZ;
        private int spatialGridTotalCells;
        private const int MAX_PARTICLES_PER_CELL = 16; // Máximo de partículas por célula da grelha espacial

        /// <summary>
        /// Inicializa a simulação ao iniciar o componente
        /// </summary>
        void Start()
        {
            InitializeSimulation();
        }

        /// <summary>
        /// Configura todos os componentes necessários para a simulação
        /// </summary>
        void InitializeSimulation()
        {
            Debug.Log("Inicialização da simulação de fluidos FLIP com colisões entre partículas...");
            
            // Cria mesh do octaedro para representação visual das partículas
            particleMesh = FluidGenerator.MeshGenerator(meshResolution);
            
            // Calcula dimensões da grelha principal de simulação
            gridCellsX = Mathf.Max(1, Mathf.CeilToInt(simulationWidth / cellSize));
            gridCellsY = Mathf.Max(1, Mathf.CeilToInt(gridHeight / cellSize));
            gridCellsZ = Mathf.Max(1, Mathf.CeilToInt(simulationHeight / cellSize));
            int totalCells = gridCellsX * gridCellsY * gridCellsZ;

            // Calcula dimensões da grelha espacial para otimização de colisões
            float spatialCellSize = smoothingRadius * 0.5f;
            spatialGridSizeX = Mathf.Max(1, Mathf.CeilToInt(simulationWidth / spatialCellSize));
            spatialGridSizeY = Mathf.Max(1, Mathf.CeilToInt(gridHeight / spatialCellSize));
            spatialGridSizeZ = Mathf.Max(1, Mathf.CeilToInt(simulationHeight / spatialCellSize));
            spatialGridTotalCells = spatialGridSizeX * spatialGridSizeY * spatialGridSizeZ;

            Debug.Log($"Dimensões da grelha: {gridCellsX}x{gridCellsY}x{gridCellsZ} = {totalCells} células");
            Debug.Log($"Grelha espacial: {spatialGridSizeX}x{spatialGridSizeY}x{spatialGridSizeZ} = {spatialGridTotalCells} células");

            // Validação de parâmetros de entrada
            if (particleCount <= 0 || gridCellsX <= 0 || gridCellsY <= 0 || gridCellsZ <= 0)
            {
                Debug.LogError("Parâmetros de simulação inválidos!");
                return;
            }

            // Inicializa arrays de dados das partículas
            particlePositions = new Vector3[particleCount];
            particleVelocities = new Vector3[particleCount];
            particleDensities = new float[particleCount];

            // Configura posições iniciais das partículas
            InitializeParticles();

            // Cria buffers de computação
            CreateComputeBuffers(totalCells);

            // Preenche buffers com dados iniciais
            particlePositionsBuffer.SetData(particlePositions);
            particleVelocitiesBuffer.SetData(particleVelocities);

            // Configura material de renderização com buffers adequados
            if (particleMaterial != null)
            {
                particleMaterial.SetBuffer("_ParticlePositions", particlePositionsBuffer);
                particleMaterial.SetBuffer("_ParticleVelocities", particleVelocitiesBuffer);
                particleMaterial.SetBuffer("_ParticleDensities", particleDensityBuffer);
                particleMaterial.SetFloat("_ParticleSize", particleSize);
            }

            isInitialized = true;
            Debug.Log($"Simulação FLIP inicializada com {particleCount} partículas");
            Debug.Log($"Resolução da mesh do octaedro: {meshResolution}");
        }

        /// <summary>
        /// Cria todos os buffers de computação necessários para a simulação
        /// </summary>
        void CreateComputeBuffers(int totalCells)
        {
            // Buffers para simulação FLIP
            particlePositionsBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            particleVelocitiesBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            gridVelocityIntBuffer = new ComputeBuffer(totalCells, sizeof(int) * 3);
            gridVelocityFloatBuffer = new ComputeBuffer(totalCells, sizeof(float) * 3);
            gridWeight = new ComputeBuffer(totalCells, sizeof(uint));
            divergence = new ComputeBuffer(totalCells, sizeof(float));
            pressure = new ComputeBuffer(totalCells, sizeof(float));
            pressureTemp = new ComputeBuffer(totalCells, sizeof(float));
            
            // Buffers para interações entre partículas
            particleDensityBuffer = new ComputeBuffer(particleCount, sizeof(float));
            particleForcesBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            spatialGridBuffer = new ComputeBuffer(spatialGridTotalCells * MAX_PARTICLES_PER_CELL, sizeof(int));
            spatialGridCountBuffer = new ComputeBuffer(spatialGridTotalCells, sizeof(int));
            particleNeighborsBuffer = new ComputeBuffer(particleCount * 64, sizeof(int));

            // Inicializa buffers com valores zero
            InitializeBuffersToZero(totalCells);
        }

        /// <summary>
        /// Inicializa todos os buffers com valores zero
        /// </summary>
        void InitializeBuffersToZero(int totalCells)
        {
            // Arrays temporários para inicialização
            int[] zeroInts = new int[totalCells * 3];
            Vector3[] zeroVectors = new Vector3[totalCells];
            uint[] zeroUints = new uint[totalCells];
            float[] zeroFloats = new float[totalCells];
            
            // Inicializa buffers FLIP
            gridVelocityIntBuffer.SetData(zeroInts);
            gridVelocityFloatBuffer.SetData(zeroVectors);
            gridWeight.SetData(zeroUints);
            divergence.SetData(zeroFloats);
            pressure.SetData(zeroFloats);
            pressureTemp.SetData(zeroFloats);
            
            // Arrays temporários para buffers de partículas
            int[] zeroSpatialGrid = new int[spatialGridTotalCells * MAX_PARTICLES_PER_CELL];
            int[] zeroSpatialCounts = new int[spatialGridTotalCells];
            int[] zeroNeighbors = new int[particleCount * 64];
            float[] zeroDensities = new float[particleCount];
            Vector3[] zeroForces = new Vector3[particleCount];
            
            // Inicializa buffers de partículas
            spatialGridBuffer.SetData(zeroSpatialGrid);
            spatialGridCountBuffer.SetData(zeroSpatialCounts);
            particleNeighborsBuffer.SetData(zeroNeighbors);
            particleDensityBuffer.SetData(zeroDensities);
            particleForcesBuffer.SetData(zeroForces);
        }

        /// <summary>
        /// Configura as posições iniciais das partículas numa formação densa
        /// </summary>
        void InitializeParticles()
        {            
            // Define centro da formação de partículas
            Vector3 center = new Vector3(
                simulationWidth * 0.5f,
                gridHeight * 0.8f,
                simulationHeight * 0.5f
            );
            
            // Calcula espaçamento baseado no raio de colisão
            int particlesPerDimension = Mathf.CeilToInt(Mathf.Pow(particleCount, 1f/3f));
            float spacing = particleCollisionRadius * 0.8f; // Espaçamento inicial ligeiramente comprimido
            
            // Distribui partículas numa grelha tridimensional
            int index = 0;
            for (int x = 0; x < particlesPerDimension && index < particleCount; x++)
            {
                for (int y = 0; y < particlesPerDimension && index < particleCount; y++)
                {
                    for (int z = 0; z < particlesPerDimension && index < particleCount; z++)
                    {
                        particlePositions[index] = new Vector3(
                            center.x + (x - particlesPerDimension/2f) * spacing,
                            center.y + (y - particlesPerDimension/2f) * spacing,
                            center.z + (z - particlesPerDimension/2f) * spacing
                        );
                        particleVelocities[index] = Vector3.zero;
                        particleDensities[index] = targetDensity;
                        index++;
                    }
                }
            }
            
            // Preenche partículas restantes com posições aleatórias
            while (index < particleCount)
            {
                particlePositions[index] = new Vector3(
                    Random.Range(center.x - 1f, center.x + 1f),
                    Random.Range(center.y - 1f, center.y + 1f),
                    Random.Range(center.z - 1f, center.z + 1f)
                );
                particleVelocities[index] = Vector3.zero;
                particleDensities[index] = targetDensity;
                index++;
            }
            
            Debug.Log($"Inicializadas {index} partículas numa formação densa");
        }

        /// <summary>
        /// Atualiza a simulação em cada frame
        /// </summary>
        void Update()
        {
            // Verifica se a simulação está inicializada
            if (!isInitialized || gridComputeShader == null || particleComputeShader == null) return;

            // Limita o delta time para estabilidade numérica
            float dt = Mathf.Min(0.016f, Time.deltaTime);
            dt = Mathf.Clamp(dt, 0.001f, 0.033f); // Intervalo correspondente a 30-1000 FPS
            
            // Processa interações entre partículas (SPH e colisões)
            if (enableParticleCollisions || enableSPHDensity)
            {
                ProcessParticleInteractions(dt);
            }
            
            // Executa simulação FLIP principal
            ProcessFLIPSimulation(dt);
            
            // Renderiza partículas
            RenderParticles();
            
            // Exibe informações de depuração periodicamente
            if (Time.frameCount % 60 == 0)
            {
                LogDebugInfo();
            }
        }

        /// <summary>
        /// Processa interações entre partículas utilizando SPH e colisões
        /// </summary>
        void ProcessParticleInteractions(float dt)
        {
            // Identifica kernels do shader de partículas
            int buildSpatialGridKernel = particleComputeShader.FindKernel("BuildSpatialGrid");
            int findNeighborsKernel = particleComputeShader.FindKernel("FindNeighbors");
            int computeDensityKernel = particleComputeShader.FindKernel("ComputeDensity");
            int computeForcesKernel = particleComputeShader.FindKernel("ComputeForces");
            int applyForcesKernel = particleComputeShader.FindKernel("ApplyForces");

            // Verifica se os kernels foram encontrados
            if (buildSpatialGridKernel == -1)
            {
                Debug.LogError("Kernels de interação entre partículas não encontrados!");
                return;
            }

            // Calcula dimensões de dispatch para execução paralela
            int spatialGroupsX = Mathf.Max(1, Mathf.CeilToInt(spatialGridSizeX / 8f));
            int spatialGroupsY = Mathf.Max(1, Mathf.CeilToInt(spatialGridSizeY / 8f));
            int spatialGroupsZ = Mathf.Max(1, Mathf.CeilToInt(spatialGridSizeZ / 4f));
            int particleGroups = Mathf.CeilToInt(particleCount / 256f);

            // Configura parâmetros do shader de partículas
            particleComputeShader.SetInt("particleCount", particleCount);
            particleComputeShader.SetInt("spatialGridSizeX", spatialGridSizeX);
            particleComputeShader.SetInt("spatialGridSizeY", spatialGridSizeY);
            particleComputeShader.SetInt("spatialGridSizeZ", spatialGridSizeZ);
            particleComputeShader.SetFloat("dt", dt);
            particleComputeShader.SetFloat("smoothingRadius", smoothingRadius);
            particleComputeShader.SetFloat("collisionRadius", particleCollisionRadius);
            particleComputeShader.SetFloat("targetDensity", targetDensity);
            particleComputeShader.SetFloat("pressureMultiplier", pressureMultiplier);
            particleComputeShader.SetFloat("viscosityStrength", viscosityStrength);
            particleComputeShader.SetFloat("stiffness", particleStiffness);
            particleComputeShader.SetFloat("damping", particleDamping);
            particleComputeShader.SetInt("maxParticlesPerCell", MAX_PARTICLES_PER_CELL);
            particleComputeShader.SetVector("simulationBoundsMin", Vector3.zero);
            particleComputeShader.SetVector("simulationBoundsMax", 
                new Vector3(simulationWidth, gridHeight, simulationHeight));

            // Converte booleanos para inteiros (HLSL não suporta bool diretamente)
            int enableCollisionsInt = enableParticleCollisions ? 1 : 0;
            int enableSPHInt = enableSPHDensity ? 1 : 0;

            particleComputeShader.SetInt("enableCollisions", enableCollisionsInt);
            particleComputeShader.SetInt("enableSPH", enableSPHInt);

            // Constrói grelha espacial para otimização
            particleComputeShader.SetBuffer(buildSpatialGridKernel, "particlePositions", particlePositionsBuffer);
            particleComputeShader.SetBuffer(buildSpatialGridKernel, "spatialGrid", spatialGridBuffer);
            particleComputeShader.SetBuffer(buildSpatialGridKernel, "spatialGridCount", spatialGridCountBuffer);
            particleComputeShader.Dispatch(buildSpatialGridKernel, spatialGroupsX, spatialGroupsY, spatialGroupsZ);
            
            // Identifica vizinhos de cada partícula
            particleComputeShader.SetBuffer(findNeighborsKernel, "particlePositions", particlePositionsBuffer);
            particleComputeShader.SetBuffer(findNeighborsKernel, "spatialGrid", spatialGridBuffer);
            particleComputeShader.SetBuffer(findNeighborsKernel, "spatialGridCount", spatialGridCountBuffer);
            particleComputeShader.SetBuffer(findNeighborsKernel, "particleNeighbors", particleNeighborsBuffer);
            particleComputeShader.Dispatch(findNeighborsKernel, particleGroups, 1, 1);
            
            // Calcula densidades (apenas se SPH ativado)
            if (enableSPHDensity)
            {
                particleComputeShader.SetBuffer(computeDensityKernel, "particlePositions", particlePositionsBuffer);
                particleComputeShader.SetBuffer(computeDensityKernel, "particleNeighbors", particleNeighborsBuffer);
                particleComputeShader.SetBuffer(computeDensityKernel, "particleDensities", particleDensityBuffer);
                particleComputeShader.Dispatch(computeDensityKernel, particleGroups, 1, 1);
            }
            
            // Calcula forças (pressão, viscosidade e colisões)
            particleComputeShader.SetBuffer(computeForcesKernel, "particlePositions", particlePositionsBuffer);
            particleComputeShader.SetBuffer(computeForcesKernel, "particleVelocities", particleVelocitiesBuffer);
            particleComputeShader.SetBuffer(computeForcesKernel, "particleDensities", particleDensityBuffer);
            particleComputeShader.SetBuffer(computeForcesKernel, "particleNeighbors", particleNeighborsBuffer);
            particleComputeShader.SetBuffer(computeForcesKernel, "particleForces", particleForcesBuffer);
            particleComputeShader.Dispatch(computeForcesKernel, particleGroups, 1, 1);
            
            // Aplica forças calculadas às partículas
            particleComputeShader.SetBuffer(applyForcesKernel, "particleVelocities", particleVelocitiesBuffer);
            particleComputeShader.SetBuffer(applyForcesKernel, "particleForces", particleForcesBuffer);
            particleComputeShader.Dispatch(applyForcesKernel, particleGroups, 1, 1);
        }

        /// <summary>
        /// Executa a simulação FLIP principal
        /// </summary>
        void ProcessFLIPSimulation(float dt)
        {
            // Identifica kernels do shader da grelha
            int clearKernel = gridComputeShader.FindKernel("ClearGrid");
            int transferKernel = gridComputeShader.FindKernel("TransferToGrid");
            int normalizeKernel = gridComputeShader.FindKernel("NormalizeGrid");
            int advectKernel = gridComputeShader.FindKernel("AdvectVelocity");
            int divergenceKernel = gridComputeShader.FindKernel("ComputeDivergence");
            int jacobiKernel = gridComputeShader.FindKernel("JacobiPressure");
            int subtractKernel = gridComputeShader.FindKernel("SubtractPressureGradient");
            int gridToParticlesKernel = gridComputeShader.FindKernel("GridToParticles");

            // Verifica se os kernels foram encontrados
            if (clearKernel == -1)
            {
                Debug.LogError("Kernels da simulação FLIP não encontrados!");
                return;
            }

            // Calcula dimensões de dispatch para execução paralela
            int gridGroupsX = Mathf.Max(1, Mathf.CeilToInt(gridCellsX / 8f));
            int gridGroupsY = Mathf.Max(1, Mathf.CeilToInt(gridCellsY / 8f));
            int gridGroupsZ = Mathf.Max(1, Mathf.CeilToInt(gridCellsZ / 4f));
            int particleGroups = Mathf.CeilToInt(particleCount / 256f);

            // Configura parâmetros do shader da grelha
            gridComputeShader.SetInt("gridSizeX", gridCellsX);
            gridComputeShader.SetInt("gridSizeY", gridCellsY);
            gridComputeShader.SetInt("gridSizeZ", gridCellsZ);
            gridComputeShader.SetInt("particleCount", particleCount);
            gridComputeShader.SetInt("numJacobiIterations", pressureIterations);
            gridComputeShader.SetFloat("dt", dt);
            gridComputeShader.SetFloat("cellSize", cellSize);
            gridComputeShader.SetFloat("flipRatio", flipBlend);
            gridComputeShader.SetVector("gravity", new Vector3(0, -gravityScale, 0));
            gridComputeShader.SetFloat("particleCollisionRadius", particleCollisionRadius);

            // Limpa buffers da grelha
            gridComputeShader.SetBuffer(clearKernel, "gridVelocityInt", gridVelocityIntBuffer);
            gridComputeShader.SetBuffer(clearKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.SetBuffer(clearKernel, "gridWeight", gridWeight);
            gridComputeShader.SetBuffer(clearKernel, "divergence", divergence);
            gridComputeShader.SetBuffer(clearKernel, "pressure", pressure);
            gridComputeShader.SetBuffer(clearKernel, "pressureTemp", pressureTemp);
            gridComputeShader.Dispatch(clearKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // Transfere partículas para a grelha
            gridComputeShader.SetBuffer(transferKernel, "particlePositions", particlePositionsBuffer);
            gridComputeShader.SetBuffer(transferKernel, "particleVelocities", particleVelocitiesBuffer);
            gridComputeShader.SetBuffer(transferKernel, "gridVelocityInt", gridVelocityIntBuffer);
            gridComputeShader.SetBuffer(transferKernel, "gridWeight", gridWeight);
            gridComputeShader.Dispatch(transferKernel, particleGroups, 1, 1);

            // Normaliza velocidades na grelha
            gridComputeShader.SetBuffer(normalizeKernel, "gridVelocityInt", gridVelocityIntBuffer);
            gridComputeShader.SetBuffer(normalizeKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.SetBuffer(normalizeKernel, "gridWeight", gridWeight);
            gridComputeShader.Dispatch(normalizeKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // Advecta velocidade na grelha
            gridComputeShader.SetBuffer(advectKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.Dispatch(advectKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // Calcula divergência do campo de velocidade
            gridComputeShader.SetBuffer(divergenceKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.SetBuffer(divergenceKernel, "divergence", divergence);
            gridComputeShader.Dispatch(divergenceKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // Resolve equação de Poisson para pressão (método de Jacobi)
            for (int i = 0; i < pressureIterations; i++)
            {
                gridComputeShader.SetBuffer(jacobiKernel, "divergence", divergence);
                gridComputeShader.SetBuffer(jacobiKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
                gridComputeShader.SetBuffer(jacobiKernel, "pressure", pressure);
                gridComputeShader.SetBuffer(jacobiKernel, "pressureTemp", pressureTemp);
                gridComputeShader.Dispatch(jacobiKernel, gridGroupsX, gridGroupsY, gridGroupsZ);
                
                // Alterna buffers de pressão para iteração seguinte
                ComputeBuffer temp = pressure;
                pressure = pressureTemp;
                pressureTemp = temp;
            }

            // Subtrai gradiente de pressão do campo de velocidade
            gridComputeShader.SetBuffer(subtractKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.SetBuffer(subtractKernel, "pressure", pressure);
            gridComputeShader.Dispatch(subtractKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // Transfere velocidades da grelha para as partículas
            gridComputeShader.SetBuffer(gridToParticlesKernel, "particlePositions", particlePositionsBuffer);
            gridComputeShader.SetBuffer(gridToParticlesKernel, "particleVelocities", particleVelocitiesBuffer);
            gridComputeShader.SetBuffer(gridToParticlesKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.Dispatch(gridToParticlesKernel, particleGroups, 1, 1);

            // Lê dados atualizados dos buffers para arrays CPU
            particlePositionsBuffer.GetData(particlePositions);
            particleVelocitiesBuffer.GetData(particleVelocities);
            if (enableSPHDensity)
            {
                particleDensityBuffer.GetData(particleDensities);
            }
        }

        /// <summary>
        /// Renderiza as partículas utilizando instanciação procedural
        /// </summary>
        void RenderParticles()
        {
            if (particleMaterial != null && particleMesh != null)
            {
                // Define limites de renderização baseados nas dimensões da simulação
                Bounds bounds = new Bounds(
                    new Vector3(simulationWidth * 0.5f, gridHeight * 0.5f, simulationHeight * 0.5f),
                    new Vector3(simulationWidth, gridHeight, simulationHeight)
                );

                // Desenha instâncias da mesh com dados das partículas
                Graphics.DrawMeshInstancedProcedural(
                    particleMesh,
                    0,
                    particleMaterial,
                    bounds,
                    particleCount
                );
            }
        }

        /// <summary>
        /// Regista informações sobre o estado da simulação
        /// </summary>
        void LogDebugInfo()
        {
            float avgVelocity = 0f;
            float maxVelocity = 0f;
            float avgDensity = 0f;
            
            // Calcula estatísticas das partículas
            for (int i = 0; i < particleCount; i++)
            {
                float vel = particleVelocities[i].magnitude;
                avgVelocity += vel;
                avgDensity += particleDensities[i];
                if (vel > maxVelocity) maxVelocity = vel;
            }
            avgVelocity /= particleCount;
            avgDensity /= particleCount;
            
            // Regista informações detalhadas periodicamente
            if (Time.frameCount % 300 == 0) // A cada 5 segundos (assumindo 60 FPS)
            {
                Debug.Log($"Frame {Time.frameCount}: Velocidade Média = {avgVelocity:F3}, Velocidade Máxima = {maxVelocity:F3}, Densidade Média = {avgDensity:F3}");
            }
        }

        /// <summary>
        /// Liberta recursos ao destruir o componente
        /// </summary>
        void OnDestroy()
        {
            // Liberta todos os buffers de computação
            particlePositionsBuffer?.Release();
            particleVelocitiesBuffer?.Release();
            gridVelocityIntBuffer?.Release();
            gridVelocityFloatBuffer?.Release();
            gridWeight?.Release();
            divergence?.Release();
            pressure?.Release();
            pressureTemp?.Release();
            
            // Liberta buffers de interações entre partículas
            particleDensityBuffer?.Release();
            particleForcesBuffer?.Release();
            spatialGridBuffer?.Release();
            spatialGridCountBuffer?.Release();
            particleNeighborsBuffer?.Release();
        }

        /// <summary>
        /// Renderiza interface gráfica para controlo da simulação
        /// </summary>
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 400, 600));
            GUILayout.Label("Simulação de Fluidos FLIP - Versão Melhorada");
            GUILayout.Label($"Partículas: {particleCount}");
            GUILayout.Label($"Grelha: {gridCellsX}x{gridCellsY}x{gridCellsZ}");
            GUILayout.Label($"Gravidade: {gravityScale}");
            GUILayout.Label($"Mistura FLIP: {flipBlend}");
            GUILayout.Label($"Resolução da Mesh: {meshResolution}");
            
            GUILayout.Space(10);
            GUILayout.Label("=== Interações entre Partículas ===");
            
            // Controlos para colisões entre partículas
            enableParticleCollisions = GUILayout.Toggle(enableParticleCollisions, "Ativar Colisões entre Partículas");
            if (enableParticleCollisions)
            {
                GUILayout.Label("Raio de Colisão:");
                particleCollisionRadius = GUILayout.HorizontalSlider(particleCollisionRadius, 0.05f, 0.3f);
                GUILayout.Label("Rigidez:");
                particleStiffness = GUILayout.HorizontalSlider(particleStiffness, 0.01f, 1.0f);
                GUILayout.Label("Amortecimento:");
                particleDamping = GUILayout.HorizontalSlider(particleDamping, 0.8f, 1.0f);
            }
            
            // Controlos para densidade SPH
            enableSPHDensity = GUILayout.Toggle(enableSPHDensity, "Ativar Densidade SPH");
            if (enableSPHDensity)
            {
                GUILayout.Label("Raio de Suavização:");
                smoothingRadius = GUILayout.HorizontalSlider(smoothingRadius, 0.1f, 0.5f);
                GUILayout.Label("Densidade Alvo:");
                targetDensity = GUILayout.HorizontalSlider(targetDensity, 0.5f, 5.0f);
                GUILayout.Label("Multiplicador de Pressão:");
                pressureMultiplier = GUILayout.HorizontalSlider(pressureMultiplier, 0.1f, 3.0f);
                GUILayout.Label("Viscosidade:");
                viscosityStrength = GUILayout.HorizontalSlider(viscosityStrength, 0.01f, 0.5f);
            }
            
            GUILayout.Space(10);
            // Botão para reiniciar simulação
            if (GUILayout.Button("Reiniciar Simulação"))
            {
                InitializeParticles();
                particlePositionsBuffer.SetData(particlePositions);
                particleVelocitiesBuffer.SetData(particleVelocities);
            }
            
            GUILayout.EndArea();
        }
    }
}