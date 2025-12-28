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
        private int simulationWidth = 256; //To be defined in inspector
        [SerializeField]
        private int simulationHeight = 256;
        
        // Particle settings
        [SerializeField]
        private int particleCount = 10000;
        [SerializeField]
        private float particleSize = 0.1f;
        [SerializeField]
        private Material particleMaterial;
        
        // Data arrays
        Vector3[] particlePositions;
        Vector3[] particleVelocities;
        
        // GPU buffers
        ComputeBuffer particlePositionsBuffer;
        ComputeBuffer particleVelocitiesBuffer;
        
        Mesh particleMesh;
        
        void Start()
        {
            // Generating Mesh
            particleMesh = FluidGenerator.MeshGenerator(1);
            
            // Initializing arrays
            particlePositions = new Vector3[particleCount];
            particleVelocities = new Vector3[particleCount];
            
            // Random initial positions
            for (int i = 0; i < particleCount; i++)
            {
                particlePositions[i] = new Vector3(
                    Random.Range(-simulationWidth/2, simulationWidth/2),
                    0,
                    Random.Range(-simulationHeight/2, simulationHeight/2)
                );
                particleVelocities[i] = Vector3.zero;
            }
            
            // Creating buffers
            particlePositionsBuffer = new ComputeBuffer(
                particleCount, 
                sizeof(float) * 3 // 3 floats per Vector3
            );
            
            particleVelocitiesBuffer = new ComputeBuffer(
                particleCount, 
                sizeof(float) * 3
            );
            
            // Setting initial data
            particlePositionsBuffer.SetData(particlePositions);
            particleVelocitiesBuffer.SetData(particleVelocities);
            
            // Connecting buffers to shader
            particleMaterial.SetBuffer("_ParticlePositions", particlePositionsBuffer);
            particleMaterial.SetBuffer("_ParticleVelocities", particleVelocitiesBuffer);
            particleMaterial.SetFloat("_ParticleSize", particleSize);
        }
        
        void Update()
        {   
            // Get velocity from grid
            Texture2D velocityTexture = grid.GetVelocityTexture();
            
            // Update every particle's velocity to grid velocity
            for (int i = 0; i < particleCount; i++)
            {
                // Converting 3D world position to 2D texture UV (0-1 range)
                Vector2 uv = new Vector2(
                    (particlePositions[i].x / simulationWidth) + 0.5f,
                    (particlePositions[i].z / simulationHeight) + 0.5f
                );
                
                // Clamp UV to texture bounds
                uv.x = Mathf.Clamp01(uv.x);
                uv.y = Mathf.Clamp01(uv.y);
                
                // Sample velocity texture
                Color velColor = velocityTexture.GetPixelBilinear(uv.x, uv.y);
                
                Vector3 velocity = new Vector3(
                    velColor.r * 2f - 1f,  // X velocity (-1 to +1)
                    0f,                     // Y buoyancy can be added
                    velColor.g * 2f - 1f   // Z velocity (-1 to +1)
                );
                
                // Scale by simulation intensity
                velocity *= grid.velocityScale;
                
                // Update particle velocity
                particleVelocities[i] = velocity;
                
                // Update position based on velocity
                particlePositions[i] += velocity * Time.deltaTime;
                
                // Wrap particles at simulation boundaries
                if (particlePositions[i].x < -simulationWidth/2) 
                    particlePositions[i].x = simulationWidth/2;
                if (particlePositions[i].x > simulationWidth/2) 
                    particlePositions[i].x = -simulationWidth/2;
                if (particlePositions[i].z < -simulationHeight/2) 
                    particlePositions[i].z = simulationHeight/2;
                if (particlePositions[i].z > simulationHeight/2) 
                    particlePositions[i].z = -simulationHeight/2;
            }
            
            // Send data to buffers
            particlePositionsBuffer.SetData(particlePositions);
            particleVelocitiesBuffer.SetData(particleVelocities);
            
            // Rendering
            Graphics.DrawMeshInstancedProcedural(
                particleMesh, 
                0, 
                particleMaterial, 
                particleMesh.bounds, 
                particleCount
            );
        }
        
        void OnDestroy()
        {
            // Cleaning up buffers
            if (particlePositionsBuffer != null) 
                particlePositionsBuffer.Release();
            if (particleVelocitiesBuffer != null) 
                particleVelocitiesBuffer.Release();
        }
    }
    }
