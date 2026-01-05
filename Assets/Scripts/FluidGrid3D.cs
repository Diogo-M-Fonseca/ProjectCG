using UnityEngine;
using System;

namespace CGProject
{
    /// <summary>
    /// Implementação simplificada de uma grelha 3D para simulação de fluidos.
    /// Esta classe gere campos de velocidade, densidade e peso numa mesh tridimensional.
    /// </summary>
    public class FluidGrid3D
    {
        // Dimensões da grelha
        private int sizeX, sizeY, sizeZ;
        
        // Campos de velocidade (atual e do passo anterior para FLIP)
        private Vector3[,,] velocity;
        private Vector3[,,] velocityPrev;
        
        // Campos de densidade e peso
        private float[,,] density;
        private float[,,] weight;

        // Aceleração gravítica (padrão: 9.81 m/s² para baixo)
        private Vector3 gravity = new Vector3(0, -9.81f, 0);

        /// <summary>
        /// Construtor da grelha 3D
        /// </summary>
        /// <param name="x">Número de células no eixo X</param>
        /// <param name="y">Número de células no eixo Y</param>
        /// <param name="z">Número de células no eixo Z</param>
        public FluidGrid3D(int x, int y, int z)
        {
            sizeX = x;
            sizeY = y;
            sizeZ = z;

            // Inicializa arrays com as dimensões especificadas
            velocity = new Vector3[x, y, z];
            velocityPrev = new Vector3[x, y, z];
            density = new float[x, y, z];
            weight = new float[x, y, z];
        }
        
        /// <summary>
        /// Avança a simulação um passo temporal
        /// </summary>
        /// <param name="dt">Intervalo de tempo (delta time)</param>
        public void Step(float dt)
        {
            // Armazena velocidade atual para cálculo FLIP (diferenças entre passos)
            Array.Copy(velocity, velocityPrev, velocity.Length);
            
            // Implementação simplificada: aplica gravidade e amortecimento nas fronteiras
            for (int x = 0; x < sizeX; x++)
                for (int y = 0; y < sizeY; y++)
                    for (int z = 0; z < sizeZ; z++)
                    {
                        // Aplica aceleração gravítica
                        velocity[x, y, z] += gravity * dt;
                        
                        // Amortecimento simplificado nas células de fronteira
                        if (x == 0 || x == sizeX - 1 || 
                            y == 0 || y == sizeY - 1 || 
                            z == 0 || z == sizeZ - 1)
                        {
                            velocity[x, y, z] *= 0.9f;
                        }
                    }
            
            // Aplica condições de fronteira
            EnforceBoundary();
        }

        /// <summary>
        /// Aplica condições de fronteira (velocidade nula nas paredes)
        /// </summary>
        void EnforceBoundary()
        {
            // Fronteiras nos planos X (esquerda/direita)
            for (int y = 0; y < sizeY; y++)
                for (int z = 0; z < sizeZ; z++)
                {
                    velocity[0, y, z] = Vector3.zero;
                    velocity[sizeX - 1, y, z] = Vector3.zero;
                }
            
            // Fronteiras nos planos Y (baixo/cima)
            for (int x = 0; x < sizeX; x++)
                for (int z = 0; z < sizeZ; z++)
                {
                    velocity[x, 0, z] = Vector3.zero;
                    velocity[x, sizeY - 1, z] = Vector3.zero;
                }
            
            // Fronteiras nos planos Z (frente/trás)
            for (int x = 0; x < sizeX; x++)
                for (int y = 0; y < sizeY; y++)
                {
                    velocity[x, y, 0] = Vector3.zero;
                    velocity[x, y, sizeZ - 1] = Vector3.zero;
                }
        }

        /// <summary>
        /// Amostra a velocidade numa posição do mundo (interpolação simplificada)
        /// </summary>
        /// <param name="worldPos">Posição no espaço mundial</param>
        /// <returns>Velocidade na posição especificada</returns>
        public Vector3 SampleVelocity(Vector3 worldPos)
        {
            // Amostragem simplificada: lookup direto na grelha (sem interpolação)
            int x = Mathf.Clamp((int)worldPos.x, 0, sizeX - 1);
            int y = Mathf.Clamp((int)worldPos.y, 0, sizeY - 1);
            int z = Mathf.Clamp((int)worldPos.z, 0, sizeZ - 1);
            
            return velocity[x, y, z];
        }

        /// <summary>
        /// Amostra a velocidade do passo anterior numa posição do mundo
        /// </summary>
        /// <param name="worldPos">Posição no espaço mundial</param>
        /// <returns>Velocidade anterior na posição especificada</returns>
        public Vector3 SamplePreviousVelocity(Vector3 worldPos)
        {
            // Amostragem simplificada: lookup direto na grelha
            int x = Mathf.Clamp((int)worldPos.x, 0, sizeX - 1);
            int y = Mathf.Clamp((int)worldPos.y, 0, sizeY - 1);
            int z = Mathf.Clamp((int)worldPos.z, 0, sizeZ - 1);
            
            return velocityPrev[x, y, z];
        }

        /// <summary>
        /// Adiciona valor ao campo de peso numa posição específica
        /// </summary>
        public void AddWeight(int x, int y, int z, float w)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                weight[x, y, z] += w;
        }

        /// <summary>
        /// Obtém o valor do campo de peso numa posição específica
        /// </summary>
        public float GetWeight(int x, int y, int z)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                return weight[x, y, z];
            return 0f;
        }

        /// <summary>
        /// Define o valor do campo de peso numa posição específica
        /// </summary>
        public void SetWeight(int x, int y, int z, float w)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                weight[x, y, z] = w;
        }

        // Métodos existentes para densidade e velocidade

        /// <summary>
        /// Adiciona valor ao campo de densidade numa posição específica
        /// </summary>
        public void AddDensity(int x, int y, int z, float dens)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                density[x, y, z] += dens;
        }

        /// <summary>
        /// Adiciona valor ao campo de velocidade numa posição específica
        /// </summary>
        public void AddVelocity(int x, int y, int z, Vector3 vel)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                velocity[x, y, z] += vel;
        }

        /// <summary>
        /// Obtém o valor do campo de densidade numa posição específica
        /// </summary>
        public float GetDensity(int x, int y, int z)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                return density[x, y, z];
            return 0f;
        }

        /// <summary>
        /// Obtém o valor do campo de velocidade numa posição específica
        /// </summary>
        public Vector3 GetVelocity(int x, int y, int z)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                return velocity[x, y, z];
            return Vector3.zero;
        }

        /// <summary>
        /// Obtém as dimensões da grelha
        /// </summary>
        /// <returns>Vector3Int com tamanho (X, Y, Z)</returns>
        public Vector3Int GetGridSize() => new Vector3Int(sizeX, sizeY, sizeZ);

        /// <summary>
        /// Define o valor do campo de velocidade numa posição específica
        /// </summary>
        public void SetVelocity(int x, int y, int z, Vector3 vel)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                velocity[x, y, z] = vel;
        }

        /// <summary>
        /// Define o valor do campo de densidade numa posição específica
        /// </summary>
        public void SetDensity(int x, int y, int z, float dens)
        {
            if (x >= 0 && x < sizeX && y >= 0 && y < sizeY && z >= 0 && z < sizeZ)
                density[x, y, z] = dens;
        }
    }
}