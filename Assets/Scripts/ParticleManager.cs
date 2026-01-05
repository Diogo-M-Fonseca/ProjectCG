using UnityEngine;
using System.Collections.Generic;

namespace CGProject
{
    public class ParticleManager : MonoBehaviour
    {
        [Header("Grid Configuration")]
        [SerializeField] private int simulationWidth = 8;
        [SerializeField] private int simulationHeight = 8;
        [SerializeField] private int gridHeight = 16;
        [SerializeField, Range(0, 1)] float flipBlend = 0.95f;
        [SerializeField] private int pressureIterations = 40;

        [Header("Particle Configuration")]
        [SerializeField] private int particleCount = 500;
        [SerializeField] private float particleSize = 0.1f;
        [SerializeField] private Material particleMaterial;
        [SerializeField] private int meshResolution = 1;
        [SerializeField] private float gravityScale = 20.0f;
        
        [Header("Particle Collision & Density")]
        [SerializeField] private bool enableParticleCollisions = true;
        [SerializeField] private float particleCollisionRadius = 0.12f;
        [SerializeField] private float particleStiffness = 0.1f;
        [SerializeField] private float particleDamping = 0.95f;
        [SerializeField] private bool enableSPHDensity = true;
        [SerializeField] private float smoothingRadius = 0.3f;
        [SerializeField] private float targetDensity = 2.0f;
        [SerializeField] private float pressureMultiplier = 1.0f;
        [SerializeField] private float viscosityStrength = 0.1f;

        [Header("Compute Shaders")]
        [SerializeField] private ComputeShader gridComputeShader;
        [SerializeField] private ComputeShader particleComputeShader;

        // Buffers
        private ComputeBuffer particlePositionsBuffer;
        private ComputeBuffer particleVelocitiesBuffer;
        private ComputeBuffer gridVelocityIntBuffer;
        private ComputeBuffer gridVelocityFloatBuffer;
        private ComputeBuffer gridWeight;
        private ComputeBuffer divergence;
        private ComputeBuffer pressure;
        private ComputeBuffer pressureTemp;
        
        private ComputeBuffer particleDensityBuffer;
        private ComputeBuffer particleForcesBuffer;
        private ComputeBuffer spatialGridBuffer;
        private ComputeBuffer spatialGridCountBuffer;
        private ComputeBuffer particleNeighborsBuffer;

        private Vector3[] particlePositions;
        private Vector3[] particleVelocities;
        private float[] particleDensities;

        private Mesh particleMesh;
        private int gridCellsX, gridCellsY, gridCellsZ;
        private float cellSize = 1.0f;
        private bool isInitialized = false;
        
        private int spatialGridSizeX, spatialGridSizeY, spatialGridSizeZ;
        private int spatialGridTotalCells;
        private const int MAX_PARTICLES_PER_CELL = 16;

        // Track changes for debugging
        private float lastGravityScale = 20.0f;
        private bool lastEnableCollisions = true;
        private bool lastEnableSPH = true;
        private float lastFlipBlend = 0.95f;

        void Start()
        {
            InitializeSimulation();
        }

        void InitializeSimulation()
        {
            Debug.Log("Initializing FLIP fluid simulation...");
            
            // Clean up if already initialized
            if (isInitialized)
            {
                ReleaseBuffers();
            }
            
            particleMesh = FluidGenerator.MeshGenerator(meshResolution);
            
            // Calculate grid dimensions
            gridCellsX = Mathf.Max(1, Mathf.CeilToInt(simulationWidth / cellSize));
            gridCellsY = Mathf.Max(1, Mathf.CeilToInt(gridHeight / cellSize));
            gridCellsZ = Mathf.Max(1, Mathf.CeilToInt(simulationHeight / cellSize));
            int totalCells = gridCellsX * gridCellsY * gridCellsZ;

            // Calculate spatial grid dimensions
            float spatialCellSize = smoothingRadius * 0.5f;
            spatialGridSizeX = Mathf.Max(1, Mathf.CeilToInt(simulationWidth / spatialCellSize));
            spatialGridSizeY = Mathf.Max(1, Mathf.CeilToInt(gridHeight / spatialCellSize));
            spatialGridSizeZ = Mathf.Max(1, Mathf.CeilToInt(simulationHeight / spatialCellSize));
            spatialGridTotalCells = spatialGridSizeX * spatialGridSizeY * spatialGridSizeZ;

            Debug.Log($"Grid: {gridCellsX}x{gridCellsY}x{gridCellsZ}");
            Debug.Log($"Spatial Grid: {spatialGridSizeX}x{spatialGridSizeY}x{spatialGridSizeZ}");

            // Validate
            if (particleCount <= 0 || gridCellsX <= 0 || gridCellsY <= 0 || gridCellsZ <= 0)
            {
                Debug.LogError("Invalid parameters!");
                return;
            }

            // Initialize arrays
            particlePositions = new Vector3[particleCount];
            particleVelocities = new Vector3[particleCount];
            particleDensities = new float[particleCount];

            InitializeParticles();

            // Create buffers
            CreateComputeBuffers(totalCells);

            // Set initial data
            particlePositionsBuffer.SetData(particlePositions);
            particleVelocitiesBuffer.SetData(particleVelocities);

            // Setup material
            if (particleMaterial != null)
            {
                particleMaterial.SetBuffer("_ParticlePositions", particlePositionsBuffer);
                particleMaterial.SetBuffer("_ParticleVelocities", particleVelocitiesBuffer);
                particleMaterial.SetBuffer("_ParticleDensities", particleDensityBuffer);
                particleMaterial.SetFloat("_ParticleSize", particleSize);
            }

            isInitialized = true;
            Debug.Log($"Simulation initialized with {particleCount} particles");
        }

        void CreateComputeBuffers(int totalCells)
        {
            // Create FLIP buffers
            particlePositionsBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            particleVelocitiesBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            gridVelocityIntBuffer = new ComputeBuffer(totalCells, sizeof(int) * 3);
            gridVelocityFloatBuffer = new ComputeBuffer(totalCells, sizeof(float) * 3);
            gridWeight = new ComputeBuffer(totalCells, sizeof(uint));
            divergence = new ComputeBuffer(totalCells, sizeof(float));
            pressure = new ComputeBuffer(totalCells, sizeof(float));
            pressureTemp = new ComputeBuffer(totalCells, sizeof(float));
            
            // Create particle interaction buffers
            particleDensityBuffer = new ComputeBuffer(particleCount, sizeof(float));
            particleForcesBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            spatialGridBuffer = new ComputeBuffer(spatialGridTotalCells * MAX_PARTICLES_PER_CELL, sizeof(int));
            spatialGridCountBuffer = new ComputeBuffer(spatialGridTotalCells, sizeof(int));
            particleNeighborsBuffer = new ComputeBuffer(particleCount * 64, sizeof(int));

            // Initialize to zero
            InitializeBuffersToZero(totalCells);
        }

        void InitializeBuffersToZero(int totalCells)
        {
            // Initialize FLIP buffers
            int[] zeroInts = new int[totalCells * 3];
            Vector3[] zeroVectors = new Vector3[totalCells];
            uint[] zeroUints = new uint[totalCells];
            float[] zeroFloats = new float[totalCells];
            
            gridVelocityIntBuffer.SetData(zeroInts);
            gridVelocityFloatBuffer.SetData(zeroVectors);
            gridWeight.SetData(zeroUints);
            divergence.SetData(zeroFloats);
            pressure.SetData(zeroFloats);
            pressureTemp.SetData(zeroFloats);
            
            // Initialize particle buffers
            int[] zeroSpatialGrid = new int[spatialGridTotalCells * MAX_PARTICLES_PER_CELL];
            int[] zeroSpatialCounts = new int[spatialGridTotalCells];
            int[] zeroNeighbors = new int[particleCount * 64];
            float[] zeroDensities = new float[particleCount];
            Vector3[] zeroForces = new Vector3[particleCount];
            
            spatialGridBuffer.SetData(zeroSpatialGrid);
            spatialGridCountBuffer.SetData(zeroSpatialCounts);
            particleNeighborsBuffer.SetData(zeroNeighbors);
            particleDensityBuffer.SetData(zeroDensities);
            particleForcesBuffer.SetData(zeroForces);
        }

        void InitializeParticles()
        {
            Vector3 center = new Vector3(
                simulationWidth * 0.5f,
                gridHeight * 0.8f,
                simulationHeight * 0.5f
            );
            
            int particlesPerDimension = Mathf.CeilToInt(Mathf.Pow(particleCount, 1f/3f));
            float spacing = particleCollisionRadius * 0.8f;
            
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
            
            Debug.Log($"Initialized {index} particles");
        }

        void Update()
        {
            if (!isInitialized || gridComputeShader == null || particleComputeShader == null) 
                return;

            float dt = Mathf.Min(0.016f, Time.deltaTime);
            dt = Mathf.Clamp(dt, 0.001f, 0.033f);
            
            // Check for parameter changes
            bool parametersChanged = CheckParameterChanges();
            
            // Process particle interactions
            if (enableParticleCollisions || enableSPHDensity)
            {
                ProcessParticleInteractions(dt, parametersChanged);
            }
            
            // Process FLIP simulation
            ProcessFLIPSimulation(dt, parametersChanged);
            
            // Render
            RenderParticles();
            
            // Debug info
            if (Time.frameCount % 60 == 0)
            {
                LogDebugInfo();
            }
        }

        bool CheckParameterChanges()
        {
            bool changed = false;
            
            if (gravityScale != lastGravityScale)
            {
                Debug.Log($"Gravity changed: {lastGravityScale} -> {gravityScale}");
                lastGravityScale = gravityScale;
                changed = true;
            }
            
            if (enableParticleCollisions != lastEnableCollisions)
            {
                Debug.Log($"Collisions changed: {lastEnableCollisions} -> {enableParticleCollisions}");
                lastEnableCollisions = enableParticleCollisions;
                changed = true;
            }
            
            if (enableSPHDensity != lastEnableSPH)
            {
                Debug.Log($"SPH changed: {lastEnableSPH} -> {enableSPHDensity}");
                lastEnableSPH = enableSPHDensity;
                changed = true;
            }
            
            if (flipBlend != lastFlipBlend)
            {
                Debug.Log($"FLIP blend changed: {lastFlipBlend} -> {flipBlend}");
                lastFlipBlend = flipBlend;
                changed = true;
            }
            
            return changed;
        }

        void ProcessParticleInteractions(float dt, bool parametersChanged)
        {
            // Find kernels
            int buildSpatialGridKernel = particleComputeShader.FindKernel("BuildSpatialGrid");
            int findNeighborsKernel = particleComputeShader.FindKernel("FindNeighbors");
            int computeDensityKernel = particleComputeShader.FindKernel("ComputeDensity");
            int computeForcesKernel = particleComputeShader.FindKernel("ComputeForces");
            int applyForcesKernel = particleComputeShader.FindKernel("ApplyForces");

            if (buildSpatialGridKernel == -1)
            {
                Debug.LogError("Particle interaction kernels not found!");
                return;
            }

            // Calculate dispatch sizes
            int spatialGroupsX = Mathf.Max(1, Mathf.CeilToInt(spatialGridSizeX / 8f));
            int spatialGroupsY = Mathf.Max(1, Mathf.CeilToInt(spatialGridSizeY / 8f));
            int spatialGroupsZ = Mathf.Max(1, Mathf.CeilToInt(spatialGridSizeZ / 4f));
            int particleGroups = Mathf.CeilToInt(particleCount / 256f);

            // ***** CRITICAL: SET ALL PARAMETERS EVERY FRAME *****
            // Set parameters for particle compute shader
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

            // Set bools as ints
            particleComputeShader.SetInt("enableCollisions", enableParticleCollisions ? 1 : 0);
            particleComputeShader.SetInt("enableSPH", enableSPHDensity ? 1 : 0);

            // Build spatial grid
            particleComputeShader.SetBuffer(buildSpatialGridKernel, "particlePositions", particlePositionsBuffer);
            particleComputeShader.SetBuffer(buildSpatialGridKernel, "spatialGrid", spatialGridBuffer);
            particleComputeShader.SetBuffer(buildSpatialGridKernel, "spatialGridCount", spatialGridCountBuffer);
            particleComputeShader.Dispatch(buildSpatialGridKernel, spatialGroupsX, spatialGroupsY, spatialGroupsZ);
            
            // Find neighbors
            particleComputeShader.SetBuffer(findNeighborsKernel, "particlePositions", particlePositionsBuffer);
            particleComputeShader.SetBuffer(findNeighborsKernel, "spatialGrid", spatialGridBuffer);
            particleComputeShader.SetBuffer(findNeighborsKernel, "spatialGridCount", spatialGridCountBuffer);
            particleComputeShader.SetBuffer(findNeighborsKernel, "particleNeighbors", particleNeighborsBuffer);
            particleComputeShader.Dispatch(findNeighborsKernel, particleGroups, 1, 1);
            
            // Compute density
            if (enableSPHDensity)
            {
                particleComputeShader.SetBuffer(computeDensityKernel, "particlePositions", particlePositionsBuffer);
                particleComputeShader.SetBuffer(computeDensityKernel, "particleNeighbors", particleNeighborsBuffer);
                particleComputeShader.SetBuffer(computeDensityKernel, "particleDensities", particleDensityBuffer);
                particleComputeShader.Dispatch(computeDensityKernel, particleGroups, 1, 1);
            }
            
            // Compute forces
            particleComputeShader.SetBuffer(computeForcesKernel, "particlePositions", particlePositionsBuffer);
            particleComputeShader.SetBuffer(computeForcesKernel, "particleVelocities", particleVelocitiesBuffer);
            particleComputeShader.SetBuffer(computeForcesKernel, "particleDensities", particleDensityBuffer);
            particleComputeShader.SetBuffer(computeForcesKernel, "particleNeighbors", particleNeighborsBuffer);
            particleComputeShader.SetBuffer(computeForcesKernel, "particleForces", particleForcesBuffer);
            particleComputeShader.Dispatch(computeForcesKernel, particleGroups, 1, 1);
            
            // Apply forces
            particleComputeShader.SetBuffer(applyForcesKernel, "particleVelocities", particleVelocitiesBuffer);
            particleComputeShader.SetBuffer(applyForcesKernel, "particleForces", particleForcesBuffer);
            particleComputeShader.Dispatch(applyForcesKernel, particleGroups, 1, 1);
        }

        void ProcessFLIPSimulation(float dt, bool parametersChanged)
        {
            // Find kernels
            int clearKernel = gridComputeShader.FindKernel("ClearGrid");
            int transferKernel = gridComputeShader.FindKernel("TransferToGrid");
            int normalizeKernel = gridComputeShader.FindKernel("NormalizeGrid");
            int advectKernel = gridComputeShader.FindKernel("AdvectVelocity");
            int divergenceKernel = gridComputeShader.FindKernel("ComputeDivergence");
            int jacobiKernel = gridComputeShader.FindKernel("JacobiPressure");
            int subtractKernel = gridComputeShader.FindKernel("SubtractPressureGradient");
            int gridToParticlesKernel = gridComputeShader.FindKernel("GridToParticles");

            if (clearKernel == -1)
            {
                Debug.LogError("FLIP kernels not found!");
                return;
            }

            // Calculate dispatch sizes
            int gridGroupsX = Mathf.Max(1, Mathf.CeilToInt(gridCellsX / 8f));
            int gridGroupsY = Mathf.Max(1, Mathf.CeilToInt(gridCellsY / 8f));
            int gridGroupsZ = Mathf.Max(1, Mathf.CeilToInt(gridCellsZ / 4f));
            int particleGroups = Mathf.CeilToInt(particleCount / 256f);

            // ***** CRITICAL: SET ALL PARAMETERS EVERY FRAME *****
            // Set parameters for grid compute shader
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

            // Set simulation bounds
            gridComputeShader.SetFloat("simulationWidth", simulationWidth);
            gridComputeShader.SetFloat("simulationHeight", simulationHeight);
            gridComputeShader.SetFloat("gridHeight", gridHeight);

            // Clear grid
            gridComputeShader.SetBuffer(clearKernel, "gridVelocityInt", gridVelocityIntBuffer);
            gridComputeShader.SetBuffer(clearKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.SetBuffer(clearKernel, "gridWeight", gridWeight);
            gridComputeShader.SetBuffer(clearKernel, "divergence", divergence);
            gridComputeShader.SetBuffer(clearKernel, "pressure", pressure);
            gridComputeShader.SetBuffer(clearKernel, "pressureTemp", pressureTemp);
            gridComputeShader.Dispatch(clearKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // Transfer particles to grid
            gridComputeShader.SetBuffer(transferKernel, "particlePositions", particlePositionsBuffer);
            gridComputeShader.SetBuffer(transferKernel, "particleVelocities", particleVelocitiesBuffer);
            gridComputeShader.SetBuffer(transferKernel, "gridVelocityInt", gridVelocityIntBuffer);
            gridComputeShader.SetBuffer(transferKernel, "gridWeight", gridWeight);
            gridComputeShader.Dispatch(transferKernel, particleGroups, 1, 1);
            

            // Normalize grid
            gridComputeShader.SetBuffer(normalizeKernel, "gridVelocityInt", gridVelocityIntBuffer);
            gridComputeShader.SetBuffer(normalizeKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.SetBuffer(normalizeKernel, "gridWeight", gridWeight);
            gridComputeShader.Dispatch(normalizeKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // Advect velocity
            gridComputeShader.SetBuffer(advectKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.Dispatch(advectKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // Compute divergence
            gridComputeShader.SetBuffer(divergenceKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.SetBuffer(divergenceKernel, "divergence", divergence);
            gridComputeShader.Dispatch(divergenceKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // Jacobi pressure solver
            for (int i = 0; i < pressureIterations; i++)
            {
                gridComputeShader.SetBuffer(jacobiKernel, "divergence", divergence);
                gridComputeShader.SetBuffer(jacobiKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
                gridComputeShader.SetBuffer(jacobiKernel, "pressure", pressure);
                gridComputeShader.SetBuffer(jacobiKernel, "pressureTemp", pressureTemp);
                gridComputeShader.Dispatch(jacobiKernel, gridGroupsX, gridGroupsY, gridGroupsZ);
                
                // Swap buffers
                ComputeBuffer temp = pressure;
                pressure = pressureTemp;
                pressureTemp = temp;
            }

            // Subtract pressure gradient
            gridComputeShader.SetBuffer(subtractKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.SetBuffer(subtractKernel, "pressure", pressure);
            gridComputeShader.Dispatch(subtractKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // Grid to particles - THIS IS WHERE BOUNDARIES ARE ENFORCED
            gridComputeShader.SetBuffer(gridToParticlesKernel, "particlePositions", particlePositionsBuffer);
            gridComputeShader.SetBuffer(gridToParticlesKernel, "particleVelocities", particleVelocitiesBuffer);
            gridComputeShader.SetBuffer(gridToParticlesKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.Dispatch(gridToParticlesKernel, particleGroups, 1, 1);

            // Read back data
            particlePositionsBuffer.GetData(particlePositions);
            particleVelocitiesBuffer.GetData(particleVelocities);
            if (enableSPHDensity)
            {
                particleDensityBuffer.GetData(particleDensities);
            }
        }

        void RenderParticles()
        {
            if (particleMaterial != null && particleMesh != null)
            {
                Bounds bounds = new Bounds(
                    new Vector3(simulationWidth * 0.5f, gridHeight * 0.5f, simulationHeight * 0.5f),
                    new Vector3(simulationWidth, gridHeight, simulationHeight)
                );

                Graphics.DrawMeshInstancedProcedural(
                    particleMesh,
                    0,
                    particleMaterial,
                    bounds,
                    particleCount
                );
            }
        }

        void LogDebugInfo()
        {
            float avgVelocity = 0f;
            float maxVelocity = 0f;
            float particlesOutside = 0f;
            
            for (int i = 0; i < particleCount; i++)
            {
                float vel = particleVelocities[i].magnitude;
                avgVelocity += vel;
                if (vel > maxVelocity) maxVelocity = vel;
                
                // Check if particle is outside bounds
                if (particlePositions[i].x < 0 || particlePositions[i].x > simulationWidth ||
                    particlePositions[i].y < 0 || particlePositions[i].y > gridHeight ||
                    particlePositions[i].z < 0 || particlePositions[i].z > simulationHeight)
                {
                    particlesOutside++;
                }
            }
            avgVelocity /= particleCount;
            
            if (Time.frameCount % 300 == 0)
            {
                Debug.Log($"Frame {Time.frameCount}: Avg Vel = {avgVelocity:F3}, Max Vel = {maxVelocity:F3}");
                if (particlesOutside > 0)
                {
                    Debug.LogWarning($"{particlesOutside} particles are outside bounds!");
                }
            }
        }

        void ReleaseBuffers()
        {
            particlePositionsBuffer?.Release();
            particleVelocitiesBuffer?.Release();
            gridVelocityIntBuffer?.Release();
            gridVelocityFloatBuffer?.Release();
            gridWeight?.Release();
            divergence?.Release();
            pressure?.Release();
            pressureTemp?.Release();
            
            particleDensityBuffer?.Release();
            particleForcesBuffer?.Release();
            spatialGridBuffer?.Release();
            spatialGridCountBuffer?.Release();
            particleNeighborsBuffer?.Release();
        }

        void OnDestroy()
        {
            ReleaseBuffers();
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 400, 600));
            GUILayout.Label("FLIP Fluid Simulation - Debug");
            GUILayout.Label($"Particles: {particleCount}");
            GUILayout.Label($"Gravity: {gravityScale}");
            GUILayout.Label($"FLIP Blend: {flipBlend}");
            GUILayout.Label($"Pressure Iterations: {pressureIterations}");
            
            GUILayout.Space(10);
            GUILayout.Label("=== Particle Interactions ===");
            
            // Store old values to detect changes
            bool oldCollisions = enableParticleCollisions;
            bool oldSPH = enableSPHDensity;
            
            enableParticleCollisions = GUILayout.Toggle(enableParticleCollisions, "Enable Collisions");
            if (enableParticleCollisions)
            {
                float oldRadius = particleCollisionRadius;
                GUILayout.Label($"Collision Radius: {particleCollisionRadius:F3}");
                particleCollisionRadius = GUILayout.HorizontalSlider(particleCollisionRadius, 0.05f, 0.5f);
                if (particleCollisionRadius != oldRadius)
                    Debug.Log($"Collision radius changed to: {particleCollisionRadius}");
                
                float oldStiffness = particleStiffness;
                GUILayout.Label($"Stiffness: {particleStiffness:F3}");
                particleStiffness = GUILayout.HorizontalSlider(particleStiffness, 0.01f, 2.0f);
                if (particleStiffness != oldStiffness)
                    Debug.Log($"Stiffness changed to: {particleStiffness}");
                
                float oldDamping = particleDamping;
                GUILayout.Label($"Damping: {particleDamping:F3}");
                particleDamping = GUILayout.HorizontalSlider(particleDamping, 0.5f, 1.0f);
                if (particleDamping != oldDamping)
                    Debug.Log($"Damping changed to: {particleDamping}");
            }
            
            enableSPHDensity = GUILayout.Toggle(enableSPHDensity, "Enable SPH Density");
            if (enableSPHDensity)
            {
                float oldSmoothing = smoothingRadius;
                GUILayout.Label($"Smoothing Radius: {smoothingRadius:F3}");
                smoothingRadius = GUILayout.HorizontalSlider(smoothingRadius, 0.1f, 1.0f);
                if (smoothingRadius != oldSmoothing)
                    Debug.Log($"Smoothing radius changed to: {smoothingRadius}");
                
                float oldTarget = targetDensity;
                GUILayout.Label($"Target Density: {targetDensity:F3}");
                targetDensity = GUILayout.HorizontalSlider(targetDensity, 0.5f, 10.0f);
                if (targetDensity != oldTarget)
                    Debug.Log($"Target density changed to: {targetDensity}");
                
                float oldPressure = pressureMultiplier;
                GUILayout.Label($"Pressure Multiplier: {pressureMultiplier:F3}");
                pressureMultiplier = GUILayout.HorizontalSlider(pressureMultiplier, 0.1f, 5.0f);
                if (pressureMultiplier != oldPressure)
                    Debug.Log($"Pressure multiplier changed to: {pressureMultiplier}");
                
                float oldViscosity = viscosityStrength;
                GUILayout.Label($"Viscosity: {viscosityStrength:F3}");
                viscosityStrength = GUILayout.HorizontalSlider(viscosityStrength, 0.01f, 1.0f);
                if (viscosityStrength != oldViscosity)
                    Debug.Log($"Viscosity changed to: {viscosityStrength}");
            }
            
            GUILayout.Space(10);
            GUILayout.Label("=== FLIP Parameters ===");
            
            float oldGravity = gravityScale;
            GUILayout.Label($"Gravity Scale: {gravityScale:F1}");
            gravityScale = GUILayout.HorizontalSlider(gravityScale, 0.0f, 50.0f);
            if (gravityScale != oldGravity)
                Debug.Log($"Gravity scale changed to: {gravityScale}");
            
            float oldFlip = flipBlend;
            GUILayout.Label($"FLIP Blend: {flipBlend:F3}");
            flipBlend = GUILayout.HorizontalSlider(flipBlend, 0.0f, 1.0f);
            if (flipBlend != oldFlip)
                Debug.Log($"FLIP blend changed to: {flipBlend}");
            
            int oldIterations = pressureIterations;
            GUILayout.Label($"Pressure Iterations: {pressureIterations}");
            pressureIterations = (int)GUILayout.HorizontalSlider(pressureIterations, 1, 100);
            if (pressureIterations != oldIterations)
                Debug.Log($"Pressure iterations changed to: {pressureIterations}");
            
            GUILayout.Space(10);
            if (GUILayout.Button("Reset Simulation"))
            {
                InitializeSimulation();
                Debug.Log("Simulation reset!");
            }
            
            if (GUILayout.Button("Test Parameters"))
            {
                Debug.Log("=== Current Parameters ===");
                Debug.Log($"Gravity: {gravityScale}");
                Debug.Log($"Collisions: {enableParticleCollisions}, Radius: {particleCollisionRadius}");
                Debug.Log($"SPH: {enableSPHDensity}, Smoothing: {smoothingRadius}");
                Debug.Log($"FLIP Blend: {flipBlend}, Pressure Iterations: {pressureIterations}");
            }
            
            GUILayout.EndArea();
        }
    }
}