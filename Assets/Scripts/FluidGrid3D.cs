using UnityEngine;
using System;

namespace CGProject
{
    public class FluidGrid3D
    {
        private int sizeX, sizeY, sizeZ;
        
        private Vector3[,,] velocity;
        private Vector3[,,] velocityPrev;
        
        private float[,,] density;
        private float[,,] weight;

        private Vector3 gravity = new Vector3(0, -9.81f, 0); // Normal gravity

        public FluidGrid3D(int x, int y, int z)
        {
            sizeX = x;
            sizeY = y;
            sizeZ = z;

            velocity = new Vector3[x, y, z];
            velocityPrev = new Vector3[x, y, z];
            density = new float[x, y, z];
            weight = new float[x, y, z];
        }
        
        public void Step(float dt)
        {
            // Save current velocity for FLIP
            Array.Copy(velocity, velocityPrev, velocity.Length);
            
            // SIMPLIFIED: Just apply gravity and basic advection
            for (int x = 0; x < sizeX; x++)
                for (int y = 0; y < sizeY; y++)
                    for (int z = 0; z < sizeZ; z++)
                    {
                        // Apply gravity
                        velocity[x, y, z] += gravity * dt;
                        
                        // Simple boundary damping
                        if (x == 0 || x == sizeX - 1 || 
                            y == 0 || y == sizeY - 1 || 
                            z == 0 || z == sizeZ - 1)
                        {
                            velocity[x, y, z] *= 0.9f;
                        }
                    }
            
            // Apply boundary conditions
            EnforceBoundary();
        }

        void EnforceBoundary()
        {
            // SIMPLIFIED: Just zero out boundaries
            for (int y = 0; y < sizeY; y++)
                for (int z = 0; z < sizeZ; z++)
                {
                    velocity[0, y, z] = Vector3.zero;
                    velocity[sizeX - 1, y, z] = Vector3.zero;
                }
            
            for (int x = 0; x < sizeX; x++)
                for (int z = 0; z < sizeZ; z++)
                {
                    velocity[x, 0, z] = Vector3.zero;
                    velocity[x, sizeY - 1, z] = Vector3.zero;
                }
            
            for (int x = 0; x < sizeX; x++)
                for (int y = 0; y < sizeY; y++)
                {
                    velocity[x, y, 0] = Vector3.zero;
                    velocity[x, y, sizeZ - 1] = Vector3.zero;
                }
        }

        public Vector3 SampleVelocity(Vector3 worldPos)
        {
            // SIMPLIFIED: Direct grid lookup
            int x = Mathf.Clamp((int)worldPos.x, 0, sizeX - 1);
            int y = Mathf.Clamp((int)worldPos.y, 0, sizeY - 1);
            int z = Mathf.Clamp((int)worldPos.z, 0, sizeZ - 1);
            
            return velocity[x, y, z];
        }

        public Vector3 SamplePreviousVelocity(Vector3 worldPos)
        {
            // SIMPLIFIED: Direct grid lookup
            int x = Mathf.Clamp((int)worldPos.x, 0, sizeX - 1);
            int y = Mathf.Clamp((int)worldPos.y, 0, sizeY - 1);
            int z = Mathf.Clamp((int)worldPos.z, 0, sizeZ - 1);
            
            return velocityPrev[x, y, z];
        }

        // Weight grid methods
        public void AddWeight(int x, int y, int z, float w)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                weight[x, y, z] += w;
        }

        public float GetWeight(int x, int y, int z)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                return weight[x, y, z];
            return 0f;
        }

        public void SetWeight(int x, int y, int z, float w)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                weight[x, y, z] = w;
        }

        // Existing methods
        public void AddDensity(int x, int y, int z, float dens)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                density[x, y, z] += dens;
        }

        public void AddVelocity(int x, int y, int z, Vector3 vel)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                velocity[x, y, z] += vel;
        }

        public float GetDensity(int x, int y, int z)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                return density[x, y, z];
            return 0f;
        }

        public Vector3 GetVelocity(int x, int y, int z)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                return velocity[x, y, z];
            return Vector3.zero;
        }

        public Vector3Int GetGridSize() => new Vector3Int(sizeX, sizeY, sizeZ);

        public void SetVelocity(int x, int y, int z, Vector3 vel)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                velocity[x, y, z] = vel;
        }

        public void SetDensity(int x, int y, int z, float dens)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                density[x, y, z] = dens;
        }
    }
}