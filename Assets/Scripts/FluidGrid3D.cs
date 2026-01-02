using UnityEngine;

namespace CGProject
{
    public class FluidGrid3D
    {
        // Tamanho da Grid
        private int sizeX, sizeY, sizeZ;
        // Origem da grid no mundo
        private Vector3 origin;
        
        // Velocidade dos fluidos em cada cell
        private Vector3[,,] velocity;
        // Buffer temporário de velocidade para Advection
        private Vector3[,,] velocityTemp;

        // Densidade
        private float[,,] density;
        // Buffer temporário de densidade
        private float[,,] densityTemp;

        // Pressão 
        private float[,,] pressure;

        // Divergência
        private float[,,] divergence;

        // Número de iterações de Jacobi para a resolução da pressão
        private const int _PressureIT = 20;
        // Valor da viscosidade
        private float viscosity = 0.01f;

        // Gravidade aplicada à grid
        private Vector3 gravity = new Vector3(0, -9.81f, 0);

        /// <summary>
        /// Cria uma grid com o tamanho dado
        /// Todas as propriedades são inicializadas com valor 0
        /// </summary>
        public FluidGrid3D(int x, int y, int z)
        {
            sizeX = x;
            sizeY = y;
            sizeZ = z;
            origin = Vector3.zero;

            velocity = new Vector3[x, y, z];
            velocityTemp = new Vector3[x, y, z];

            density = new float[x, y, z];
            densityTemp = new float[x, y, z];

            pressure = new float[x, y, z];
            divergence = new float[x, y, z];
        }
        
        /// <summary>
        /// Define a origem da grid no mundo
        /// </summary>
        public void SetOrigin(Vector3 newOrigin)
        {
            origin = newOrigin;
        }

        /// <summary>
        /// Avança a simulação por um passo
        /// </summary>
        public void Step(float dt)
        {
            // Aplica forças externas (gravidade)
            ApplyExternalForces(dt);
            // Aplica viscosidade
            ApplyViscosity(dt);
            // Forçar a não comprimir
            Project();  
            // Força as particulas a manterem se dentro da caixa
            EnforceBoundary();
        }

        void ApplyViscosity(float dt)
        {
            float alpha = dt * viscosity;
            
            for (int x = 1; x < sizeX - 1; x++)
                for (int y = 1; y < sizeY - 1; y++)
                    for (int z = 1; z < sizeZ - 1; z++)
                    {
                        // Laplaciano da velocidade
                        Vector3 laplacian = 
                            (velocity[x+1, y, z] + velocity[x-1, y, z] +
                            velocity[x, y+1, z] + velocity[x, y-1, z] +
                            velocity[x, y, z+1] + velocity[x, y, z-1] - 
                            6 * velocity[x, y, z]);
                        
                        velocityTemp[x, y, z] = velocity[x, y, z] + alpha * laplacian;
                    }
            Swap(ref velocity, ref velocityTemp);
        }

        void ApplyExternalForces(float dt)
        {
            for (int x = 0; x < sizeX; x++)
                for (int y = 0; y < sizeY; y++)
                    for (int z = 0; z < sizeZ; z++)
                    {
                        velocity[x, y, z] += gravity * dt;
                    }
        }

        /// <summary>
        /// Impõe a incompressibilidade
        /// </summary>
        void Project()
        {
            ComputeDivergence();
            ClearPressure();

            // metodo de Jacobi para a equação de Poisson
            for (int i = 0; i < _PressureIT; i++)
            {
                ApplyPressureBoundary();
                JacobiPressure();
            }
            SubtractPressureGradient();
        }

        /// <summary>
        /// Calcula divergencia de velocidade
        /// </summary>
        void ComputeDivergence()
        {
            for (int x = 1; x < sizeX - 1; x++)
                for (int y = 1; y < sizeY - 1; y++)
                    for (int z = 1; z < sizeZ - 1; z++)
                    {
                        divergence[x, y, z] = -0.5f * (
                             velocity[x + 1, y, z].x - velocity[x - 1, y, z].x +
                             velocity[x, y + 1, z].y - velocity[x, y - 1, z].y +
                             velocity[x, y, z + 1].z - velocity[x, y, z - 1].z);
                    }
        }

        /// <summary>
        /// Uma iteração de Jacobi para resolver a equação de Poisson da pressão.
        /// </summary>
        void JacobiPressure()
        {
            for (int x = 1; x < sizeX - 1; x++)
                for (int y = 1; y < sizeY - 1; y++)
                    for (int z = 1; z < sizeZ - 1; z++)
                    {
                        pressure[x, y, z] =
                            (pressure[x + 1, y, z] + pressure[x - 1, y, z] +
                             pressure[x, y + 1, z] + pressure[x, y - 1, z] +
                             pressure[x, y, z + 1] + pressure[x, y, z - 1] -
                             divergence[x, y, z]) / 6f;
                    }
        }

        /// <summary>
        /// Subtrai o gradiente de pressão da velocidade
        /// </summary>
        void SubtractPressureGradient()
        {
            for (int x = 1; x < sizeX - 1; x++)
                for (int y = 1; y < sizeY - 1; y++)
                    for (int z = 1; z < sizeZ - 1; z++)
                    {
                        velocity[x, y, z] -= new Vector3(
                            pressure[x + 1, y, z] - pressure[x - 1, y, z],
                            pressure[x, y + 1, z] - pressure[x, y - 1, z],
                            pressure[x, y, z + 1] - pressure[x, y, z - 1]
                        ) * 0.5f;
                    }
        }

        /// <summary>
        /// Faz Sample do campo de velocidade usando interpolação trilinear.
        /// </summary>
        public Vector3 SampleVelocity(Vector3 worldPos)
        {
            Vector3 localPos = worldPos - origin;
            localPos = ClampPosition(localPos);

            int x0 = Mathf.FloorToInt(localPos.x);
            int y0 = Mathf.FloorToInt(localPos.y);
            int z0 = Mathf.FloorToInt(localPos.z);

            int x1 = Mathf.Min(x0 + 1, sizeX - 1);
            int y1 = Mathf.Min(y0 + 1, sizeY - 1);
            int z1 = Mathf.Min(z0 + 1, sizeZ - 1);

            float tx = localPos.x - x0;
            float ty = localPos.y - y0;
            float tz = localPos.z - z0;

            return Trilerp(
                velocity[x0, y0, z0], velocity[x1, y0, z0],
                velocity[x0, y1, z0], velocity[x1, y1, z0],
                velocity[x0, y0, z1], velocity[x1, y0, z1],
                velocity[x0, y1, z1], velocity[x1, y1, z1],
                tx, ty, tz
            );
        }

        /// <summary>
        /// Limita as posições a um intervalo válido da grade.
        /// </summary>
        Vector3 ClampPosition(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, 0, sizeX - 1.001f);
            p.y = Mathf.Clamp(p.y, 0, sizeY - 1.001f);
            p.z = Mathf.Clamp(p.z, 0, sizeZ - 1.001f);
            return p;
        }

        /// <summary>
        /// Interpolação trilinear para valores Vector3.
        /// </summary>
        Vector3 Trilerp(
            Vector3 c000, Vector3 c100,
            Vector3 c010, Vector3 c110,
            Vector3 c001, Vector3 c101,
            Vector3 c011, Vector3 c111,
            float tx, float ty, float tz)
        {
            Vector3 x00 = Vector3.Lerp(c000, c100, tx);
            Vector3 x10 = Vector3.Lerp(c010, c110, tx);
            Vector3 x01 = Vector3.Lerp(c001, c101, tx);
            Vector3 x11 = Vector3.Lerp(c011, c111, tx);

            Vector3 y0 = Vector3.Lerp(x00, x10, ty);
            Vector3 y1 = Vector3.Lerp(x01, x11, ty);

            return Vector3.Lerp(y0, y1, tz);
        }

        void ClearPressure()
        {
            System.Array.Clear(pressure, 0, pressure.Length);
        }

        void Swap<T>(ref T[,,] a, ref T[,,] b)
        {
            (a, b) = (b, a);
        }

        void EnforceBoundary()
        {
            // Paredes X
            for (int y = 0; y < sizeY; y++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    velocity[0, y, z] = Vector3.zero;
                    velocity[sizeX - 1, y, z] = Vector3.zero;
                    
                    density[0, y, z] = 0;
                    density[sizeX - 1, y, z] = 0;
                }
            }
            
            // Paredes Z
            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    velocity[x, y, 0] = Vector3.zero;
                    velocity[x, y, sizeZ - 1] = Vector3.zero;
                    
                    density[x, y, 0] = 0;
                    density[x, y, sizeZ - 1] = 0;
                }
            }
            
            // Paredes Y
            for (int x = 0; x < sizeX; x++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    velocity[x, 0, z] = Vector3.zero;
                    velocity[x, sizeY - 1, z] = Vector3.zero;
                    
                    density[x, 0, z] = 0;
                    density[x, sizeY - 1, z] = 0;
                }
            }
        }

        void ApplyPressureBoundary()
        {
            // Paredes X
            for (int y = 0; y < sizeY; y++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    pressure[0, y, z] = pressure[1, y, z];
                    pressure[sizeX - 1, y, z] = pressure[sizeX - 2, y, z];
                }
            }
            
            // Paredes Y
            for (int x = 0; x < sizeX; x++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    pressure[x, 0, z] = pressure[x, 1, z];
                    pressure[x, sizeY - 1, z] = pressure[x, sizeY - 2, z];
                }
            }
            
            // Paredes Z
            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    pressure[x, y, 0] = pressure[x, y, 1];
                    pressure[x, y, sizeZ - 1] = pressure[x, y, sizeZ - 2];
                }
            }
        }

        /// <summary>
        /// Adiciona densidade em uma posição específica
        /// </summary>
        public void AddDensity(int x, int y, int z, float dens)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
            {
                density[x, y, z] += dens;
            }
        }

        /// <summary>
        /// Adiciona velocidade em uma posição específica
        /// </summary>
        public void AddVelocity(int x, int y, int z, Vector3 vel)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
            {
                velocity[x, y, z] += vel;
            }
        }

        /// <summary>
        /// Reseta toda a grid para zero
        /// </summary>
        public void Reset()
        {
            System.Array.Clear(velocity, 0, velocity.Length);
            System.Array.Clear(density, 0, density.Length);
            System.Array.Clear(pressure, 0, pressure.Length);
            System.Array.Clear(divergence, 0, divergence.Length);
        }
    }
}