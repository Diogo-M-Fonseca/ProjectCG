# ProjectCG – Simulação de Fluidos

Este projeto tem como objetivo a implementação de uma simulação de fluidos em Unity, recorrendo ao método **semi-Lagrangiano**, com foco na estabilidade, desempenho e realismo visual da simulação.

## Autores

- **Diogo Fonseca** – a22402652
- **Miguel Filipe** – a22408872

---

## Obstáculo 1 – 3D vs. 2D

Para iniciar o projeto, era necessário escolher a dimensionalidade da simulação. Embora a maioria dos tutoriais, explicações e teses disponíveis online comecem com simulações 2D, pois estas englobam todos os conceitos fundamentais e, segundo algumas opiniões, são mais simples de implementar e visualizar, optámos por desenvolver a simulação diretamente em 3D. Seguir estritamente os tutoriais existentes contrariaria o propósito académico do projeto. Além disso, considerámos que uma simulação 3D seria mais desafiadora e interessante de realizar.

## Obstáculo 2 – Geração de partículas

Para a geração das partículas, optou-se pela criação de **octaedros**, definidos através dos seus vértices, arestas e faces. A construção destas partículas foi realizada através do script **MeshGenerator**, responsável pela geração da *mesh* utilizada. Este script inclui um parâmetro configurável no editor do Unity que permite ajustar a resolução do polígono.

O preenchimento das faces foi realizado com o método **CreateFace**, desenvolvido para construir as superfícies do octaedro. A sua implementação foi baseada num trabalho de Sebastian Lague, sendo essencial para o processo.

Adicionalmente, foi desenvolvido um **shader** para alterar a cor das partículas em função da sua direção e velocidade, com o intuito de enriquecer o aspeto visual. Contudo, este efeito não se encontra totalmente funcional neste momento, pois depende da implementação completa da grelha de simulação.

### Problemas encontrados

- Foi necessário realizar múltiplos ajustes na quantidade de partículas simuladas para equilibrar o desempenho e a estabilidade.
- O método **CreateFace**, embora eficaz, exigiu uma adaptação ao nosso contexto.

---

## Obstáculo 3 – A grelha

Para suportar o movimento das partículas, implementou-se uma grelha tridimensional através do script **FluidGrid3D**, que aproxima numericamente a equação de **Navier-Stokes**. Este sistema calcula os processos de **advecção** e os campos de **densidade**, **velocidade**, **divergência** e **gradiente de pressão**.

A resolução da **equação de Poisson** para o cálculo da pressão foi efetuada utilizando o **método iterativo de Jacobi**. Após os cálculos, recorreu-se à **interpolação trilinear** para amostrar os campos de densidade e velocidade, garantindo maior estabilidade na abordagem semi-Lagrangiana.

### Problemas encontrados

- A implementação inicial da gravidade e dos limites da grelha fazia com que as partículas fossem teleportadas para o interior da mesma.
- A ausência de um sistema de colisões dificultou a validação da correção dos cálculos.
- Sem colisões, a advecção tendia a acumular as partículas nos cantos da grelha, em vez de produzir um comportamento fluido natural.

---

## Obstáculo 4 – Colisões

A implementação de colisões permitiu introduzir o conceito de **viscosidade**, completando, de forma simplificada, a equação de Navier-Stokes. Para este fim, utilizou-se o método **Smoothed Particle Hydrodynamics (SPH)**, que simula interações diretas entre partículas, complementando a influência da grelha.

Para otimizar o desempenho, foi implementada uma **grelha espacial**, reduzindo a complexidade computacional das interações de **O(n²)** para **O(n)**.

### Problemas encontrados

- Observaram-se comportamentos instáveis das partículas, tais como:
    - Escalagem de superfícies verticais;
    - Vibrações excessivas;
    - Projeção para fora dos limites da simulação;
    - Teleporte de volta para o interior de colisões;
    - Ignorar a gravidade e colisões;
    - Agrupamento instável de partículas.
- Mesmo após extensivos ajustes dos parâmetros, o fluido não atingia um estado de repouso estável, como seria expectável.

---

## Obstáculo 5 – Perda de energia

Após múltiplas reimplementações, concluiu-se que, independentemente dos parâmetros utilizados, o fluido não atingia um estado estável. A redução da força das colisões resultava na sobreposição das partículas, enquanto forças de repulsão mais elevadas mantinham o sistema em movimento constante.

Numa tentativa de estabilização, substituiu-se o método de integração **Euler** por **Verlet**, com o objetivo de reduzir a instabilidade numérica. Foram também introduzidas variáveis como **SurfaceTension** e **energyDissipationRate** no método **UpdateParticleSPH**, bem como uma perda de energia adicional durante as colisões.

### Problemas encontrados

- O desempenho da simulação degradou-se significativamente além das duas mil partículas.
- A introdução de mecanismos de dissipação de energia não resolveu o problema de instabilidade, levando à sua posterior remoção.

---

## Obstáculo 6 – Desconexão de grelhas

Foi adicionado um "gizmo" para visualizar a grelha do script **FluidGrid3D** e as suas células. Através desta visualização, observou-se que algumas partículas criadas fora da grelha se comportavam de forma idêntica às criadas no seu interior, o que suscitou as seguintes hipóteses:

1. A grelha não comunica corretamente com as partículas, sendo indiferente à sua localização.
2. Existe mais do que uma grelha, e o gizmo apenas desenha uma delas.
3. O gizmo não está a ser renderizado corretamente.

### Problemas encontrados

- As partículas comportam-se de forma semelhante dentro e fora da grelha visualizada.
- Com menos de três mil partículas, estas parecem ficar confinadas a um plano invisível, em vez de se distribuírem ao longo dos eixos X e Z da grelha.

---

## Obstáculo 7 – Método PIC/FLIP e *compute shaders*

Para que a simulação fosse verdadeiramente semi-Lagrangiana, implementou-se o método **PIC/FLIP**, criando um híbrido com **SPH** para uma sinergia adequada entre a grelha e as partículas. Devido a problemas de desempenho, foi necessário recorrer a dois *compute shaders*: um para os cálculos SPH e outro para os cálculos da grelha.

Com a migração dos cálculos para os *compute shaders*, a simulação estabilizou significativamente e os problemas de *frame rate* foram mitigados.

### Problemas encontrados

- As partículas, que anteriormente exibiam um comportamento fluido estável, passaram a flutuar ou a escalar as paredes.
- Após o ajuste de alguns parâmetros, as partículas passaram a cair como areia e a acumular-se no fundo da simulação.

---

## Obstáculo 8 – SPH vs. PIC/FLIP

O método **SPH** (*Smoothed-particle hydrodynamics*) simula o comportamento do fluido através de interações diretas partícula-partícula. O método **PIC/FLIP**, por sua vez, baseia-se na troca de informação grelha-partícula. A coexistência de ambos é possível, mas delicada, pois a sobreposição de cálculos pode resultar em valores incorretos para as partículas. Tentámos reduzir a influência do SPH, mas os resultados não foram satisfatórios.

### Problemas encontrados

- As partículas exibiam comportamentos incorretos e extremos: eram criadas sem velocidade ou efeito da gravidade, explodiam com velocidades absurdas ou não atingiam um estado de repouso.
- A alteração de parâmetros tinha um efeito mínimo ou nulo nestes casos.

---

## Obstáculo 9 – *Kernels* e *buffers* em *compute shaders*

*Kernels* e *buffers* são elementos essenciais para o funcionamento de um *compute shader*. *Kernels* são métodos executados em paralelo na GPU, enquanto *buffers* são blocos de memória da mesma. A sua definição no *shader* é simples, a dificuldade reside na sua correta utilização a partir do script **ParticleManager** que controla a simulação. Encontrámos diversos erros relacionados com *kernels* não encontrados e *buffers* mal utilizados.

### Problemas encontrados

- As partículas eram criadas, mas não se moviam, pois os valores calculados pelos *kernels* não lhes eram aplicados.
- *Kernels* não eram encontrados, apesar de se usar o nome/índice correto.
- Erros relacionados com *buffers* apareciam brevemente na consola, dificultando o diagnóstico.

---

## Conclusão

Torna-se evidente que a maioria dos tutoriais começa com simulações 2D por uma boa razão. Tivemos de assimilar os conceitos de programação e matemáticos utilizados no projeto enquanto lidávamos com a complexidade adicional de o fazer funcionar em três dimensões. Se tivéssemos começado com uma simulação 2D, poderíamos ter-nos focado mais na procura dos conceitos essenciais e, só posteriormente, trocado a simulação para 3D.

Reconhecemos também que seria benéfico ter uma compreensão mais profunda dos conceitos teóricos antes de iniciar a implementação. No entanto, este projeto revelou-se um excelente meio de aprendizagem. Cada obstáculo forçou-nos a pesquisar e a experimentar com conceitos que, até então, eram simplesmente teóricos. Embora não nos tenhamos tornado especialistas na área, saímos deste projeto com um entendimento significativamente mais amplo sobre simulação e física de fluidos.

A partir de determinado ponto, não foi possível manter uma simulação funcional que reproduzisse o comportamento correto. Existem, atualmente, dois protótipos: um mais simples mas estável, e outro mais completo mas instável. Os seus comportamentos são os seguintes:

*   **Protótipo Antigo (SPH + Grelha):**
    *   As partículas caem até ao fundo da grelha.
    *   Após impactarem no fundo, salpicam brevemente até entrarem em repouso.
    *   Em repouso, formam uma superfície estável.
    *   A alteração dos diversos parâmetros afeta significativamente a simulação, podendo gerar instabilidade.

*   **Protótipo Novo (Compute Shaders + FLIP):**
    *   As partículas caem até ao fundo da grelha.
    *   Ao chegarem ao fundo, entram em repouso quase instantaneamente.
    *   Toda a lógica física está implementada em *compute shaders*.
    *   Os parâmetros têm um efeito limitado na simulação, indicando um problema subjacente na comunicação com os *kernels*/*buffers*.

### Análise do Protótipo Antigo

A simulação apresenta um comportamento funcional e visualmente coerente com cerca de **duas mil partículas**. Contudo, ao aumentar este número, a densidade do sistema cresce, gerando instabilidades – um problema comum em implementações de **SPH**. Neste protótipo, o SPH calcula as interações locais (pressão e viscosidade), a grelha resolve a incompressibilidade e os resultados são retornados às partículas. Foram tentadas limitações artificiais da densidade máxima sem sucesso. Ajustes na densidade base e no multiplicador de pressão permitiram simular mais partículas com relativa estabilidade, mas à custa de uma redução significativa no desempenho (FPS).

### Análise do Protótipo Novo

A simulação apresenta um comportamento disfuncional para qualquer número de partículas, apesar de integrar *compute shaders* e o método **FLIP**, constituindo a versão mais próxima do método semi-Lagrangiano que conseguimos atingir. Várias tentativas de reimplementação e ajuste de parâmetros não resultaram numa simulação estável e correta. Por outro lado, os FPS mantêm-se elevados, possivelmente porque os *compute shaders* estão a executar eficientemente, mas a lógica de colisões ou a transferência de dados pode estar incorreta, um problema que não conseguimos identificar definitivamente.

## Notas Finais

Embora não tenhamos conseguido desenvolver um protótipo totalmente funcional que ultrapassasse todos os obstáculos, o processo de implementação e a obtenção de simulações parcialmente funcionais permitiram-nos compreender a investigação necessária e as dificuldades práticas ao desenvolvimento de uma simulação de fluidos. O projeto constituiu uma valiosa experiência que nos levou a aprender sobre os desafios da computação gráfica aplicada à simulação física.

---

## Fontes

- https://pages.cs.wisc.edu/~chaol/data/cs777/stam-stable_fluids.pdf - Stable Fluids (1999) - Jos Stam
- https://www.youtube.com/watch?v=rSKMYc1CQHE - Simulating Fluids - Sebastian Lague
- https://shahriyarshahrabi.medium.com/gentle-introduction-to-fluid-simulation-for-programmers-and-technical-artists-7c0045c40bac - Gentle Introduction to Realtime Fluid Simulation for Programmers and Technical Artists - Shahriar Shahrabi
- https://nccastaff.bournemouth.ac.uk/jmacey/MastersProject/MSc22/09/Thesis.pdf - Real-Time Multiple Fluid Simulation for Games - Jacob Worgan
- https://graphics.cs.cmu.edu/nsp/course/15464-s20/www/lectures/BasicFluids.pdf - FLUID SIMULATION - Robert Bridson, UBC Matthias Müller-Fischer, AGEIA Inc.
- https://www.youtube.com/watch?v=XmzBREkK8kY - 18 - How to write a FLIP water/fluid simulation to run in your browser. - Ten Minute Physics