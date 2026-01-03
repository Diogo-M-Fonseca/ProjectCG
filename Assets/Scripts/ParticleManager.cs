using UnityEngine;
using System.Collections.Generic;

namespace CGProject
{
    public class ParticleManager : MonoBehaviour
    {
        [Header("Grid Settings")]
        [SerializeField] private int simulationWidth = 8;
        [SerializeField] private int simulationHeight = 8;
        [SerializeField] private int gridHeight = 16;
        [SerializeField, Range(0, 1)] float flipBlend = 0.95f; // High FLIP blend

        [Header("Particle Settings")]
        [SerializeField] private int particleCount = 500; // REDUCED for testing
        [SerializeField] private float particleSize = 0.1f;
        [SerializeField] private Material particleMaterial;
        [SerializeField] private int meshResolution = 1;
        [SerializeField] private float particleMass = 1.0f;

        [Header("SPH Settings")]
        [SerializeField] private float pressureMultiplier = 1000.0f; // DRAMATICALLY INCREASED
        [SerializeField] private float targetDensity = 8.0f; // Increased
        [SerializeField] private float smoothingRadius = 1.0f; // Larger
        [SerializeField] private float viscosityStrength = 20.0f; // Increased

        [Header("Movement Settings")]
        [SerializeField] private float damping = 0.98f;
        [SerializeField] private float maxSpeed = 10.0f;
        [SerializeField] private float particleGravityScale = 5.0f; // INCREASED

        [Header("Collision Settings")]
        [SerializeField] private float wallBounce = 0.3f;
        [SerializeField] private float wallFriction = 0.5f;
        [SerializeField] private float wallMargin = 0.2f;

        [Header("Repulsion Settings")]
        [SerializeField] private bool useArtificialRepulsion = true;
        [SerializeField] private float repulsionStrength = 200.0f; // Much stronger
        [SerializeField] private float repulsionRadius = 0.3f;

        [Header("Compute Shaders")]
        [SerializeField] private ComputeShader sphComputeShader;
        [SerializeField] private ComputeShader gridComputeShader;

        // Compute Buffers
        private ComputeBuffer particlePositionsBuffer;
        private ComputeBuffer particleVelocitiesBuffer;
        private ComputeBuffer densitiesBuffer;
        private ComputeBuffer pressureForcesBuffer;

        // Spatial grid buffers
        private ComputeBuffer spatialGridCellsBuffer;
        private ComputeBuffer spatialGridOffsetsBuffer;
        private ComputeBuffer spatialGridCountsBuffer;
        private ComputeBuffer spatialGridIndicesBuffer;

        // CPU arrays
        private Vector3[] particlePositions;
        private Vector3[] particleVelocities;
        private float[] densities;
        private Vector3[] pressureForces;

        // Rendering
        private Mesh particleMesh;

        // Grid dimensions
        private int gridCellsX, gridCellsY, gridCellsZ;
        private float spatialCellSize;
        private int totalCells;
        private int maxParticlesPerCell = 64;

        // Tracking
        private bool isInitialized = false;

        void Start()
        {
            InitializeSimulation();
        }

        void InitializeSimulation()
        {
            Debug.Log("Initializing Hybrid SPH-FLIP Simulation...");
            
            // Create particle mesh
            particleMesh = FluidGenerator.MeshGenerator(meshResolution);

            // Initialize arrays
            particlePositions = new Vector3[particleCount];
            particleVelocities = new Vector3[particleCount];
            densities = new float[particleCount];
            pressureForces = new Vector3[particleCount];

            // Initialize particles
            InitializeParticles();
            
            // Initialize compute buffers
            InitializeComputeBuffers();
            
            // Initialize spatial grid
            InitializeSpatialGrid();

            // Setup material
            if (particleMaterial != null)
            {
                particleMaterial.SetBuffer("_ParticlePositions", particlePositionsBuffer);
                particleMaterial.SetBuffer("_ParticleVelocities", particleVelocitiesBuffer);
                particleMaterial.SetFloat("_ParticleSize", particleSize);
            }
            
            Debug.Log($"Hybrid Simulation Initialized: {particleCount} particles");
            isInitialized = true;
        }

        void InitializeComputeBuffers()
        {
            // Particle buffers
            particlePositionsBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            particleVelocitiesBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
            densitiesBuffer = new ComputeBuffer(particleCount, sizeof(float));
            pressureForcesBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);

            // Set initial data
            particlePositionsBuffer.SetData(particlePositions);
            particleVelocitiesBuffer.SetData(particleVelocities);
            densitiesBuffer.SetData(densities);
            pressureForcesBuffer.SetData(pressureForces);
        }

        void InitializeSpatialGrid()
        {
            spatialCellSize = smoothingRadius * 1.2f;
            gridCellsX = Mathf.Max(1, Mathf.CeilToInt(simulationWidth / spatialCellSize));
            gridCellsY = Mathf.Max(1, Mathf.CeilToInt(gridHeight / spatialCellSize)); // Z is up in your setup
            gridCellsZ = Mathf.Max(1, Mathf.CeilToInt(simulationHeight / spatialCellSize));
            totalCells = gridCellsX * gridCellsY * gridCellsZ;

            spatialGridCellsBuffer = new ComputeBuffer(particleCount, sizeof(int));
            spatialGridOffsetsBuffer = new ComputeBuffer(totalCells, sizeof(int));
            spatialGridCountsBuffer = new ComputeBuffer(totalCells, sizeof(int));
            spatialGridIndicesBuffer = new ComputeBuffer(particleCount * maxParticlesPerCell, sizeof(int));

            // Initialize offsets
            int[] offsets = new int[totalCells];
            for (int i = 0; i < totalCells; i++)
            {
                offsets[i] = i * maxParticlesPerCell;
            }
            spatialGridOffsetsBuffer.SetData(offsets);
        }

        void InitializeParticles()
        {
            float margin = wallMargin + 0.5f;
            
            // Create a dense block of particles at the top
            Vector3 spawnCenter = new Vector3(
                simulationWidth * 0.5f,
                gridHeight * 0.8f,  // Top 80%
                simulationHeight * 0.5f
            );
            
            // Create a dense block (not sphere)
            int particlesPerSide = Mathf.CeilToInt(Mathf.Pow(particleCount, 1f/3f));
            float spacing = 0.15f;
            
            int count = 0;
            for (int x = 0; x < particlesPerSide && count < particleCount; x++)
            {
                for (int y = 0; y < particlesPerSide && count < particleCount; y++)
                {
                    for (int z = 0; z < particlesPerSide && count < particleCount; z++)
                    {
                        Vector3 pos = new Vector3(
                            spawnCenter.x + (x - particlesPerSide/2f) * spacing,
                            spawnCenter.y + (y - particlesPerSide/2f) * spacing,
                            spawnCenter.z + (z - particlesPerSide/2f) * spacing
                        );
                        
                        // Clamp to ensure particles are inside bounds
                        pos.x = Mathf.Clamp(pos.x, margin, simulationWidth - margin);
                        pos.y = Mathf.Clamp(pos.y, margin, gridHeight - margin);
                        pos.z = Mathf.Clamp(pos.z, margin, simulationHeight - margin);
                        
                        particlePositions[count] = pos;
                        particleVelocities[count] = Vector3.zero;
                        count++;
                    }
                }
            }
            
            Debug.Log($"Initialized {count} particles in a dense block");
        }

        void Update()
        {
            if (!isInitialized) return;

            // Fixed time step for stability
            float dt = 0.016f; // Fixed 60 FPS

            if (sphComputeShader != null)
            {
                // Run SPH simulation on GPU
                RunSPHSimulation(dt);
            }
            else
            {
                Debug.LogError("SPH Compute Shader is not assigned!");
                return;
            }

            // Read data back from GPU
            particlePositionsBuffer.GetData(particlePositions);
            particleVelocitiesBuffer.GetData(particleVelocities);
            densitiesBuffer.GetData(densities);

            // Debug: Check if particles are moving
            float totalVelocity = 0f;
            for (int i = 0; i < particleCount; i++)
            {
                totalVelocity += particleVelocities[i].magnitude;
            }
            
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"Frame {Time.frameCount}: Avg Velocity = {totalVelocity/particleCount:F4}, Gravity Scale = {particleGravityScale}");
                
                // Check density
                float avgDensity = 0f;
                for (int i = 0; i < particleCount; i++) avgDensity += densities[i];
                avgDensity /= particleCount;
                Debug.Log($"Average Density: {avgDensity:F2}, Target: {targetDensity}");
            }

            // Render
            Graphics.DrawMeshInstancedProcedural(
                particleMesh,
                0,
                particleMaterial,
                new Bounds(new Vector3(simulationWidth * 0.5f, gridHeight * 0.5f, simulationHeight * 0.5f),
                          new Vector3(simulationWidth, gridHeight, simulationHeight)),
                particleCount);
        }

        void RunSPHSimulation(float dt)
        {
            // Reset spatial grid counts to zero
            int[] zeroCounts = new int[totalCells];
            spatialGridCountsBuffer.SetData(zeroCounts);

            // Set compute shader parameters - CRITICAL: Use correct parameter names
            sphComputeShader.SetFloat("particleMass", particleMass);
            sphComputeShader.SetFloat("smoothingRadius", smoothingRadius);
            sphComputeShader.SetFloat("pressureMultiplier", pressureMultiplier);
            sphComputeShader.SetFloat("targetDensity", targetDensity);
            sphComputeShader.SetFloat("viscosityStrength", viscosityStrength);
            sphComputeShader.SetFloat("gravityScale", particleGravityScale); // This is key!
            sphComputeShader.SetFloat("dt", dt);
            sphComputeShader.SetFloat("damping", damping);
            sphComputeShader.SetFloat("maxSpeed", maxSpeed);
            sphComputeShader.SetInt("particleCount", particleCount);
            sphComputeShader.SetFloat("cellSize", spatialCellSize);
            sphComputeShader.SetInt("gridCellsX", gridCellsX);
            sphComputeShader.SetInt("gridCellsY", gridCellsY);
            sphComputeShader.SetInt("gridCellsZ", gridCellsZ);
            sphComputeShader.SetVector("gridBounds", new Vector3(simulationWidth, gridHeight, simulationHeight));
            sphComputeShader.SetFloat("wallMargin", wallMargin);
            sphComputeShader.SetFloat("wallBounce", wallBounce);
            sphComputeShader.SetFloat("wallFriction", wallFriction);
            sphComputeShader.SetBool("useArtificialRepulsion", useArtificialRepulsion);
            sphComputeShader.SetFloat("repulsionStrength", repulsionStrength);
            sphComputeShader.SetFloat("repulsionRadius", repulsionRadius);

            // Find kernels
            int buildGridKernel = sphComputeShader.FindKernel("BuildSpatialGrid");
            int densitiesKernel = sphComputeShader.FindKernel("CalculateDensities");
            int forcesKernel = sphComputeShader.FindKernel("CalculatePressureForces");
            int updateKernel = sphComputeShader.FindKernel("UpdateParticles");

            int buildThreadGroups = Mathf.CeilToInt(particleCount / 256.0f);

            // Build spatial grid
            sphComputeShader.SetBuffer(buildGridKernel, "particlePositions", particlePositionsBuffer);
            sphComputeShader.SetBuffer(buildGridKernel, "spatialGridCells", spatialGridCellsBuffer);
            sphComputeShader.SetBuffer(buildGridKernel, "spatialGridOffsets", spatialGridOffsetsBuffer);
            sphComputeShader.SetBuffer(buildGridKernel, "spatialGridCounts", spatialGridCountsBuffer);
            sphComputeShader.SetBuffer(buildGridKernel, "spatialGridIndices", spatialGridIndicesBuffer);
            sphComputeShader.Dispatch(buildGridKernel, buildThreadGroups, 1, 1);

            // Calculate densities
            sphComputeShader.SetBuffer(densitiesKernel, "particlePositions", particlePositionsBuffer);
            sphComputeShader.SetBuffer(densitiesKernel, "densities", densitiesBuffer);
            sphComputeShader.SetBuffer(densitiesKernel, "spatialGridCells", spatialGridCellsBuffer);
            sphComputeShader.SetBuffer(densitiesKernel, "spatialGridOffsets", spatialGridOffsetsBuffer);
            sphComputeShader.SetBuffer(densitiesKernel, "spatialGridCounts", spatialGridCountsBuffer);
            sphComputeShader.SetBuffer(densitiesKernel, "spatialGridIndices", spatialGridIndicesBuffer);
            sphComputeShader.Dispatch(densitiesKernel, buildThreadGroups, 1, 1);

            // Calculate pressure forces
            sphComputeShader.SetBuffer(forcesKernel, "particlePositions", particlePositionsBuffer);
            sphComputeShader.SetBuffer(forcesKernel, "particleVelocities", particleVelocitiesBuffer);
            sphComputeShader.SetBuffer(forcesKernel, "densities", densitiesBuffer);
            sphComputeShader.SetBuffer(forcesKernel, "pressureForces", pressureForcesBuffer);
            sphComputeShader.SetBuffer(forcesKernel, "spatialGridCells", spatialGridCellsBuffer);
            sphComputeShader.SetBuffer(forcesKernel, "spatialGridOffsets", spatialGridOffsetsBuffer);
            sphComputeShader.SetBuffer(forcesKernel, "spatialGridCounts", spatialGridCountsBuffer);
            sphComputeShader.SetBuffer(forcesKernel, "spatialGridIndices", spatialGridIndicesBuffer);
            sphComputeShader.Dispatch(forcesKernel, buildThreadGroups, 1, 1);

            // Update particles
            sphComputeShader.SetBuffer(updateKernel, "particlePositions", particlePositionsBuffer);
            sphComputeShader.SetBuffer(updateKernel, "particleVelocities", particleVelocitiesBuffer);
            sphComputeShader.SetBuffer(updateKernel, "pressureForces", pressureForcesBuffer);
            sphComputeShader.SetBuffer(updateKernel, "spatialGridCells", spatialGridCellsBuffer);
            sphComputeShader.SetBuffer(updateKernel, "spatialGridOffsets", spatialGridOffsetsBuffer);
            sphComputeShader.SetBuffer(updateKernel, "spatialGridCounts", spatialGridCountsBuffer);
            sphComputeShader.SetBuffer(updateKernel, "spatialGridIndices", spatialGridIndicesBuffer);
            sphComputeShader.Dispatch(updateKernel, buildThreadGroups, 1, 1);
        }

        void OnDestroy()
        {
            // Release all compute buffers
            particlePositionsBuffer?.Release();
            particleVelocitiesBuffer?.Release();
            densitiesBuffer?.Release();
            pressureForcesBuffer?.Release();
            spatialGridCellsBuffer?.Release();
            spatialGridOffsetsBuffer?.Release();
            spatialGridCountsBuffer?.Release();
            spatialGridIndicesBuffer?.Release();
        }

        public void ResetSimulation()
        {
            InitializeParticles();
            particlePositionsBuffer.SetData(particlePositions);
            particleVelocitiesBuffer.SetData(particleVelocities);
        }
        
        // Debug GUI
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 400));
            GUILayout.Label("SPH-FLIP Simulation Debug");
            GUILayout.Label($"Particles: {particleCount}");
            GUILayout.Label($"Gravity Scale: {particleGravityScale}");
            GUILayout.Label($"Pressure Multiplier: {pressureMultiplier}");
            GUILayout.Label($"Target Density: {targetDensity}");
            GUILayout.Label($"Smoothing Radius: {smoothingRadius}");
            
            GUILayout.Space(10);
            if (GUILayout.Button("Increase Gravity"))
            {
                particleGravityScale += 2.0f;
            }
            if (GUILayout.Button("Decrease Gravity"))
            {
                particleGravityScale = Mathf.Max(0.1f, particleGravityScale - 2.0f);
            }
            if (GUILayout.Button("Reset Simulation"))
            {
                ResetSimulation();
            }
            GUILayout.EndArea();
        }
    }
}