using UnityEngine;
using System.Collections.Generic;

namespace CGProject
{
    /// <summary>
    /// Script responsible for applying grid's parameters to fluid particles
    /// </summary>
    public class ParticleManager : MonoBehaviour
    {
        public FluidGrid3D grid;
        
        // Simulation parameters
        [SerializeField]
        private int simulationWidth = 64; // Start with smaller values for testing
        [SerializeField]
        private int simulationHeight = 64;
        [SerializeField]
        private int gridHeight = 16; // Reduced for 3D
        
        // Particle settings
        [SerializeField]
        private int particleCount = 5000; // Start with fewer particles
        [SerializeField]
        private float particleSize = 0.1f;
        [SerializeField]
        private Material particleMaterial;
        
        // Visuals
        [SerializeField]
        private bool showVelocityTexture = true;
        private Texture2D velocityTexture;
        private Mesh quadMesh;
        
        // Data arrays
        Vector3[] particlePositions;
        Vector3[] particleVelocities;
        
        // GPU buffers
        ComputeBuffer particlePositionsBuffer;
        ComputeBuffer particleVelocitiesBuffer;
        
        Mesh particleMesh;
        
        void Start()
        {
            // Create the grid
            grid = new FluidGrid3D(simulationWidth, gridHeight, simulationHeight);
            
            // Add initial velocity
            AddTestVelocity();
            
            // Generate particle mesh
            particleMesh = FluidGenerator.MeshGenerator(3); // Lower resolution for performance
            
            // Create quad for texture display
            CreateQuadMesh();
            
            // Initialize particle arrays
            particlePositions = new Vector3[particleCount];
            particleVelocities = new Vector3[particleCount];
            
            // Initialize particles within grid bounds
            for (int i = 0; i < particleCount; i++)
            {
                // Place particles in grid space (0 to size)
                particlePositions[i] = new Vector3(
                    Random.Range(1, simulationWidth - 1),  // Avoid edges
                    Random.Range(1, gridHeight - 1),
                    Random.Range(1, simulationHeight - 1)
                );
                particleVelocities[i] = Vector3.zero;
            }
            
            // Create compute buffers
            particlePositionsBuffer = new ComputeBuffer(
                particleCount, 
                sizeof(float) * 3
            );
            
            particleVelocitiesBuffer = new ComputeBuffer(
                particleCount, 
                sizeof(float) * 3
            );
            
            // Set initial data
            particlePositionsBuffer.SetData(particlePositions);
            particleVelocitiesBuffer.SetData(particleVelocities);
            
            // Connect to shader
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
            
            // Create velocity texture for debugging
            velocityTexture = new Texture2D(simulationWidth, simulationHeight);
        }
        
        void Update()
        {   
            if (grid == null)
            {
                Debug.LogWarning("Grid is null");
                return;
            }
            
            // Update simulation
            grid.Step(Time.deltaTime);
            
            // Get velocity texture for visualization
            velocityTexture = grid.GetVelocityTexture();
            
            // Update particles
            UpdateParticles();
            
            // Send data to buffers
            if (particlePositionsBuffer != null && particleVelocitiesBuffer != null)
            {
                particlePositionsBuffer.SetData(particlePositions);
                particleVelocitiesBuffer.SetData(particleVelocities);
                
                // Render particles
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
                // Convert particle position to grid indices
                int gridX = Mathf.FloorToInt(particlePositions[i].x);
                int gridY = Mathf.FloorToInt(particlePositions[i].y);
                int gridZ = Mathf.FloorToInt(particlePositions[i].z);
                
                // Clamp to grid bounds (but keep them within sampling range)
                gridX = Mathf.Clamp(gridX, 1, simulationWidth - 2);
                gridY = Mathf.Clamp(gridY, 1, gridHeight - 2);
                gridZ = Mathf.Clamp(gridZ, 1, simulationHeight - 2);
            
                // Sample velocity from grid - Use float coordinates for trilinear interpolation
                Vector3 samplePos = new Vector3(
                    particlePositions[i].x,  // Use actual float position, not integer
                    particlePositions[i].y,
                    particlePositions[i].z);
            
                // Clamp the sampling position to avoid out-of-bounds
                samplePos.x = Mathf.Clamp(samplePos.x, 1, simulationWidth - 2);
                samplePos.y = Mathf.Clamp(samplePos.y, 1, gridHeight - 2);
                samplePos.z = Mathf.Clamp(samplePos.z, 1, simulationHeight - 2);
                
                Vector3 velocity = grid.SampleVelocity(samplePos);  // Now this will work
                
                // Update particle velocity
                particleVelocities[i] = velocity * grid.velocityScale;
                
                // Update position
                particlePositions[i] += particleVelocities[i] * Time.deltaTime;
                
                // Boundary handling - wrap around
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
            // Adds vortex velocity field
            int centerX = simulationWidth / 2;
            int centerZ = simulationHeight / 2;
            int middleY = gridHeight / 2;
            
            for (int x = 1; x < simulationWidth - 1; x++)
            {
                for (int z = 1; z < simulationHeight - 1; z++)
                {
                    // Calculates vector from center
                    float dx = x - centerX;
                    float dz = z - centerZ;
                    float distance = Mathf.Sqrt(dx * dx + dz * dz);
                    
                    if (distance < 10f && distance > 0.1f)
                    {
                        // Creates circular velocity field
                        Vector3 velocity = new Vector3(-dz / distance, 0, dx / distance) * 2f;
                        grid.AddVelocity(x, middleY, z, velocity);
                        
                        // Add some density for visualization
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
        
        void OnGUI()
        {
            if (showVelocityTexture && velocityTexture != null)
            {
                // Debug
                GUI.DrawTexture(new Rect(10, 10, 256, 256), velocityTexture, ScaleMode.ScaleToFit);
            }
        }
        
        void OnDestroy()
        {
            // Cleanup
            if (particlePositionsBuffer != null) 
                particlePositionsBuffer.Release();
            if (particleVelocitiesBuffer != null) 
                particleVelocitiesBuffer.Release();
            if (velocityTexture != null)
                Destroy(velocityTexture);
        }
    }
}