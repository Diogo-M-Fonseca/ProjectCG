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
        [SerializeField] private ComputeShader gridComputeShader;

        // Compute Buffers
        private ComputeBuffer particlePositionsBuffer;
        private ComputeBuffer particleVelocitiesBuffer;
        private ComputeBuffer densitiesBuffer;
        private ComputeBuffer pressureForcesBuffer;
        private ComputeBuffer spatialGridCellsBuffer;
        private ComputeBuffer spatialGridOffsetsBuffer;
        private ComputeBuffer spatialGridCountsBuffer;
        private ComputeBuffer spatialGridIndicesBuffer;
        private ComputeBuffer gridVelocityRead;
        private ComputeBuffer gridVelocityWrite;
        private ComputeBuffer gridVelocityFloat;
        private ComputeBuffer gridWeight;
        private ComputeBuffer gridDensity;
        private ComputeBuffer divergence;
        private ComputeBuffer pressureRead;
        private ComputeBuffer pressureWrite;
        private ComputeBuffer gridVelocityIntBuffer;

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

            InitializeGridComputeShader();

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

            if (gridComputeShader != null)
            {
                RunGridSimulation(dt);
            }
            else
            {
                Debug.LogError("Compute Shader is not assigned!");
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

        void InitializeGridComputeShader()
        {
            int totalCells = gridCellsX * gridCellsY * gridCellsZ;

            gridVelocityRead = new ComputeBuffer(totalCells, sizeof(float) * 3);
            gridVelocityWrite = new ComputeBuffer(totalCells, sizeof(float) * 3);
            gridVelocityFloat = new ComputeBuffer(totalCells, sizeof(float) * 3);
            gridWeight = new ComputeBuffer(totalCells, sizeof(uint));
            gridDensity = new ComputeBuffer(totalCells, sizeof(uint));
            divergence = new ComputeBuffer(totalCells, sizeof(float));
            pressureRead = new ComputeBuffer(totalCells, sizeof(float));
            pressureWrite = new ComputeBuffer(totalCells, sizeof(float));
            gridVelocityIntBuffer = new ComputeBuffer(totalCells, sizeof(int) * 3);

            int[] zeroInts = new int[totalCells * 3]; 
            gridVelocityIntBuffer.SetData(zeroInts);
            Vector3[] zeros = new Vector3[totalCells];
            gridVelocityRead.SetData(zeros);
            gridVelocityWrite.SetData(zeros);
            gridVelocityFloat.SetData(zeros);
            float[] zerosF = new float[totalCells];
            divergence.SetData(zerosF);
            float[] zerosP = new float[totalCells];
            pressureRead.SetData(zerosP);
            pressureWrite.SetData(zerosP);
            uint[] zerosU = new uint[totalCells];
            gridWeight.SetData(zerosU);
            gridDensity.SetData(zerosU);
        }

        void RunGridSimulation(float dt)
        {
            int totalCells = gridCellsX * gridCellsY * gridCellsZ;

            int gx = Mathf.Max(1, Mathf.CeilToInt(gridCellsX / 8f));
            int gy = Mathf.Max(1, Mathf.CeilToInt(gridCellsY / 8f));
            int gz = Mathf.Max(1, Mathf.CeilToInt(gridCellsZ / 4f));
            int particleGroups = Mathf.CeilToInt(particleCount / 256f);

            Vector3[] zeroV = new Vector3[totalCells];
            float[] zeroF = new float[totalCells];
            uint[] zeroU = new uint[totalCells];

            gridVelocityWrite.SetData(zeroV);
            gridVelocityFloat.SetData(zeroV);
            gridWeight.SetData(zeroU);
            gridDensity.SetData(zeroU);
            divergence.SetData(zeroF);
            pressureWrite.SetData(zeroF);

            int clearKernel = gridComputeShader.FindKernel("ClearGrid");
            int transferKernel = gridComputeShader.FindKernel("TransferToGrid");
            int normalizeKernel = gridComputeShader.FindKernel("NormalizeGrid");
            int advectKernel = gridComputeShader.FindKernel("AdvectGridVelocity");
            int divergenceKernel = gridComputeShader.FindKernel("ComputeDivergence");
            int jacobiKernel = gridComputeShader.FindKernel("JacobiPressure");
            int subtractKernel = gridComputeShader.FindKernel("SubtractPressureGradient");
            int g2pKernel = gridComputeShader.FindKernel("GridToParticles");

            gridComputeShader.SetInt("particleCount", particleCount);
            gridComputeShader.SetInt("gridSizeX", gridCellsX);
            gridComputeShader.SetInt("gridSizeY", gridCellsY);
            gridComputeShader.SetInt("gridSizeZ", gridCellsZ);
            gridComputeShader.SetFloat("dt", dt);
            gridComputeShader.SetFloat("particleMass", particleMass);
            gridComputeShader.SetFloat("flipRatio", flipBlend);
            gridComputeShader.SetFloat("cellSize", spatialCellSize);

            gridComputeShader.SetBuffer(clearKernel, "gridDensity", gridDensity);
            gridComputeShader.SetBuffer(clearKernel, "gridVelocityInt", gridVelocityIntBuffer);
            gridComputeShader.SetBuffer(clearKernel, "gridWeight", gridWeight);
            gridComputeShader.Dispatch(clearKernel, gx, gy, gz);

            gridComputeShader.SetBuffer(transferKernel, "particlePositions", particlePositionsBuffer);
            gridComputeShader.SetBuffer(transferKernel, "particleVelocities", particleVelocitiesBuffer);
            gridComputeShader.SetBuffer(transferKernel, "gridVelocityInt", gridVelocityIntBuffer);
            gridComputeShader.SetBuffer(transferKernel, "gridWeight", gridWeight);
            gridComputeShader.SetBuffer(transferKernel, "gridDensity", gridDensity);
            gridComputeShader.Dispatch(transferKernel, particleGroups, 1, 1);

            gridComputeShader.SetBuffer(normalizeKernel, "gridVelocityInt", gridVelocityIntBuffer);
            gridComputeShader.SetBuffer(normalizeKernel, "gridVelocityFloat", gridVelocityFloat);
            gridComputeShader.SetBuffer(normalizeKernel, "gridWeight", gridWeight);
            gridComputeShader.Dispatch(normalizeKernel, gx, gy, gz);

            gridComputeShader.SetBuffer(advectKernel, "gridVelocityRead", gridVelocityRead);
            gridComputeShader.SetBuffer(advectKernel, "gridVelocityWrite", gridVelocityWrite);
            gridComputeShader.Dispatch(advectKernel, gx, gy, gz);

            gridComputeShader.SetBuffer(divergenceKernel, "gridVelocityRead", gridVelocityRead);
            gridComputeShader.SetBuffer(divergenceKernel, "divergence", divergence);
            gridComputeShader.Dispatch(divergenceKernel, gx, gy, gz);

            for (int i = 0; i < 20; i++)
            {
                gridComputeShader.SetBuffer(jacobiKernel, "pressureRead", pressureRead);
                gridComputeShader.SetBuffer(jacobiKernel, "pressureWrite", pressureWrite);
                gridComputeShader.Dispatch(jacobiKernel, gx, gy, gz);

                
                ComputeBuffer tmp = pressureRead;
                pressureRead = pressureWrite;
                pressureWrite = tmp;
            }

            gridComputeShader.SetBuffer(subtractKernel, "gridVelocityWrite", gridVelocityWrite);
            gridComputeShader.SetBuffer(subtractKernel, "pressureRead", pressureRead);
            gridComputeShader.Dispatch(subtractKernel, gx, gy, gz);

            gridComputeShader.SetBuffer(g2pKernel, "particlePositions", particlePositionsBuffer);
            gridComputeShader.SetBuffer(g2pKernel, "particleVelocities", particleVelocitiesBuffer);
            gridComputeShader.SetBuffer(g2pKernel, "gridVelocityRead", gridVelocityRead);
            gridComputeShader.SetBuffer(g2pKernel, "gridVelocityWrite", gridVelocityWrite);
            gridComputeShader.SetFloat("flipRatio", flipBlend);
            gridComputeShader.Dispatch(g2pKernel, particleGroups, 1, 1);

            var tempV = gridVelocityRead;
            gridVelocityRead = gridVelocityWrite;
            gridVelocityWrite = tempV;
            
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