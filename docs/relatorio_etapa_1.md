# Relatório de Entrega — Etapa 1

**Projeto "Mergulho Virtual" — Núcleo do Aplicativo AR e Reconhecimento**

---

## 1. Identificação

| | |
|---|---|
| **Contratada** | GUILHERME PINTO FICKEL DESENVOLVIMENTO DE SOFTWARE LTDA |
| **CNPJ** | 58.187.905/0001-00 |
| **Projeto** | Mergulho Virtual — App Android AR + Sistema de Backend |
| **Documento de referência** | Proposta de Prestação de Serviço de 03 de junho de 2026 |
| **Etapa** | Etapa 1 — Núcleo do Aplicativo AR e Reconhecimento |
| **Marco de pagamento vinculado** | 30% do valor total — R$ 7.500,00 |
| **Data do relatório** | 28 de setembro de 2026 |

---

## 2. Objetivo deste relatório

Este documento apresenta, para validação da Contratante, os entregáveis da **Etapa 1** definidos no item 4 da Proposta de Prestação de Serviço. Para cada entregável contratado, descreve-se o que foi implementado, como foi implementado e como a Contratante pode verificá-lo.

Ao final, são listadas as **entregas adicionais** realizadas nesta etapa — funcionalidades que excedem o escopo mínimo contratado para o Mês 1 — e os próximos passos referentes à Etapa 2.

---

## 3. Resumo executivo

**Todos os entregáveis da Etapa 1 foram concluídos e encontram-se operacionais em dispositivo Android real.**

O aplicativo "Mergulho Virtual" já executa, em um único fluxo contínuo:

1. Inicia a sessão de Realidade Aumentada sobre a câmera do dispositivo;
2. Obtém a posição por GPS e identifica **automaticamente e sem internet** em qual das 17 praias de Fernando de Noronha o usuário se encontra;
3. Processa o feed da câmera com **inteligência artificial executada no próprio aparelho** (sem enviar imagens a servidores), reconhecendo tanto categorias visuais gerais quanto, especificamente, se a câmera está apontada para o mar;
4. Posiciona em AR os **modelos 3D de tubarões correspondentes àquela praia**, animados, respondendo ao toque do usuário;
5. Alterna entre as telas do aplicativo (abertura, AR e navegação inferior) gerenciando automaticamente consumo de bateria e desempenho.

Dois pontos merecem destaque por representarem entrega acima do contratado para esta etapa:

- A proposta previa **pelo menos um** modelo 3D em AR. Foram entregues **cinco espécies** completas (Tubarão-martelo, Tubarão-tigre, Tubarão-limão, Tubarão-lixa e Tubarão-de-recife), com animação, escala real e distribuição por praia.
- Foi desenvolvido um **detector de mar por visão computacional sem necessidade de treinamento ou base de dados**, capaz de identificar quando a câmera está voltada para o oceano. Este componente não constava do escopo e abre caminho para ativar o conteúdo de AR apenas no contexto correto.

---

## 4. Quadro-resumo dos entregáveis

| # | Entregável previsto na Proposta (item 4, Etapa 1) | Situação |
|---|---|---|
| 1 | Projeto Unity configurado (AR Foundation / ARCore) rodando em dispositivo Android | **Concluído** |
| 2 | Classificação de imagem on-device (ONNX) operando sobre o feed da câmera | **Concluído** |
| 3 | Localização por GPS + geocodificação reversa das praias de Noronha | **Concluído** |
| 4 | Posicionamento de pelo menos **um** modelo 3D em AR com interação por toque | **Concluído — 5 espécies entregues** |
| 5 | Navegação base entre telas (Splash, AR/HUD, barra inferior) | **Concluído** |

---

## 5. Detalhamento dos entregáveis

### 5.1. Projeto Unity com AR Foundation / ARCore em dispositivo Android

**Situação: concluído.**

Base técnica configurada, versionada e reproduzível:

| Componente | Versão adotada |
|---|---|
| Unity | 6000.3.14f1 (Unity 6) |
| Pipeline de renderização | Universal Render Pipeline (URP) 17.3.0 |
| AR Foundation | 6.3.4 |
| Provedor AR Android (ARCore) | 6.3.4 |
| Sistema de entrada | Input System 1.19.0 |
| Android mínimo suportado | API 30 (Android 11) |
| Identificador do aplicativo | `dev.mergulhovirtual` |

Decisões de arquitetura relevantes para as etapas seguintes:

- **Arquitetura de cena única.** O aplicativo nunca recarrega a cena. A inicialização da sessão AR, a permissão de câmera, a obtenção do primeiro ponto de GPS e a alocação dos modelos de IA na GPU são operações custosas e visivelmente lentas em Android; recarregar a cena para "voltar de tela" reiniciaria todas elas. Toda a navegação ocorre dentro de uma única cena, com os subsistemas de AR, sensores e inferência preservados em memória. Essa escolha é a razão pela qual a troca de telas é instantânea.
- **Separação por responsabilidade.** Cada preocupação (localização, visão computacional, spawn de modelos, interface, integração com servidor) é um componente independente, conectado por configuração e não por dependência direta entre eles. Isso permite evoluir ou substituir qualquer parte sem efeito colateral nas demais.
- **Estimativa de iluminação em AR.** O aplicativo lê a estimativa de luz ambiente fornecida pelo ARCore a cada quadro e ajusta a iluminação da cena virtual de acordo, de forma que os tubarões 3D recebam luz coerente com o ambiente real onde o usuário está (praia ensolarada, fim de tarde, sombra).

O aplicativo foi compilado, instalado e validado em **aparelho Android físico com suporte a ARCore**, não apenas no simulador.

---

### 5.2. Classificação de imagem on-device (ONNX) sobre o feed da câmera

**Situação: concluído.**

O aplicativo executa inferência de inteligência artificial **no próprio dispositivo**, utilizando o *Unity Inference Engine* 2.6.1 com aceleração por GPU. **Nenhuma imagem da câmera é enviada para servidores** — requisito importante tanto para privacidade quanto para funcionamento em Fernando de Noronha, onde a conectividade é limitada.

**Pipeline implementado:**

1. O componente se inscreve no evento de novo quadro da câmera AR;
2. Amostra o feed em intervalo controlado (e não a cada quadro, preservando bateria e fluidez);
3. Converte a imagem nativa da câmera para textura, corrige a orientação conforme a posição física do aparelho e recorta para a proporção da tela;
4. Redimensiona para as dimensões de entrada do modelo e converte em tensor;
5. Executa a inferência de forma assíncrona (sem travar a renderização) e lê o resultado.

O gerenciamento de memória de GPU é explícito: todos os tensores e o executor do modelo são liberados no encerramento, evitando vazamento de memória de vídeo — um problema comum e difícil de diagnosticar em aplicações AR com IA embarcada.

**Modelos embarcados:**

- **MobileNet V2 (ImageNet)** — classificador geral de imagem com 1.000 categorias, acompanhado do arquivo de rótulos correspondente. Permanece disponível no aplicativo e é ativado por chave de configuração.
- **MobileCLIP2-S0 (detector de mar)** — detalhado no item 6.2 deste relatório, por se tratar de entrega adicional ao escopo. Na configuração atual, é este o modelo que consome o pipeline descrito acima.

O pipeline é agnóstico ao modelo: trocar a rede neural utilizada significa substituir o arquivo `.onnx` e seu arquivo de rótulos, sem alteração de código.

---

### 5.3. Localização por GPS e geocodificação reversa das praias de Noronha

**Situação: concluído.**

O aplicativo identifica automaticamente em qual praia o usuário está, **100% offline**.

**Componente de GPS.** Solicita as permissões de localização precisa e aproximada em tempo de execução, inicializa o serviço de localização com tratamento de indisponibilidade e tempo limite, e publica a praia atual para os demais componentes por meio de um evento — que é disparado **apenas quando a praia efetivamente muda**, e não a cada quadro. Expõe também uma API de **seleção manual de praia**, que sobrepõe o resultado do GPS; essa funcionalidade permite ao usuário explorar o conteúdo de outras praias sem estar fisicamente nelas, e é o que torna possível testar e demonstrar o aplicativo fora da ilha.

**Motor de geocodificação reversa.** Implementado inteiramente no dispositivo, sem chamadas de rede e sem custo por consulta:

- A base geográfica é carregada sob demanda na primeira consulta e mantida em cache em memória, de modo que as consultas subsequentes **não alocam memória** — condição necessária para rodar a cada quadro sem impacto no desempenho da AR;
- O algoritmo calcula a distância do usuário a **todas** as praias e retorna a mais próxima dentro de um raio de tolerância configurável (padrão de 50 metros). Para pontos dentro do contorno de uma praia, a distância é zero; para pontos fora, calcula-se a distância em metros até a borda mais próxima, por projeção local;
- O raio de tolerância absorve a imprecisão natural do GPS de celular e a imprecisão do traçado da costa, evitando que o usuário "perca" a praia por poucos metros;
- Quando o usuário não está próximo de nenhuma praia mapeada, o sistema retorna esse estado explicitamente, em vez de arbitrar uma praia incorreta.

**Base geográfica — 17 praias de Fernando de Noronha:**

Praia do Sancho · Baía dos Porcos · Praia da Cacimba do Padre · Praia da Quixaba · Praia do Bode · Praia do Americano · Praia do Boldró · Praia da Conceição · Praia do Meio · Praia do Cachorro · Praia do Porto · Enseada dos Tubarões · Buraco da Raquel · Enseada da Caieira · Praia do Atalaia · Baía do Sueste · Praia do Leão

A base foi **migrada de caixas retangulares aproximadas para contornos de costa reais**: 14 praias utilizam o traçado efetivo da linha costeira (chegando a 61 pontos de contorno em uma única praia) e 3 pontos de mergulho sem contorno mapeado utilizam áreas circunscritas ao redor de sua coordenada de referência. O ganho é direto: em uma ilha onde várias praias são vizinhas e separadas por poucas dezenas de metros, contornos reais são a diferença entre identificar a praia certa e a praia ao lado.

Cada praia possui **dois nomes**: uma chave técnica estável (usada internamente para vincular conteúdo, modelos 3D e registros) e um **nome de exibição em português** apresentado ao usuário. Essa separação permite corrigir ou reescrever livremente o texto apresentado ao público sem quebrar nenhuma vinculação interna de dados.

**Ferramental de apoio desenvolvido** (para manutenção futura da base, sem necessidade de intervenção da Contratada):

- Script de obtenção dos contornos de praia a partir de base cartográfica aberta;
- Script de migração e validação da base geográfica;
- Ferramenta visual interativa de depuração, que permite testar qualquer coordenada contra a base e visualizar o resultado.

**Validação automatizada:** foi implementada uma suíte de testes automatizados cobrindo o motor de geocodificação (pontos dentro de praias, pontos fora de todas as praias, comportamento do raio de tolerância e resolução de praias vizinhas).

---

### 5.4. Modelos 3D em AR com interação por toque

**Situação: concluído — entrega superior ao escopo contratado.**

A Proposta previa o posicionamento de **pelo menos um** modelo 3D em AR com interação por toque. Foram entregues **cinco espécies completas**:

| Espécie | Modelo |
|---|---|
| Tubarão-martelo | `hammerhead` |
| Tubarão-tigre | `tiger_shark` |
| Tubarão-limão | `lemon_shark` |
| Tubarão-lixa | `nurse_shark` |
| Tubarão-de-recife | `reef_shark` |

Cada espécie passou pelo processo completo de produção:

- **Importação e correção do modelo 3D**, incluindo ajuste de rig de animação e configuração de repetição contínua do ciclo de natação;
- **Conversão de materiais para o pipeline de renderização do projeto** (URP), com mapas de cor, normais e metalicidade conectados — sem essa etapa, os modelos são renderizados sem textura;
- **Escala em metros reais.** Foi adotada a convenção de que 1 unidade do mundo virtual equivale a 1 metro, e o comprimento real de cada espécie foi calibrado no próprio modelo. Um tubarão-martelo de 4 metros aparece em AR com 4 metros. Para viabilizar essa calibração de forma verificável, foi desenvolvida uma **ferramenta de editor que mede o modelo importado e informa o fator de escala exato** necessário para atingir o comprimento-alvo da espécie;
- **Empacotamento como variante de prefab**, de modo que correções futuras no modelo de origem se propaguem automaticamente para o aplicativo;
- **Controlador de animação** dedicado por espécie.

**Distribuição por praia.** Um componente dedicado escuta o evento de mudança de praia emitido pelo GPS e, a cada troca, remove os modelos da praia anterior e instancia os modelos configurados para a nova praia. A associação praia → espécies é feita por configuração, sem alteração de código: adicionar uma espécie a uma praia é uma operação de configuração.

**Interação por toque (tap-to-info).** Implementada sobre o Input System atual do Unity (e não sobre a API legada), a detecção de toque projeta um raio a partir da posição tocada na tela **com raio de tolerância de 20 cm**, em vez de um raio infinitamente fino. Essa tolerância é deliberada: acertar com o dedo um objeto virtual distante, através da câmera e com o aparelho em movimento na mão, é significativamente mais difícil do que clicar em um botão — a tolerância torna a interação confiável na prática. Ao acertar o animal, o painel informativo correspondente é exibido; ao tocar fora, é ocultado.

---

### 5.5. Navegação base entre telas

**Situação: concluído.**

O fluxo base de navegação está operante: **tela de abertura (Splash) → tela de AR/HUD → barra de navegação inferior** entre os destinos do aplicativo.

**Tela de abertura.** Exibida na inicialização por tempo configurável, cedendo o controle ao restante do aplicativo em seguida. A sessão AR é deliberadamente mantida **ativa durante a splash**, de modo que a inicialização da câmera e do rastreamento ocorra "atrás" da tela de abertura — quando o usuário chega à tela de AR, ela já está pronta. É o mesmo princípio que motivou a arquitetura de cena única.

**HUD de AR.** Sobreposição sobre a câmera com identificação da praia atual e área de informações do aplicativo.

**Gestão automática de desempenho e bateria.** Foi implementado um controlador que observa a tela ativa e ajusta o aplicativo de acordo. Fora das telas de AR:

- A **sessão AR é desligada** (câmera e rastreamento param), o que representa a maior economia de bateria possível no aplicativo, com retomada em menos de um segundo ao voltar para a AR;
- A **taxa de quadros é elevada para a taxa nativa do painel do aparelho** (90 ou 120 Hz em celulares modernos), de modo que a navegação e a rolagem da interface fiquem fluidas — enquanto em AR a taxa é fixada em 30 quadros por segundo, que é a cadência da câmera;
- A **inferência de IA é pausada**, liberando a GPU.

Esse comportamento é controlável por uma única chave de configuração, permitindo desativá-lo integralmente para fins de diagnóstico.

---

## 6. Entregas adicionais (além do escopo da Etapa 1)

As entregas a seguir **não constavam** do escopo mínimo da Etapa 1 e foram realizadas sem custo adicional.

### 6.1. Quatro espécies de tubarão além do mínimo contratado

Conforme item 5.4: a Proposta previa **um** modelo 3D em AR; foram entregues **cinco**, cada um com o ciclo completo de produção (importação, materiais URP, escala real calibrada, animação em laço e distribuição por praia).

### 6.2. Detector de mar por visão computacional, sem treinamento e sem base de dados

Foi desenvolvido um componente capaz de responder à pergunta *"a câmera está apontada para o mar?"* — **sem coletar imagens, sem rotular dados e sem treinar modelo algum**.

**Como funciona.** Um codificador de imagem (MobileCLIP2-S0, 45 MB) executado no dispositivo converte o quadro da câmera em uma representação numérica. A definição de "mar" não vem de um modelo treinado, mas de **descrições em texto** processadas previamente no computador de desenvolvimento e embarcadas como um pequeno arquivo de dados. O aplicativo compara a imagem com essas descrições e calcula a probabilidade de a cena ser mar, aplicando suavização temporal e histerese (limiares distintos para ligar e desligar) para evitar oscilação do resultado.

**Consequência prática:** ajustar ou ampliar o comportamento do detector — por exemplo, distinguir "mar aberto" de "piscina natural" — é uma **alteração de texto seguida de reprocessamento offline**, sem necessidade de nova coleta de imagens, sem retreinamento e sem atualização do modelo embarcado.

**Resultados medidos:**

| Cenário de teste | Pontuação de "mar" |
|---|---|
| Fotos de praias de Noronha | 0,56 a 0,99 (mediana 0,87) |
| Fotos submarinas em close de tubarões | 0,07 a 0,19 |
| Imagens de interface e texturas do aplicativo | ≤ 0,03 |

Os limiares de decisão foram fixados dentro da folga medida entre as faixas (ligar em 0,50 / desligar em 0,35), o que confere margem de segurança ao comportamento.

**Verificação de paridade em dispositivo.** Como modelos de IA podem produzir resultados diferentes no celular e no computador de referência (por diferenças de precisão numérica e de espaço de cor), foi embarcado um **autoteste**: o aplicativo classifica uma imagem de referência na inicialização e registra o resultado. O valor medido no aparelho foi de **82,7%**, contra **82,6%** na referência em Python — comprovando que a implementação embarcada está correta.

**Tratamento de orientação do aparelho.** Constatou-se em campo que o quadro entregue pela câmera chega sempre na orientação nativa do sensor, independentemente de como o usuário segura o celular, e que um quadro deitado derruba drasticamente a pontuação do detector (uma cena de praia de 90% cai para cerca de 10%). O quadro passou a ser rotacionado conforme a orientação real da tela e recortado para a proporção do visor, de forma que o modelo analise exatamente o que o usuário está vendo. A correção foi validada empiricamente por varredura das quatro orientações possíveis em dispositivo real.

**Ferramental offline entregue:** script de exportação do codificador de imagem, script de geração das descrições de referência e harness de teste em lote sobre imagens locais — tudo versionado e documentado.

### 6.3. Migração da base geográfica para contornos de costa reais

Conforme item 5.3: a base saiu de caixas retangulares aproximadas para contornos de costa efetivos em 14 das 17 praias, com o ferramental de obtenção, migração e depuração visual necessário para mantê-la.

### 6.4. Automação de verificação e documentação técnica

- **Execução de testes e rotinas de build sem abrir o editor Unity**, por linha de comando, viabilizando verificação rápida e repetível;
- **Documentação técnica de engenharia** mantida junto ao código, cobrindo arquitetura, decisões de projeto, procedimentos operacionais e armadilhas conhecidas de cada subsistema. Essa documentação é parte do pacote de entrega previsto para a Etapa 3 e vem sendo escrita de forma contínua, e não ao final do projeto.

---

## 7. Como a Contratante pode validar esta etapa

Sugere-se o seguinte roteiro de homologação, em aparelho Android com suporte a ARCore:

| # | Verificação | Resultado esperado |
|---|---|---|
| 1 | Abrir o aplicativo | Tela de abertura exibida e transição automática para a tela principal |
| 2 | Conceder as permissões de câmera e localização | Aplicativo prossegue normalmente |
| 3 | Entrar na tela de AR | Feed da câmera com rastreamento AR ativo |
| 4 | Estar em uma das 17 praias mapeadas (ou usar a seleção manual de praia) | Nome da praia identificado e exibido |
| 5 | Observar a cena em AR | Modelos de tubarão da praia correspondente presentes e animados |
| 6 | Tocar em um tubarão | Painel informativo exibido |
| 7 | Deslocar-se para outra praia mapeada (ou trocar a praia manualmente) | Modelos trocados automaticamente para os da nova praia |
| 8 | Sair da tela de AR e navegar pelo aplicativo | Interface fluida; câmera desligada em segundo plano |
| 9 | Apontar a câmera para o mar e para fora do mar | Pontuação do detector de mar reagindo conforme o item 6.2 |

A Contratada permanece à disposição para acompanhar a homologação presencialmente ou de forma remota, e para fornecer o pacote de instalação (APK) em versão de teste.

---

## 8. Próximos passos — Etapa 2

Conforme item 4 da Proposta, a Etapa 2 compreende **Conteúdo, Backend e Registro de Avistamentos**, com os seguintes entregáveis:

- Catálogo de Animais com visualizador 3D interativo (rotação, zoom e enquadramento automático);
- Telas de Praias com cartão de condições e tábua de marés oficial (DHN / Marinha do Brasil);
- Backend (FastAPI) com API de upload e contador de avistamentos;
- Painel administrativo web (listagem, filtros, visualização, edição/exclusão e telemetria);
- Armazenamento de imagens em nuvem (original + variante de exibição, com EXIF preservado);
- Fluxo de registro de avistamentos no aplicativo, integrado ao backend, com fila de upload em segundo plano e retentativa.

Esses itens serão objeto do **Relatório de Entrega — Etapa 2**.

Registre-se, conforme item 7 da Proposta, que o **design de UX/UI** é insumo de responsabilidade da Contratante e condiciona o cronograma de implementação das telas; eventuais deslocamentos de prazo decorrentes da disponibilização desse insumo seguem o previsto no item 5 da Proposta.

---

## 9. Aceite e marco de pagamento

Com base nos entregáveis descritos nos itens 4 e 5 deste relatório, a Contratada considera **cumprido integralmente o escopo da Etapa 1** e submete os entregáveis à validação da Contratante.

| | |
|---|---|
| **Etapa** | Etapa 1 — Núcleo do Aplicativo AR e Reconhecimento |
| **Percentual** | 30% do valor total |
| **Valor** | **R$ 7.500,00** |

Conforme o item 6 da Proposta, a Contratante dispõe de **5 (cinco) dias úteis** para validar os entregáveis; decorrido esse prazo sem manifestação formal, os entregáveis serão considerados aceitos. O pagamento é devido em até **5 (cinco) dias úteis** após a apresentação dos entregáveis e a emissão da respectiva nota fiscal.

**Dados bancários para pagamento:**

| | |
|---|---|
| **Favorecido** | GUILHERME PINTO FICKEL DESENVOLVIMENTO DE SOFTWARE LTDA |
| **CNPJ** | 58.187.905/0001-00 |
| **Banco** | 301 |
| **Agência** | 0001 |
| **Conta / Código** | 31153256 |

---

_Guilherme Pinto Fickel Desenvolvimento de Software LTDA — 28 de setembro de 2026._
