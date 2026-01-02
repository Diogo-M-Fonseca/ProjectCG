# ProjectCG – Simulação de Fluidos

Este projeto tem como objetivo a implementação de uma simulação de fluidos em Unity, recorrendo ao método **semi-Lagrangiano**, com foco na estabilidade, desempenho e realismo visual da simulação.

## Autores

- **Diogo Fonseca** – a22402652  
- **Miguel Filipe** – a22408872  

---

## Obstáculo 1 – Geração de partículas

Para a geração das partículas, optou-se pela criação de **octaedros**, definidos através dos seus vértices, arestas e faces. A construção destas partículas foi realizada através do script **MeshGenerator**, responsável pela geração da *mesh* utilizada pelas partículas. Este script inclui ainda um parâmetro configurável no editor do Unity, permitindo ajustar a resolução do polígono.

O preenchimento das faces dos polígonos foi realizado com o método **CreateFace**, que tem como propósito a construção das superfícies do octaedro.

Adicionalmente, foi desenvolvido um **shader** responsável por alterar a cor das partículas em função da sua direcção e velocidade, de forma a enriquecer o aspecto visual da simulação. No entanto, este efeito não se encontra totalmente funcional no momento de criação, uma vez que dependia da implementação completa da grelha de simulação.

### Problemas encontrados

- Necessidade de múltiplos ajustes na quantidade de partículas simuladas, de forma a equilibrar desempenho e estabilidade.
- A utilização do método **CreateFace**, baseado numa implementação de Sebastian Lague, foi essencial para o preenchimento das faces dos polígonos.

---

## Obstáculo 2 – A grelha

Para permitir o movimento das partículas, foi implementada uma grelha tridimensional através do script **FluidGrid3D**, que aproxima a equação de **Navier-Stokes**. Este sistema calcula a **advecção**, bem como os campos de **densidade**, **velocidade** desta, tal como a **divergência** e **gradiente da pressão**.

A resolução da **equação de Poisson** para o cálculo da pressão foi realizada utilizando o **método iterativo de Jacobi**. Após os cálculos, recorreu-se à **interpolação trilinear** para amostrar os campos de densidade e velocidade, garantindo maior estabilidade na simulação semi-Lagrangiana.

### Problemas encontrados

- A implementação inicial da gravidade e dos limites da grelha resultava no teleporte das partículas para o interior da grelha.
- A ausência de colisões dificultou a validação da correção dos cálculos efetuados.
- Sem colisões, a advecção tendia a atrair as partículas para os cantos da grelha, causando acumulação excessiva em vez de um comportamento de fluido natural.

---

## Obstáculo 3 – Colisão

A implementação de colisões permitiu introduzir o conceito de **viscosidade**, completando, ainda que de forma simplificada, a equação de Navier-Stokes. Para este efeito, foi utilizado o método **Smoothed Particle Hydrodynamics (SPH)**, permitindo simular as interações entre partículas de forma direta, em vez de depender exclusivamente da grelha.

Para optimização do desempenho, foi implementada uma **grelha espacial**, reduzindo a complexidade do cálculo de interacções de **O(n²)** para **O(n)**.

### Problemas encontrados

- Comportamentos instáveis das partículas, tais como:
  - Escalar superfícies verticais;
  - Vibrações excessivas;
  - Projeção para fora dos limites da simulação;
  - Teleportes de volta para dentro das colisões;
  - Ignorar gravidade e colisões;
  - Agrupamento de partículas.
- Mesmo após ajustes extensivos dos parâmetros, o fluido não apresentava um estado de repouso estável, como esperado num fluido real.

---

## Obstáculo 4 – Perda de energia

Após múltiplas reimplementações do mesmo script, concluiu-se que, independentemente dos parâmetros utilizados, o fluido não atingia um estado estável. A redução da força das colisões resultava na sobreposição das partículas, enquanto forças de repulsão mais elevadas mantinham o sistema em constante movimento.

Como tentativa de estabilização, substituiu-se a integração **Euler** por **Verlet**, com o objectivo de reduzir a instabilidade numérica. Foram ainda introduzidas variáveis adicionais, como **SurfaceTension** e **energyDissipationRate**, aplicadas no método *UpdateParticleSPH*, bem como perda adicional de energia durante colisões entre partículas.

### Problemas encontrados

- O desempenho da simulação degradou-se significativamente a partir de aproximadamente duas mil partículas.
- A introdução de mecanismos de dissipação de energia não resolveu o problema de instabilidade, levando à remoção destas variáveis.

---

## Conclusão

A simulação apresenta um comportamento funcional e visualmente coerente com cerca de **duas mil partículas**. Contudo, ao aumentar este número, a densidade do sistema cresce significativamente, originando instabilidades, que, após alguma pesquisa, verifica-se ser um problema comum em implementações de **Smoothed Particle Hydrodynamics**.

Foram exploradas tentativas de limitar artificialmente a densidade máxima, sem sucesso. No entanto, ao aumentar a densidade base e reduzir o multiplicador de pressão, foi possível simular um maior número de partículas com relativa estabilidade. Ainda assim, esta abordagem teve um impacto negativo no desempenho, resultando numa diminuição considerável de FPS.

---

## Fontes

