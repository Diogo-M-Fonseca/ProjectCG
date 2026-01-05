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

        // FLIP Simulation Buffers
        private ComputeBuffer particlePositionsBuffer;
        private ComputeBuffer particleVelocitiesBuffer;
        private ComputeBuffer gridVelocityIntBuffer;
        private ComputeBuffer gridVelocityFloatBuffer;
        private ComputeBuffer gridWeight;
        private ComputeBuffer divergence;
        private ComputeBuffer pressure;
        private ComputeBuffer pressureTemp;
        
        // Particle Interaction Buffers
        private ComputeBuffer particleDensityBuffer;
        private ComputeBuffer particleForcesBuffer;
        private ComputeBuffer spatialGridBuffer;
        private ComputeBuffer spatialGridCountBuffer;
        private ComputeBuffer particleNeighborsBuffer;

        // Arrays
        private Vector3[] particlePositions;
        private Vector3[] particleVelocities;
        private float[] particleDensities;

        // Mesh for rendering
        private Mesh particleMesh;

        // Grid dimensions
        private int gridCellsX, gridCellsY, gridCellsZ;
        private float cellSize = 1.0f;
        private bool isInitialized = false;
        
        // Spatial partitioning for particle collisions
        private int spatialGridSizeX, spatialGridSizeY, spatialGridSizeZ;
        private int spatialGridTotalCells;
        private const int MAX_PARTICLES_PER_CELL = 16; // Reduced from 32 to save memory

        void Start()
        {
            InitializeSimulation();
        }

        void InitializeSimulation()
        {
            Debug.Log("Initializing FLIP fluid simulation with particle-particle collisions...");
            
            // Create octahedron mesh using your FluidGenerator
            particleMesh = FluidGenerator.MeshGenerator(meshResolution);
            
            // Main simulation grid
            gridCellsX = Mathf.Max(1, Mathf.CeilToInt(simulationWidth / cellSize));
            gridCellsY = Mathf.Max(1, Mathf.CeilToInt(gridHeight / cellSize));
            gridCellsZ = Mathf.Max(1, Mathf.CeilToInt(simulationHeight / cellSize));
            int totalCells = gridCellsX * gridCellsY * gridCellsZ;

            // OPTIMIZED: Use smoothingRadius for spatial grid cell size (better for SPH)
            float spatialCellSize = smoothingRadius * 0.5f; // Half smoothing radius for better neighbor search
            spatialGridSizeX = Mathf.Max(1, Mathf.CeilToInt(simulationWidth / spatialCellSize));
            spatialGridSizeY = Mathf.Max(1, Mathf.CeilToInt(gridHeight / spatialCellSize));
            spatialGridSizeZ = Mathf.Max(1, Mathf.CeilToInt(simulationHeight / spatialCellSize));
            spatialGridTotalCells = spatialGridSizeX * spatialGridSizeY * spatialGridSizeZ;

            Debug.Log($"Grid dimensions: {gridCellsX}x{gridCellsY}x{gridCellsZ} = {totalCells} cells");
            Debug.Log($"Spatial grid: {spatialGridSizeX}x{spatialGridSizeY}x{spatialGridSizeZ} = {spatialGridTotalCells} cells");

            // Validate parameters
            if (particleCount <= 0 || gridCellsX <= 0 || gridCellsY <= 0 || gridCellsZ <= 0)
            {
                Debug.LogError("Invalid simulation parameters!");
                return;
            }

            particlePositions = new Vector3[particleCount];
            particleVelocities = new Vector3[particleCount];
            particleDensities = new float[particleCount];

            InitializeParticles();

            // Create all compute buffers
            CreateComputeBuffers(totalCells);

            particlePositionsBuffer.SetData(particlePositions);
            particleVelocitiesBuffer.SetData(particleVelocities);

            if (particleMaterial != null)
            {
                particleMaterial.SetBuffer("_ParticlePositions", particlePositionsBuffer);
                particleMaterial.SetBuffer("_ParticleVelocities", particleVelocitiesBuffer);
                particleMaterial.SetBuffer("_ParticleDensities", particleDensityBuffer);
                particleMaterial.SetFloat("_ParticleSize", particleSize);
            }

            isInitialized = true;
            Debug.Log($"FLIP fluid simulation initialized with {particleCount} particles");
            Debug.Log($"Octahedron mesh resolution: {meshResolution}");
        }

        void CreateComputeBuffers(int totalCells)
        {
            // FLIP Simulation buffers
            particlePositionsBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            particleVelocitiesBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            gridVelocityIntBuffer = new ComputeBuffer(totalCells, sizeof(int) * 3);
            gridVelocityFloatBuffer = new ComputeBuffer(totalCells, sizeof(float) * 3);
            gridWeight = new ComputeBuffer(totalCells, sizeof(uint));
            divergence = new ComputeBuffer(totalCells, sizeof(float));
            pressure = new ComputeBuffer(totalCells, sizeof(float));
            pressureTemp = new ComputeBuffer(totalCells, sizeof(float));
            
            // Particle interaction buffers
            particleDensityBuffer = new ComputeBuffer(particleCount, sizeof(float));
            particleForcesBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            spatialGridBuffer = new ComputeBuffer(spatialGridTotalCells * MAX_PARTICLES_PER_CELL, sizeof(int));
            spatialGridCountBuffer = new ComputeBuffer(spatialGridTotalCells, sizeof(int));
            particleNeighborsBuffer = new ComputeBuffer(particleCount * 64, sizeof(int));

            // Initialize buffers to zero
            InitializeBuffersToZero(totalCells);
        }

        void InitializeBuffersToZero(int totalCells)
        {
            // FLIP buffers
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
            
            // Particle interaction buffers
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
            // Create a dense blob at the top
            Vector3 center = new Vector3(
                simulationWidth * 0.5f,
                gridHeight * 0.8f,
                simulationHeight * 0.5f
            );
            
            // Calculate spacing based on particle collision radius
            int particlesPerDimension = Mathf.CeilToInt(Mathf.Pow(particleCount, 1f/3f));
            float spacing = particleCollisionRadius * 0.8f; // Start with particles slightly compressed
            
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
            
            // Fill remaining particles if not enough from grid
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
            
            Debug.Log($"Initialized {index} particles in a dense blob");
        }

        void Update()
        {
            if (!isInitialized || gridComputeShader == null || particleComputeShader == null) return;

            // Clamp dt for numerical stability
            float dt = Mathf.Min(0.016f, Time.deltaTime);
            dt = Mathf.Clamp(dt, 0.001f, 0.033f); // 30-1000 FPS range
            
            // PARTICLE INTERACTIONS (SPH & Collisions)
            if (enableParticleCollisions || enableSPHDensity)
            {
                ProcessParticleInteractions(dt);
            }
            
            // FLIP SIMULATION
            ProcessFLIPSimulation(dt);
            
            // RENDER PARTICLES
            RenderParticles();
            
            // DEBUG INFO
            if (Time.frameCount % 60 == 0)
            {
                LogDebugInfo();
            }
        }

        void ProcessParticleInteractions(float dt)
        {
            // Find particle interaction kernels
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

            // Set particle compute shader parameters
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

            // Convert bool to int for HLSL
            int enableCollisionsInt = enableParticleCollisions ? 1 : 0;
            int enableSPHInt = enableSPHDensity ? 1 : 0;

            particleComputeShader.SetInt("enableCollisions", enableCollisionsInt);
            particleComputeShader.SetInt("enableSPH", enableSPHInt);

            // STEP 1: Build spatial grid
            particleComputeShader.SetBuffer(buildSpatialGridKernel, "particlePositions", particlePositionsBuffer);
            particleComputeShader.SetBuffer(buildSpatialGridKernel, "spatialGrid", spatialGridBuffer);
            particleComputeShader.SetBuffer(buildSpatialGridKernel, "spatialGridCount", spatialGridCountBuffer);
            particleComputeShader.Dispatch(buildSpatialGridKernel, spatialGroupsX, spatialGroupsY, spatialGroupsZ);
            
            // STEP 2: Find neighbors
            particleComputeShader.SetBuffer(findNeighborsKernel, "particlePositions", particlePositionsBuffer);
            particleComputeShader.SetBuffer(findNeighborsKernel, "spatialGrid", spatialGridBuffer);
            particleComputeShader.SetBuffer(findNeighborsKernel, "spatialGridCount", spatialGridCountBuffer);
            particleComputeShader.SetBuffer(findNeighborsKernel, "particleNeighbors", particleNeighborsBuffer);
            particleComputeShader.Dispatch(findNeighborsKernel, particleGroups, 1, 1);
            
            // STEP 3: Compute density
            if (enableSPHDensity)
            {
                particleComputeShader.SetBuffer(computeDensityKernel, "particlePositions", particlePositionsBuffer);
                particleComputeShader.SetBuffer(computeDensityKernel, "particleNeighbors", particleNeighborsBuffer);
                particleComputeShader.SetBuffer(computeDensityKernel, "particleDensities", particleDensityBuffer);
                particleComputeShader.Dispatch(computeDensityKernel, particleGroups, 1, 1);
            }
            
            // STEP 4: Compute forces
            particleComputeShader.SetBuffer(computeForcesKernel, "particlePositions", particlePositionsBuffer);
            particleComputeShader.SetBuffer(computeForcesKernel, "particleVelocities", particleVelocitiesBuffer);
            particleComputeShader.SetBuffer(computeForcesKernel, "particleDensities", particleDensityBuffer);
            particleComputeShader.SetBuffer(computeForcesKernel, "particleNeighbors", particleNeighborsBuffer);
            particleComputeShader.SetBuffer(computeForcesKernel, "particleForces", particleForcesBuffer);
            particleComputeShader.Dispatch(computeForcesKernel, particleGroups, 1, 1);
            
            // STEP 5: Apply forces
            particleComputeShader.SetBuffer(applyForcesKernel, "particleVelocities", particleVelocitiesBuffer);
            particleComputeShader.SetBuffer(applyForcesKernel, "particleForces", particleForcesBuffer);
            particleComputeShader.Dispatch(applyForcesKernel, particleGroups, 1, 1);
        }

        void ProcessFLIPSimulation(float dt)
        {
            // Find FLIP simulation kernels
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
                Debug.LogError("FLIP simulation kernels not found!");
                return;
            }

            // Calculate dispatch sizes
            int gridGroupsX = Mathf.Max(1, Mathf.CeilToInt(gridCellsX / 8f));
            int gridGroupsY = Mathf.Max(1, Mathf.CeilToInt(gridCellsY / 8f));
            int gridGroupsZ = Mathf.Max(1, Mathf.CeilToInt(gridCellsZ / 4f));
            int particleGroups = Mathf.CeilToInt(particleCount / 256f);

            // Set FLIP compute shader parameters
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

            // 1. Clear grid
            gridComputeShader.SetBuffer(clearKernel, "gridVelocityInt", gridVelocityIntBuffer);
            gridComputeShader.SetBuffer(clearKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.SetBuffer(clearKernel, "gridWeight", gridWeight);
            gridComputeShader.SetBuffer(clearKernel, "divergence", divergence);
            gridComputeShader.SetBuffer(clearKernel, "pressure", pressure);
            gridComputeShader.SetBuffer(clearKernel, "pressureTemp", pressureTemp);
            gridComputeShader.Dispatch(clearKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // 2. Transfer particles to grid
            gridComputeShader.SetBuffer(transferKernel, "particlePositions", particlePositionsBuffer);
            gridComputeShader.SetBuffer(transferKernel, "particleVelocities", particleVelocitiesBuffer);
            gridComputeShader.SetBuffer(transferKernel, "gridVelocityInt", gridVelocityIntBuffer);
            gridComputeShader.SetBuffer(transferKernel, "gridWeight", gridWeight);
            gridComputeShader.Dispatch(transferKernel, particleGroups, 1, 1);

            // 3. Normalize grid
            gridComputeShader.SetBuffer(normalizeKernel, "gridVelocityInt", gridVelocityIntBuffer);
            gridComputeShader.SetBuffer(normalizeKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.SetBuffer(normalizeKernel, "gridWeight", gridWeight);
            gridComputeShader.Dispatch(normalizeKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // 4. Advect velocity
            gridComputeShader.SetBuffer(advectKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.Dispatch(advectKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // 5. Compute divergence
            gridComputeShader.SetBuffer(divergenceKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.SetBuffer(divergenceKernel, "divergence", divergence);
            gridComputeShader.Dispatch(divergenceKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // 6. Jacobi pressure solver
            for (int i = 0; i < pressureIterations; i++)
            {
                gridComputeShader.SetBuffer(jacobiKernel, "divergence", divergence);
                gridComputeShader.SetBuffer(jacobiKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
                gridComputeShader.SetBuffer(jacobiKernel, "pressure", pressure);
                gridComputeShader.SetBuffer(jacobiKernel, "pressureTemp", pressureTemp);
                gridComputeShader.Dispatch(jacobiKernel, gridGroupsX, gridGroupsY, gridGroupsZ);
                
                // Swap pressure buffers
                ComputeBuffer temp = pressure;
                pressure = pressureTemp;
                pressureTemp = temp;
            }

            // 7. Subtract pressure gradient
            gridComputeShader.SetBuffer(subtractKernel, "gridVelocityFloat", gridVelocityFloatBuffer);
            gridComputeShader.SetBuffer(subtractKernel, "pressure", pressure);
            gridComputeShader.Dispatch(subtractKernel, gridGroupsX, gridGroupsY, gridGroupsZ);

            // 8. Grid to particles
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
            float avgDensity = 0f;
            for (int i = 0; i < particleCount; i++)
            {
                float vel = particleVelocities[i].magnitude;
                avgVelocity += vel;
                avgDensity += particleDensities[i];
                if (vel > maxVelocity) maxVelocity = vel;
            }
            avgVelocity /= particleCount;
            avgDensity /= particleCount;
            
            // Optional: Log additional info every 60 frames
            if (Time.frameCount % 300 == 0) // Every 5 seconds at 60 FPS
            {
                Debug.Log($"Frame {Time.frameCount}: Avg Vel = {avgVelocity:F3}, Max Vel = {maxVelocity:F3}, Avg Density = {avgDensity:F3}");
            }
        }

        void OnDestroy()
        {
            // Release all buffers
            particlePositionsBuffer?.Release();
            particleVelocitiesBuffer?.Release();
            gridVelocityIntBuffer?.Release();
            gridVelocityFloatBuffer?.Release();
            gridWeight?.Release();
            divergence?.Release();
            pressure?.Release();
            pressureTemp?.Release();
            
            // Particle interaction buffers
            particleDensityBuffer?.Release();
            particleForcesBuffer?.Release();
            spatialGridBuffer?.Release();
            spatialGridCountBuffer?.Release();
            particleNeighborsBuffer?.Release();
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 400, 600));
            GUILayout.Label("FLIP Fluid Simulation - Enhanced");
            GUILayout.Label($"Particles: {particleCount}");
            GUILayout.Label($"Grid: {gridCellsX}x{gridCellsY}x{gridCellsZ}");
            GUILayout.Label($"Gravity: {gravityScale}");
            GUILayout.Label($"FLIP Blend: {flipBlend}");
            GUILayout.Label($"Mesh Resolution: {meshResolution}");
            
            GUILayout.Space(10);
            GUILayout.Label("=== Particle Interactions ===");
            
            enableParticleCollisions = GUILayout.Toggle(enableParticleCollisions, "Enable Particle Collisions");
            if (enableParticleCollisions)
            {
                GUILayout.Label("Collision Radius:");
                particleCollisionRadius = GUILayout.HorizontalSlider(particleCollisionRadius, 0.05f, 0.3f);
                GUILayout.Label("Stiffness:");
                particleStiffness = GUILayout.HorizontalSlider(particleStiffness, 0.01f, 1.0f);
                GUILayout.Label("Damping:");
                particleDamping = GUILayout.HorizontalSlider(particleDamping, 0.8f, 1.0f);
            }
            
            enableSPHDensity = GUILayout.Toggle(enableSPHDensity, "Enable SPH Density");
            if (enableSPHDensity)
            {
                GUILayout.Label("Smoothing Radius:");
                smoothingRadius = GUILayout.HorizontalSlider(smoothingRadius, 0.1f, 0.5f);
                GUILayout.Label("Target Density:");
                targetDensity = GUILayout.HorizontalSlider(targetDensity, 0.5f, 5.0f);
                GUILayout.Label("Pressure Multiplier:");
                pressureMultiplier = GUILayout.HorizontalSlider(pressureMultiplier, 0.1f, 3.0f);
                GUILayout.Label("Viscosity:");
                viscosityStrength = GUILayout.HorizontalSlider(viscosityStrength, 0.01f, 0.5f);
            }
            
            GUILayout.Space(10);
            if (GUILayout.Button("Reset Simulation"))
            {
                InitializeParticles();
                particlePositionsBuffer.SetData(particlePositions);
                particleVelocitiesBuffer.SetData(particleVelocities);
            }
            
            GUILayout.EndArea();
        }
    }
}