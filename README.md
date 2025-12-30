# ProjectCG - Simulação de fluidos

Este projeto consiste na simulação de fluidos semi-lagrangiana através do Unity.

## Nomes

- Diogo Fonseca a22402652
- Miguel Filipe a22408872

## Obstáculo 1 - Geração de partículas

- Para a geração das partículas, optámos pela criação de octaedros ao criar os seus vértices, arestas e preenchê-los. A sua criação envolve os métodos **MeshGenerator**, que nos permitem gerar a mesh que as partículas usam, mas também tem o seu próprio parâmetro ajustável para aumentar a resolução destas acessível no próprio editor do Unity, e **CreateFace** que tem a responsabilidade de preencher os lados dos polígonos gerados por tal.

- A criação de um shader que muda a cor das partículas dependendo da direção e velocidade foi também criado para ter algum efeito visual, ainda não funcional pois a grelha não estava pronta.

### Problemas encontrados:

- Foram necessários vários ajustes à quantidade de partículas simuladas.
- Utilização do método **CreateFace** feito por Sebastian Lague para o preenchimento dos polígonos teve de ser utilizado.

## Obstáculo 2 - A grelha

As partículas, para se movimentarem, precisam de ter forças aplicadas. Para tal acontecer, temos o script FluidGrid3D que nos permite aproximar-nos um pouco mais da equação de Navier-Stokes, calculando a advecção e, por sua vez, a densidade e velocidade desta bem como a divergência e o gradiente da pressão, calculado através do **método de Jacobi** para resolver a **equação de Poisson**.

Após os cálculos, através de interpolação trilinear, dá-nos uma amostra do campo de densidade e da velocidade que será necessário para manter a estabilidade da simulação.

### Problemas encontrados:

- Da maneira como a gravidade e os limites da grelha estavam implementados, causava apenas com que as partículas teleportassem de volta para dentro da grelha (a gravidade foi eventualmente trocada por flutuabilidade ou "buoyancy" e os teleportes removidos através de um cálculo que simula colisão com as paredes da simulação).
- Alguma incapacidade de testar se os cálculos estavam corretos devido a não haver próprias colisões ainda criadas entre partículas.
- Devido à falta de colisões, a advecção estava a puxar as partículas para os cantos da simulação como um íman, causando com que acumulem em vez de apenas se espalharem ao longo do fundo.

## Obstáculo 3 - Colisão

Com a implementação de colisões, vem a implementação da viscosidade, completando a equação de Navier-Stokes(mesmo que simplificada). Utilizou-se tambem SPH (Smoothed Particle Hydrodynamics) para simular a própria partícula em vez de grelha para a aplicação da viscosidade tal como das suas colisões e uma grelha espacial para otimização (O(n) em vez de O(n²)).


### Problemas encontrados:

- A certas alturas as partículas tinham os seguintes comportamentos: Escalavam paredes, tremiam, eram projetadas para fora e teleportadas de volta, ignoravam as leis da gravidade, ignoravam as colisões e juntavam-se.
- Mesmo depois da implementação de todos os parâmetros, as partículas não mantêm paradas como fluidos.










# Fontes


