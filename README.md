# ProjectCG – Simulação de Fluidos

Este projeto tem como objetivo a implementação de uma simulação de fluidos em Unity, recorrendo ao método **semi-Lagrangiano**, com foco na estabilidade, desempenho e realismo visual da simulação.

## Autores

- **Diogo Fonseca** – a22402652  
- **Miguel Filipe** – a22408872  

---

## Obstaculo 1 - 3D vs 2D

Para começarmos a trabalhar no projecto tinhamos de escolher primeiro em que dimensões queriamos simular o nosso fluido, uma simulação 2D parece ser o ponto inicial de todos os tutoriais, explicações e téses que se encontra na net, isto porque de facto uma simulação 2D inclui todos os conceitos base de uma simulação de fluidos e segundo a opinião de alguns é mais simples de implementar e visualizar, porém decidimos fazer a simulação diretamente em 3D, fazer as coisas exatamente como nesses tutoriais téses etc derrotaria o proposito deste projecto, para além de que uma simulação 3D parece mais divertida de se fazer, em parte também devido á sua suposta dificuldade extra.

## Obstáculo 2 – Geração de partículas

Para a geração das partículas, optou-se pela criação de **octaedros**, definidos através dos seus vértices, arestas e faces. A construção destas partículas foi realizada através do script **MeshGenerator**, responsável pela geração da *mesh* utilizada pelas partículas. Este script inclui ainda um parâmetro configurável no editor do Unity, permitindo ajustar a resolução do polígono.

O preenchimento das faces dos polígonos foi realizado com o método **CreateFace**, que tem como propósito a construção das superfícies do octaedro.

Adicionalmente, foi desenvolvido um **shader** responsável por alterar a cor das partículas em função da sua direção e velocidade, de forma a enriquecer o aspecto visual da simulação. No entanto, este efeito não se encontra totalmente funcional no momento de criação, uma vez que dependia da implementação completa da grelha de simulação.

### Problemas encontrados

- Necessidade de múltiplos ajustes na quantidade de partículas simuladas, de forma a equilibrar desempenho e estabilidade.
- A utilização do método **CreateFace**, baseado numa implementação de Sebastian Lague, foi essencial para o preenchimento das faces dos polígonos.

---

## Obstáculo 3 – A grelha

Para permitir o movimento das partículas, foi implementada uma grelha tridimensional através do script **FluidGrid3D**, que aproxima a equação de **Navier-Stokes**. Este sistema calcula a **advecção**, bem como os campos de **densidade**, **velocidade** desta, tal como a **divergência** e **gradiente da pressão**.

A resolução da **equação de Poisson** para o cálculo da pressão foi realizada utilizando o **método iterativo de Jacobi**. Após os cálculos, recorreu-se à **interpolação trilinear** para amostrar os campos de densidade e velocidade, garantindo maior estabilidade na simulação semi-Lagrangiana.

### Problemas encontrados

- A implementação inicial da gravidade e dos limites da grelha resultava no teleporte das partículas para o interior da grelha.
- A ausência de colisões dificultou a validação da correção dos cálculos efetuados.
- Sem colisões, a advecção tendia a atrair as partículas para os cantos da grelha, causando acumulação excessiva em vez de um comportamento de fluido natural.

---

## Obstáculo 4 – Colisão

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

## Obstáculo 5 – Perda de energia

Após múltiplas reimplementações do mesmo script, concluiu-se que, independentemente dos parâmetros utilizados, o fluido não atingia um estado estável. A redução da força das colisões resultava na sobreposição das partículas, enquanto forças de repulsão mais elevadas mantinham o sistema em constante movimento.

Como tentativa de estabilização, substituiu-se a integração **Euler** por **Verlet**, com o objectivo de reduzir a instabilidade numérica. Foram ainda introduzidas variáveis adicionais, como **SurfaceTension** e **energyDissipationRate**, aplicadas no método **UpdateParticleSPH**, bem como perda adicional de energia durante colisões entre partículas.

### Problemas encontrados

- O desempenho da simulação degradou-se significativamente a partir de aproximadamente duas mil partículas.

- A introdução de mecanismos de dissipação de energia não resolveu o problema de instabilidade, levando à remoção destas variáveis.

---

## Obstaculo 6 - Disconexão de Grids

Foi adicionada um "gizmo" para ser possivel a visualização da Grid do script **FluidGrid3D** e das cells da mesma, atrás desse "gizmo" foi possivel observar que algumas particulas eram criadas fora da Grid mas comportavam-se de igual forma ás que eram criadas dentro da grid, isto cria algumas teorias sobre o que possivelmente poderia estar a acontecer.

Possibilidade 1 - A grid não está a comunicar correctamente com as particulas sendo indifrente se as particulas são criadas dentro ou fora dela.

Possibilidade 2 - Existe mais que uma grid e o gizmo só está a desenhar uma delas, neste caso o que faz mais sentido é apagar a grid que não queremos usar.

Possibilidade 3 - O gizmo não está bem feito.

### Problemas encontrados

- As particulas parecem comportar-se de igual forma dentro ou fora da grid visualizada com o gizmo.

- Quando um numero inferior a 3 mil particulas é criado as particulas parecem ficar "presas" a um plano inexistente em vez de se espalharem pelo comprimento todo do x e do z da grid.

---

## Obstáculo 7 - Método PIC/FLIP e compute shaders

Para a simulação ser verdadeiramente semi-Lagrangiana, implementou-se o método PIC/FLIP para a sinergia necessária entre a grelha e as partículas ser possível, tendo um híbrido de SPH-FLIP. No entanto, por problemas de performance, mesmo implementando o método SPH, é necessário o uso de dois compute shaders, um para o calculo SPH e outro para a própria grelha.

Com a passagem das variáveis para os compute shaders, a simulação estabilizou bastante, a perda de FPS deixou de ser um problema.

### Problemas encontrados

- As partículas que previamente comportavam-se como fluidos, estabilizando no fundo da simulação com certos parâmetros definidos (após o obstáculo 4), estavam agora a flutuar ou a escalar as paredes mais uma vez.

- Após trocar alguns parâmetros, as partículas apenas caem como areia e juntam-se no fundo da simulação

---

## Obstáculo 8 - SPH vs PIC/FlIP

SPH ou Smoothed-particle hydrodinamics é um dos metodos utilizados na computação grafica para simular o comportamento de agua através de particulas, ou seja são "contas" que passam informação particula-particula, enquanto que PIC/FLIP são "contas" que passam informação de grid-particula, dito isto a coexistencia de ambos é possivel mas bastante delicada, se ambos por algum desleixo fizerem a mesma conta as particulas acabam com valores extremamente incorrectos, sabendo disto tentamos retirar a influencia do SPH, mas os resultados não foram de todo satisfatórios.

### problemas encontrados

- As particulas estavam a comportarse de manerias muito incorrectas, desde serem criadas sem velocidades ou efeitos de gravidade até explodirem com diversas velocidades difrentes ou absurdas e nunca chegarem a um ponto de reposo.

- Trocar Parâmetros pouco ou nada afetava as particulas nestes casos extremos.

---

## Obstáculo 9 - Kernels e Buffers em compute shaders

Kernels e Buffers são essenciais para o funcionamento correcto de um compute shader, kernels são metodos executados em paralelo pelas threads do GPU enquanto que os buffers são pedaços de memória do GPU, defenir um buffer ou um kernel no compute shader não é um problema, o problema é saber usalos através do script C# que controla a simulação toda, tivemos diversos problemas e erros relativos a kernels que não eram encontrados e buffers que eram utilizados de maneira errada.

### problemas encontrados

- Particulas são criadas mas não se mexem pois os valores calculados pelos kernels não estão a ser aplicados a elas.

- Kernels não estão a ser encontrados apesar de ultizar o indice/nome correcto.

- Erros relacionados a buffers aparecem na consola por breves instantes mas desaparecem antes que possamos perceber exatamanete o que os causa.

---

## Conclusão

Claramente maior parte dos tutorias e téses começam por criar uma simulação 2D por bom motivo, tivemos que aprender os conceitos tanto de programação quanto matemáticos deste projecto enquanto tinhamos que lidar com a complexidade extra de fazer a simulação funcionar em 3 dimensões, se tivessemos começado com uma simulação 2D poderiamos focarnos mais em absorver os conceitos essenciais para este trabalho e só depois se sobrasse tempo ter passado a simulação para 3D.

É também obvio agora que deveriamos ter uma compreensão maior dos conceitos necessários para fazer este tipo de simulação antes de começar a faze-la, dito isso este projecto foi um professor excelente, todas as paredes e obstaculos em que batemos forçou-nos a pesquisar e experimentar com os conceitos que até então eram só teoricos, de maneira nenhuma saimos deste projecto "experts" no assunto mas definitivamente saimos deste projecto com um entendimento maior sobre simulação de fluidos e fisica de fluidos no geral.

A partir de certo ponto, não foi possível manter a simulação capaz de reproduzir o comportamento correto, ou seja, existem de momento dois protótipos de simulação de fluídos onde uma se mantém incompleta mas estável e outra mais completa mas instável, cujos comportamentos são os seguintes:

- Simulação Velha (old)
  - As particulas caem até o fundo da grid.
  - Após chegarem ao fundo da grid saltam/salpicam por um pouco até entrarem em repouso.
  - Quando estão em repouso criam uma superficie estável.
  - Alterar os diversos parametros afeta bastante o que acontece na simulação, podendo criar situações instáveis.


- Simulação Nova (Compute)
  - As particulas caem até o fundo da grid
  - Após chegarem ao fundo da grid entram em repouso instantaneamente
  - Toda a logica fisica da simulação está em compute shaders
  - Os parametros pouco afetam o que acontece na simulação devido a um problema com os kernels/buffers

### Protótipo velho:

- A simulação apresenta um comportamento funcional e visualmente coerente com cerca de **duas mil partículas**. Contudo, ao aumentar este número, a densidade do sistema cresce significativamente, originando instabilidades, que, após alguma pesquisa, verifica-se ser um problema comum em implementações de **Smoothed Particle Hydrodynamics**.

- Desta forma, o SPH calcula as interações locais das partículas (pressão e viscosidade), envia para a grelha que resolve a incompressibilidade e retorna para as partículas com os seus parâmetros.

- Foram exploradas tentativas de limitar artificialmente a densidade máxima, sem sucesso. No entanto, ao aumentar a densidade base e reduzir o multiplicador de pressão, foi possível simular um maior número de partículas com relativa estabilidade. Ainda assim, esta abordagem teve um impacto negativo no desempenho, resultando numa diminuição considerável de FPS.

### Protótipo novo:

- A simulação apresenta um comportamento disfuncional a qualquer número de partículas apesar da implementação de cada um dos parâmetros anteriores, este encontra-se mais avançado pois já contem compute shaders e o metódo FLIP que consiste nas interações grelha-particula e particula-particula, sendo a versão mais aproximada do método semi-Lagrangiano que obtemos.

- Foram exploradas várias tentativas de reimplementação e alterações de parâmetro para procurar algo estável e correto, sem sucesso.

- Por outro lado, os FPS não estão a sofrer qualquer tipo de redução, talvez porque algo está errado com a implementação de colisões ou os compute shaders estão de fato a fazer o seu trabalho, o que, por muitas reimplementações e testes que foram executados, não fomos capazes de encontrar o problema para o comportamento das partículas.

## Últimas notas

- Embora não tenhamos conseguido desenvolver um protótipo totalmente funcional, com todos os obstáculos ultrapassados, a conclusão da sua implementação e a obtenção de uma simulação funcional até determinado ponto permitiram-nos compreender não só a pesquisa necessária para o seu desenvolvimento, mas também a dificuldade enfrentada durante a sua execução.

---

## Fontes

- https://pages.cs.wisc.edu/~chaol/data/cs777/stam-stable_fluids.pdf            - Stable Fluids (1999) - Jos Stam

- https://www.youtube.com/watch?v=rSKMYc1CQHE                                   - Simulating Fluids -  Sebastian Lague

- https://shahriyarshahrabi.medium.com/gentle-introduction-to-fluid-simulation-for-programmers-and-technical-artists-7c0045c40bac - Gentle Introduction to Realtime Fluid Simulation for Programmers and Technical Artists - Shahriar Shahrabi

- https://nccastaff.bournemouth.ac.uk/jmacey/MastersProject/MSc22/09/Thesis.pdf - Real-Time Multiple Fluid Simulation for Games - Jacob Worgan

- https://graphics.cs.cmu.edu/nsp/course/15464-s20/www/lectures/BasicFluids.pdf - FLUID SIMULATION - Robert Bridson, UBC Matthias Müller-Fischer, AGEIA Inc.

- https://www.youtube.com/watch?v=XmzBREkK8kY                                   - 18 - How to write a FLIP water/fluid simulation to run in your browser.                                                                - Ten Minute Physics




