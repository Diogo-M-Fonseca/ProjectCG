using UnityEngine;

namespace CGProject
{
    public class FluidGrid3D
    {
        //Tamanho da Grid
        private int sizeX, sizeY, sizeZ;
        //Tamanho de cada Cell individual
        private float cellSize = 1f;
        // Velocidade escalar
        public float velocityScale = 1.0f;

        //Velocidade dos fluidos em cada cell
        private Vector3[,,] velocity;
        //Buffer temporário de velocidade para Advection
        private Vector3[,,] velocityTemp;

        //Densidade
        private float[,,] density;
        //Buffer temporário de velocidade para Advection
        private float[,,] densityTemp;

        //Pressão 
        private float[,,] pressure;

        //Mudança de velocidade
        private float[,,] divergence;

        //Número de iterações de Jacobi para a resolução da pressão
        private const int _PressureIT = 50;


        /// <summary>
        /// Cria uma grid com o tamanho dado
        /// Todas as propriedades são inicializadas com valor 0
        /// </summary>
        public FluidGrid3D(int x, int y, int z)
        {
            sizeX = x;
            sizeY = y;
            sizeZ = z;

            velocity = new Vector3[x, y, z];
            velocityTemp = new Vector3[x, y, z];

            density = new float[x, y, z];
            densityTemp = new float[x, y, z];

            pressure = new float[x, y, z];
            divergence = new float[x, y, z];
        }

        /// <summary>
        /// Avança a simulação por um passo
        /// !!!Alerta!!! Ordem importa
        /// </summary>
        public void Step(float dt)
        {
            //Aplica forças externas (gravidade)
            ApplyExternalForces(dt);
            //Mover o campo de velocidade através de si mesmo (semi-Lagrangian)
            AdvectVelocity(dt);
            //Forçar a não comprimir
            Project();    
            //Mover densidade pelo campo de velocidade
            AdvectDensity(dt);
            //Força as particulas a manterem se dentro da caixa
            EnforceBoundary();
        }

        /// <summary>
        /// Advecção semi-Lagrangian de velocidade.
        /// Cada cell da grid percorre o campo de velocidade para trás
        /// e sampla a velocidade anterior.
        /// </summary>
        void AdvectVelocity(float dt)
        {
            for (int x = 1; x < sizeX - 1; x++)
                for (int y = 1; y < sizeY - 1; y++)
                    for (int z = 1; z < sizeZ - 1; z++)
                    {
                        //posição atual da cell na grid
                        Vector3 pos = new Vector3(x, y, z);
                        //através da velocidade atual verificar posição anterior
                        Vector3 prev = pos - velocity[x, y, z] * dt;

                        //fazer sample da velocidade da ultima posição
                        velocityTemp[x, y, z] = SampleVelocity(prev);
                    }
            //ping-pong
            Swap(ref velocity, ref velocityTemp);
        }

        /// <summary>
        /// Advecção semi-Lagrangian de densidade.
        /// A densidade é transportada pelo campo de velocidade.
        /// </summary>
        void AdvectDensity(float dt)
        {
            for (int x = 1; x < sizeX - 1; x++)
                for (int y = 1; y < sizeY - 1; y++)
                    for (int z = 1; z < sizeZ - 1; z++)
                    {
                        Vector3 pos = new Vector3(x, y, z);
                        Vector3 prev = pos - velocity[x, y, z] * dt * velocityScale;

                        densityTemp[x, y, z] = SampleDensity(prev);
                    }

            Swap(ref density, ref densityTemp);
        }

        /// <summary>
        /// Impõe a incompressibilidade por meio de:
        /// 1. Cálculo da divergência da velocidade
        /// 2. Resolução da pressão
        /// 3. Subtração do gradiente de pressão da velocidade
        /// </summary>
        void Project()
        {
            ComputeDivergence();
            ClearPressure();

            // metodo de Jacobi para a equação de Poisson
            // (recomendação do chatgpt mas também levemente mencionado em
            // https://cg.informatik.uni-freiburg.de/intern/seminar/gridFluids_fluid-EulerParticle.pdf)
            for (int i = 0; i < _PressureIT; i++)
                JacobiPressure();

            SubtractPressureGradient();
        }

        /// <summary>
        /// Calcula divergencia de velocidade
        /// Divergencia define o quanto o fluido comprime e descomprime
        /// </summary>
        void ComputeDivergence()
        {
            float scale = 0.5f / cellSize;

            for (int x = 1; x < sizeX - 1; x++)
                for (int y = 1; y < sizeY - 1; y++)
                    for (int z = 1; z < sizeZ - 1; z++)
                    {
                        divergence[x, y, z] =
                            (velocity[x + 1, y, z].x - velocity[x - 1, y, z].x +
                             velocity[x, y + 1, z].y - velocity[x, y - 1, z].y +
                             velocity[x, y, z + 1].z - velocity[x, y, z - 1].z) * scale;
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
        /// Subtrai o gradiente de pressão da velocidade,
        /// tornando o campo de velocidade livre de divergência.
        /// </summary>
        void SubtractPressureGradient()
        {
            float scale = 0.5f / cellSize;

            for (int x = 1; x < sizeX - 1; x++)
                for (int y = 1; y < sizeY - 1; y++)
                    for (int z = 1; z < sizeZ - 1; z++)
                    {
                        velocity[x, y, z] -= new Vector3(
                            pressure[x + 1, y, z] - pressure[x - 1, y, z],
                            pressure[x, y + 1, z] - pressure[x, y - 1, z],
                            pressure[x, y, z + 1] - pressure[x, y, z - 1]
                        ) * scale;
                    }
        }

        /// <summary>
        /// Faz Sample do campo de velocidade usando interpolação trilinear.
        /// Necessário para estabilidade semi-Lagrangiana.
        /// </summary>
        public Vector3 SampleVelocity(Vector3 p)
        {
            p = ClampPosition(p);

            int x0 = Mathf.FloorToInt(p.x);
            int y0 = Mathf.FloorToInt(p.y);
            int z0 = Mathf.FloorToInt(p.z);

            int x1 = x0 + 1;
            int y1 = y0 + 1;
            int z1 = z0 + 1;

            float tx = p.x - x0;
            float ty = p.y - y0;
            float tz = p.z - z0;

            return Trilerp(
                velocity[x0, y0, z0], velocity[x1, y0, z0],
                velocity[x0, y1, z0], velocity[x1, y1, z0],
                velocity[x0, y0, z1], velocity[x1, y0, z1],
                velocity[x0, y1, z1], velocity[x1, y1, z1],
                tx, ty, tz
            );
        }


        /// <summary>
        /// Amostra o campo de densidade usando interpolação trilinear.
        /// </summary>
        float SampleDensity(Vector3 p)
        {
            p = ClampPosition(p);

            int x0 = Mathf.FloorToInt(p.x);
            int y0 = Mathf.FloorToInt(p.y);
            int z0 = Mathf.FloorToInt(p.z);

            int x1 = x0 + 1;
            int y1 = y0 + 1;
            int z1 = z0 + 1;

            float tx = p.x - x0;
            float ty = p.y - y0;
            float tz = p.z - z0;

            return Mathf.Lerp(
                Mathf.Lerp(
                    Mathf.Lerp(density[x0, y0, z0], density[x1, y0, z0], tx),
                    Mathf.Lerp(density[x0, y1, z0], density[x1, y1, z0], tx),
                    ty),
                Mathf.Lerp(
                    Mathf.Lerp(density[x0, y0, z1], density[x1, y0, z1], tx),
                    Mathf.Lerp(density[x0, y1, z1], density[x1, y1, z1], tx),
                    ty),
                tz);
        }

        /// <summary>
        /// Limita as posições a um intervalo válido da grade.
        /// Impede o acesso a valores fora dos limites.
        /// </summary>
        Vector3 ClampPosition(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, 0, sizeX - 2);
            p.y = Mathf.Clamp(p.y, 0, sizeY - 2);
            p.z = Mathf.Clamp(p.z, 0, sizeZ - 2);
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

        /// <summary>
        /// Limpa a pressão antes de aplicar o metodo de jacobi
        /// </summary>
        void ClearPressure()
        {
            System.Array.Clear(pressure, 0, pressure.Length);
        }

        /// <summary>
        /// Troca duas matrizes 3D (buffers ping-pong).
        /// </summary>
        void Swap<T>(ref T[,,] a, ref T[,,] b)
        {
            (a, b) = (b, a);
        }

        /// <summary>
        /// Cria uma textura 2D da velocidade no plano XZ (meio da grade Y)
        /// </summary>
        public Texture2D GetVelocityTexture()
        {
            Texture2D tex = new Texture2D(sizeX, sizeZ);
    
            int middleY = sizeY / 2;
    
            for (int x = 0; x < sizeX; x++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    Vector3 vel = velocity[x, middleY, z];
                    Color color = new Color(
                        (vel.x + 1f) * 0.5f,
                        0f,
                        (vel.z + 1f) * 0.5f,
                        1f
                    );
            
                    tex.SetPixel(x, z, color);
                }
            }
    
            tex.Apply();
            return tex;
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
        
        // Getter para a velocidade
        public Vector3 GetVelocity(int x, int y, int z)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
            {
                return velocity[x, y, z];
            }
            return Vector3.zero;
        }

        void ApplyExternalForces(float dt)
        {
            Vector3 gravity = new Vector3(0, -9.81f, 0);
            for (int x = 0; x < sizeX; x++)
                for (int y = 0; y < sizeY; y++)
                    for (int z = 0; z < sizeZ; z++)
                    {
                        velocity[x, y, z] += gravity * dt;
                    }
        }

        void EnforceBoundary()
        {
            for (int y = 0; y < sizeY; y++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    // Paredes da esquerda e direita
                    velocity[0, y, z] = Vector3.zero;
                    velocity[sizeX - 1, y, z] = Vector3.zero;
                }
            }
            
            // Paredes frente e trás
            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    velocity[x, y, 0] = Vector3.zero;
                    velocity[x, y, sizeZ - 1] = Vector3.zero;
                }
            }
            
            // Fundo
            for (int x = 0; x < sizeX; x++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    velocity[x, 0, z] = new Vector3(
                        velocity[x, 0, z].x,
                        0,
                        velocity[x, 0, z].z
                    );
                }
            }
        }
    }
}