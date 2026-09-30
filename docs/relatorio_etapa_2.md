# Relatório de Entrega — Etapa 2

**Projeto "Mergulho Virtual" — Conteúdo, Backend e Registro de Avistamentos**

---

## 1. Identificação

| | |
|---|---|
| **Contratada** | GUILHERME PINTO FICKEL DESENVOLVIMENTO DE SOFTWARE LTDA |
| **CNPJ** | 58.187.905/0001-00 |
| **Projeto** | Mergulho Virtual — App Android AR + Sistema de Backend |
| **Documento de referência** | Proposta de Prestação de Serviço de 03 de junho de 2026 |
| **Etapa** | Etapa 2 — Conteúdo, Backend e Registro de Avistamentos |
| **Marco de pagamento vinculado** | 40% do valor total — R$ 10.000,00 |
| **Data do relatório** | 28 de setembro de 2026 |

---

## 2. Objetivo deste relatório

Este documento apresenta, para validação da Contratante, os entregáveis da **Etapa 2** definidos no item 4 da Proposta de Prestação de Serviço, dando continuidade ao Relatório de Entrega — Etapa 1.

Para cada entregável contratado, descreve-se o que foi implementado, as decisões técnicas relevantes e como a Contratante pode verificá-lo. Ao final, são listadas as entregas adicionais e os próximos passos referentes à Etapa 3.

---

## 3. Resumo executivo

**Todos os entregáveis da Etapa 2 foram concluídos.** O sistema deixou de ser um aplicativo isolado e passou a ser uma solução completa, com servidor próprio, banco de dados, armazenamento de imagens em nuvem e painel de operação.

O que passou a existir nesta etapa:

1. **Conteúdo científico e educativo no aplicativo** — fichas de espécies com visualizador 3D interativo, e páginas de praia com condições do mar em tempo real e a tábua de marés oficial da Marinha do Brasil;
2. **Um servidor próprio** (API) que recebe os avistamentos enviados pelos usuários e informa o total registrado;
3. **Um painel administrativo web** onde a equipe do projeto consulta, filtra, edita e exclui os registros, além de acompanhar a telemetria;
4. **Armazenamento das fotos em nuvem**, preservando o arquivo original intacto — com todos os metadados de GPS, data e câmera — e gerando uma versão leve para visualização;
5. **O fluxo de registro de avistamentos pelo usuário**, com envio em segundo plano que sobrevive à falta de sinal e ao fechamento do aplicativo.

Três pontos merecem destaque:

- **A tábua de marés é a oficial da Marinha, não uma estimativa.** Foi construído um pipeline que lê o PDF anual publicado pela Diretoria de Hidrografia e Navegação e o converte para o formato do aplicativo, preservando os horários exatos de preamar e baixamar. O usuário em Noronha vê o mesmo horário e a mesma altura que leria na tábua impressa.
- **O envio de avistamentos foi construído para a realidade de conectividade da ilha.** O usuário nunca espera pelo upload: registra, recebe a confirmação e continua usando o aplicativo. A foto é enviada quando houver sinal — inclusive dias depois, inclusive após o aplicativo ter sido fechado.
- **Reenvios não geram registros duplicados.** Cada avistamento carrega uma chave de idempotência que o servidor usa como identificador do registro; a mesma foto reenviada após uma falha de rede sobrescreve a si mesma em vez de criar uma segunda entrada. É o que torna a retentativa automática segura.

---

## 4. Quadro-resumo dos entregáveis

| # | Entregável previsto na Proposta (item 4, Etapa 2) | Situação |
|---|---|---|
| 1 | Catálogo de Animais com visualizador 3D interativo (rotação / zoom / enquadramento) | **Concluído** (ver observação em 5.1) |
| 2 | Telas de Praias com cartão de condições e tábua de marés oficial (DHN / Marinha) | **Concluído** |
| 3 | Backend (FastAPI) com API de upload e contador de avistamentos | **Concluído** |
| 4 | Painel administrativo web (listagem, filtros, visualização, edição/exclusão, telemetria) | **Concluído** |
| 5 | Armazenamento de imagens em nuvem (original + variante de exibição, EXIF preservado) | **Concluído** |
| 6 | Registro de avistamentos no app integrado ao backend, com fila em background e retentativa | **Concluído** |

---

## 5. Detalhamento dos entregáveis

### 5.1. Catálogo de Animais com visualizador 3D interativo

**Situação: concluído.**

**Ficha de espécie.** Cada espécie é descrita em uma ficha estruturada contendo nome popular em português, nome científico, descrição, fotografia com o respectivo crédito, modelo 3D associado, créditos do modelo e uma lista opcional de vídeos educativos. As cinco espécies entregues na Etapa 1 estão fichadas. **Adicionar uma nova espécie ao catálogo é criar uma ficha** — não há alteração de código envolvida.

**Visualizador 3D interativo.** A ficha abre um visualizador tridimensional com o modelo da espécie:

- **Rotação por arraste** de um dedo, em torno do eixo vertical;
- **Zoom por pinça** de dois dedos, com limites mínimo e máximo de distância;
- **Enquadramento automático.** Ao abrir uma espécie, o sistema mede o corpo efetivamente visível do modelo, centraliza-o e calcula a distância de câmera necessária para enquadrá-lo com folga. Como as espécies têm tamanhos reais bem diferentes (de 2,5 a 4 metros), sem isso cada ficha abriria em um enquadramento diferente; com isso, **todas abrem igualmente enquadradas**, e a rotação do usuário gira em torno do corpo do animal, e não de um ponto arbitrário do arquivo 3D.

**Isolamento em relação à cena de AR.** O visualizador utiliza câmera, iluminação e camada de renderização próprias, separadas da cena de Realidade Aumentada. A decisão é deliberada: renderizar os modelos dentro da mesma câmera da AR provoca conflitos de profundidade e de iluminação, e obriga a manter a sessão AR ativa — com o custo de bateria correspondente — em uma tela que não usa a câmera. Com o isolamento, a sessão AR pode permanecer desligada enquanto o usuário navega o catálogo. A iluminação do visualizador usa um conjunto de três luzes (principal, de preenchimento e de contorno) que destaca a silhueta do animal, com suavização de bordas configurada para o hardware móvel.

> **Observação sobre o ponto de entrada na navegação.** A tela e o visualizador estão construídos, validados e operantes. O **layout de UX/UI (V2) entregue pela Contratante define quatro destinos na barra de navegação inferior e não contempla um destino "Animais"**, de modo que a definição de onde esse catálogo será acessado pelo usuário depende de decisão de design da Contratante (item 7 da Proposta). Registre-se que **o catálogo de espécies já está em uso produtivo no aplicativo** independentemente dessa decisão: é ele que alimenta as fichas de espécie exibidas nas páginas de praia e as opções de espécie do formulário de registro de avistamento. A Contratada permanece à disposição para conectar a tela ao ponto de entrada que a Contratante definir.

---

### 5.2. Telas de Praias com cartão de condições e tábua de marés oficial

**Situação: concluído.**

#### Cartão de condições do mar

Apresenta, para a praia atual, as condições vigentes:

| Informação | Origem |
|---|---|
| Altura, período e direção da onda | Serviço meteorológico marinho (dado horário) |
| Velocidade e direção do vento | Serviço meteorológico (dado horário) |
| Temperatura da água | Serviço meteorológico marinho |
| Maré (altura atual, próxima preamar e próxima baixamar) | Tábua oficial DHN embarcada |
| Fase da lua | Calculada no próprio aparelho |

Decisões relevantes:

- **A fonte meteorológica é gratuita e não exige chave de acesso nem contrato**, o que evita custo recorrente e ponto de falha administrativa para a Contratante;
- **Nenhum valor é inventado.** Se um campo não vier na resposta do serviço, o cartão exibe um traço no lugar, em vez de estimar. A distinção entre "sem dado" e "dado zero" é preservada em todo o caminho;
- O cartão informa **há quanto tempo o dado foi obtido** ("Atualizado: há N min"), atualizando-se sozinho — relevante para um usuário que ficou sem sinal;
- **A fase da lua é calculada localmente**, sem rede, portanto funciona offline.

#### Tábua de marés oficial da Marinha do Brasil

A tábua embarcada **não é um modelo numérico estimado: é a tabela oficial publicada anualmente pela Diretoria de Hidrografia e Navegação (DHN) da Marinha do Brasil** para o Arquipélago de Fernando de Noronha (Baía de Santo Antônio, Carta 52).

Foi desenvolvido um pipeline de duas etapas que converte o PDF oficial no formato do aplicativo:

1. **Extração do PDF** → os extremos oficiais de maré do ano (**1.410 registros** de preamar e baixamar), com validação automática da cobertura dos 365 dias;
2. **Geração do arquivo do aplicativo** → contendo **duas visões paralelas** dos mesmos dados:
   - **8.760 amostras horárias** (uma por hora do ano), interpoladas entre os extremos oficiais, que desenham a curva suave do gráfico de maré;
   - **os 1.410 extremos literais**, com o horário exato publicado.

**Por que duas visões — e por que isso importa para o usuário.** Uma grade horária só consegue indicar picos em horas cheias. A DHN publica, por exemplo, baixamar às **12:40** com 0,42 m; a grade horária diria "12:00, 0,40 m". Um morador ou condutor de turismo que confere a tábua impressa perceberia a divergência imediatamente. Por isso o gráfico usa a curva interpolada, mas **os horários de preamar e baixamar exibidos são os oficiais**, sem arredondamento.

Demais decisões:

- As alturas seguem o **mesmo referencial da tábua impressa** (datum LAT), e o Nível Médio da estação (1,28 m) é desenhado como linha de referência no gráfico — de modo que os números do aplicativo coincidem com os da publicação da Marinha;
- Erro residual do aplicativo em relação à tábua oficial: **médio de 17 mm, máximo de 39 mm** — decorrente apenas da amostragem da curva, não de discordância de modelo;
- **Atualização para o próximo ano**: baixar o PDF publicado pela Marinha e executar dois comandos. O processo não depende de bibliotecas científicas pesadas nem de download de modelos, e está documentado.

#### Conteúdo editorial por praia

Foi criada uma base de conteúdo por praia, separada da base geográfica, contemplando nível de risco, características do ambiente, melhor época do ano, maré ideal, período de maior avistamento, horário de guarda-vidas, avisos, espécies associadas e dicas de segurança.

O comportamento adotado diante de conteúdo ainda não preenchido é deliberado: **um campo vazio faz a seção simplesmente não ser desenhada**, em vez de exibir texto de preenchimento ou um cartão vazio. Isso permite que a Contratante publique o aplicativo com o conteúdo que já tem e vá completando o restante ao longo do tempo, sem que a tela pareça defeituosa em nenhum momento intermediário. A base é editável em arquivo de texto estruturado, sem necessidade de intervenção da Contratada, e falhas de preenchimento (campo desconhecido, praia inexistente, entrada duplicada) são registradas e degradadas com segurança, nunca derrubando a tela.

---

### 5.3. Backend (FastAPI) com API de upload e contador de avistamentos

**Situação: concluído.**

O servidor foi implementado em **FastAPI (Python)** e expõe ao aplicativo:

| Rota | Função |
|---|---|
| `POST /api/v1/avistamentos` | Recebe o avistamento (foto + metadados) |
| `GET /api/v1/avistamentos/count` | Informa o total de avistamentos registrados |

**Chave de idempotência — a decisão que torna a retentativa segura.** Todo envio carrega uma chave única gerada pelo aplicativo. O servidor a utiliza **simultaneamente como identificador do documento no banco de dados e como nome do arquivo no bucket de imagens**. Consequência prática: se a conexão cair depois que o servidor já gravou, mas antes que o aplicativo receba a confirmação, a retentativa automática reenvia a mesma chave — e o servidor reconhece o registro existente e o devolve, **sem criar um segundo registro e sem regravar a imagem**. Sem esse mecanismo, uma fila de retentativa produziria avistamentos duplicados exatamente nas condições de rede ruim para as quais ela foi criada.

**Estrutura do dado gravado.** Cada avistamento é gravado com praia, data e hora, espécie, observação e os componentes de data (dia, mês, ano) em campos separados — estes últimos para viabilizar os filtros do painel administrativo. Cada registro carrega também um marcador de origem (`app`, importação histórica ou dado de exemplo), permitindo separar as submissões dos usuários do acervo pré-existente em qualquer consulta.

**Modo de desenvolvimento local.** Foi implementado um modo de execução que substitui o banco de dados em nuvem por um emulador local, o armazenamento em nuvem por uma pasta no disco e semeia automaticamente seis avistamentos de exemplo na inicialização. Os caminhos de código são os mesmos da produção — apenas os destinos mudam. O valor disso para a Contratante é direto: **é possível desenvolver, testar e demonstrar o sistema sem tocar em dados reais e sem consumir serviços pagos de nuvem.**

**Testes automatizados.** O backend possui suíte de testes automatizados cobrindo as rotas da API, as rotas do painel, o serviço de avistamentos e o processamento de imagens.

---

### 5.4. Painel administrativo web

**Situação: concluído.**

Interface web para a equipe do projeto, contemplando:

- **Listagem paginada** dos avistamentos;
- **Filtros** por dia, mês, ano, praia e espécie, combináveis entre si;
- **Visualização** de um registro individual, com a fotografia enviada;
- **Edição** e **exclusão** de registros;
- **Telemetria** — listagem e contagem dos pontos de telemetria.

**Filtragem no servidor, não no navegador.** Os filtros são aplicados diretamente na consulta ao banco de dados, e não sobre uma lista já carregada. A diferença aparece conforme o acervo cresce: a abordagem adotada continua respondendo igual com dezenas de milhares de registros.

**As opções dos filtros são lidas do próprio banco de dados**, com cache de 5 minutos, em vez de fixadas a partir das listas do aplicativo. A razão é concreta: o acervo histórico importado contém variações de grafia de praias e espécies que o aplicativo nunca gera. Fixar as opções nas listas do aplicativo tornaria esses registros **invisíveis ao operador** — eles existiriam no banco sem aparecer em nenhum filtro.

**Índices de banco de dados declarados como código.** O banco de dados adotado (Firestore) exige um índice previamente construído para **cada combinação distinta** de campos filtrados com o campo de ordenação; a primeira consulta sem índice falha. Com 5 campos filtráveis, são 31 combinações possíveis — provisioná-las manualmente, uma a uma, conforme os erros aparecem, não é sustentável.

Foi implementado um **gerador declarativo**: os campos filtráveis são declarados em uma lista, o script enumera todas as combinações e emite o arquivo de configuração. **32 índices** estão declarados e publicados. Adicionar um novo filtro ao painel passou a ser: acrescentar o nome do campo à lista, rodar o gerador e publicar. O procedimento, a ordem correta de execução e o limite da plataforma (200 índices, com crescimento exponencial no número de filtros) estão documentados, incluindo as duas alternativas de arquitetura recomendadas caso o número de filtros ultrapasse sete no futuro.

---

### 5.5. Armazenamento de imagens em nuvem

**Situação: concluído.**

Cada avistamento enviado gera **dois arquivos** no armazenamento em nuvem (Google Cloud Storage):

| Arquivo | Conteúdo | Finalidade |
|---|---|---|
| `originals/<id>.<ext>` | Bytes exatamente como saíram do aparelho | Fonte da verdade; preservação do acervo |
| `imagens/<id>.jpg` | Versão redimensionada (maior lado 1600 px, JPEG qualidade 85) | Exibição no painel e futuras telas |

**Preservação de EXIF.** O bloco de metadados EXIF e o perfil de cor são transportados para a versão de exibição sem reinterpretação, de modo que **coordenadas de GPS, data e hora originais da fotografia, modelo de câmera e orientação sobrevivem** ao redimensionamento. Para um projeto de ciência cidadã, esse é um dado científico relevante: permite validar onde e quando a foto foi de fato tirada, independentemente do que o usuário declarou.

**O redimensionamento é feito no servidor, e não no aplicativo.** A justificativa é técnica: o codificador de imagem do Unity descarta todo o EXIF ao gerar um JPEG, e reconstruir o bloco de metadados manualmente de forma confiável nos dois sistemas operacionais — incluindo o formato HEIC das fotos de iPhone — é frágil. No servidor, a biblioteca utilizada faz isso corretamente em uma única operação. A contrapartida assumida é o envio da foto em tamanho integral; em troca, o original é preservado e todo o tráfego posterior de visualização usa a versão leve.

**Suporte a HEIC** (formato padrão das fotos de iPhone) está habilitado. As imagens são exibidas no painel por meio de **URLs assinadas com expiração**, de modo que o conteúdo enviado pelos usuários não fica publicamente acessível na internet.

---

### 5.6. Registro de avistamentos no aplicativo, com fila em background

**Situação: concluído.**

#### Fluxo do usuário

1. Seleciona uma foto da galeria do aparelho (Android e iOS);
2. Marca, opcionalmente, espécie, porte do animal, comportamentos observados, identificação e perfil (turista ou condutor);
3. Envia.

A praia é obtida automaticamente da localização ou da seleção manual, e a data e hora são as do envio. **Apenas a fotografia é obrigatória** — todo o restante é opcional, decisão tomada para não criar barreira ao registro no momento em que o usuário está na praia, muitas vezes com pressa e com o aparelho molhado.

**A fotografia é lida em cópia bruta de bytes, sem recodificação**, tanto na seleção quanto no envio — é isso que preserva o EXIF descrito no item anterior.

#### Envio sem espera ("fire-and-forget")

O usuário **não aguarda o upload**. Ao enviar, o avistamento é colocado em fila, a confirmação aparece e o aplicativo retorna à tela inicial em cerca de um segundo. O aplicativo nunca condiciona o usuário à disponibilidade do servidor ou à existência de sinal — o que seria, em Fernando de Noronha, condicioná-lo a algo fora do seu controle.

#### Fila de tarefas em background

Foi implementado um sistema de fila com as seguintes características:

- **Persistência em disco**, um arquivo por tarefa, com escrita atômica (arquivo temporário seguido de renomeação) — uma queda de energia no meio da gravação deixa o arquivo antigo ou o novo, nunca um arquivo pela metade;
- **Retentativa com backoff exponencial**: 5 segundos → 30 segundos → 2 minutos → 10 minutos → 1 hora, estabilizando em 1 hora;
- **Detecção de ausência de conectividade**: sem rede, a tarefa é adiada sem consumir tentativas;
- **Distinção entre falha temporária e definitiva**: erros de servidor e de conexão são reprogramados; erros definitivos movem a tarefa para uma pasta de falhas, preservada para diagnóstico em vez de descartada;
- **Sobrevivência ao fechamento do aplicativo**: ao reabrir, a fila é recarregada do disco e retomada automaticamente. A fila é inicializada **na abertura do aplicativo**, e não ao entrar na tela de registro — é essa decisão que garante a retomada de um envio pendente sem depender de o usuário visitar novamente aquela tela.

**Posse do arquivo de imagem.** No momento do enfileiramento, a fotografia é **copiada para a área privada do aplicativo**. Sem isso, o envio dependeria de um arquivo que o sistema operacional pode remover (a seleção da galeria devolve um arquivo em área temporária) ou que o próprio usuário pode apagar da galeria entre o registro e o envio efetivo.

**Acompanhamento pelo usuário.** A tela de avistamentos exibe "Seus avistamentos pendentes", lendo o estado real da fila — na fila, tentando novamente, aguardando conexão ou falhou — de modo que o usuário sabe o que aconteceu com o que enviou, sem precisar confiar cegamente no sistema.

A fila é genérica: outros tipos de tarefa em segundo plano (downloads de conteúdo, por exemplo) podem ser adicionados reaproveitando toda a persistência, a retentativa e o tratamento de conectividade.

---

## 6. Entregas adicionais (além do escopo da Etapa 2)

### 6.1. Vídeos educativos embutidos nas fichas de espécie

Subsistema de reprodução de vídeo integrado às fichas, com cartões de "toque para assistir", controles próprios (reproduzir/pausar, barra de progresso arrastável, tempo decorrido e total) e reprodução por streaming progressivo. Nada é baixado antes de o usuário tocar no vídeo.

Os vídeos são servidos de um **bucket público separado**, dedicado a conteúdo educativo, sem passar pelo servidor do projeto — o que significa que **não consomem recursos nem banda da infraestrutura do backend**, e que publicar um novo vídeo é enviar um arquivo ao bucket e acrescentar o endereço à ficha da espécie. O subsistema é independente de tela: pode ser reaproveitado em páginas de praia ou institucionais sem duplicação.

### 6.2. Publicação automática do último post do Instagram do projeto

Widget que exibe, dentro do aplicativo, a última publicação da conta de Instagram do projeto (imagem, Reel ou carrossel), com toque para abrir a publicação original.

**O aplicativo nunca se comunica com o Instagram.** O servidor consulta a API oficial periodicamente, guarda o resultado e serve apenas os próprios endpoints do projeto. Esse desenho traz três benefícios: as credenciais do Instagram nunca saem do servidor; a publicação é exibida instantaneamente (o aplicativo mostra a versão em cache antes mesmo de consultar o servidor); e, caso a consulta ao Instagram falhe, **a última publicação válida continua sendo servida** — o novo conteúdo só é publicado internamente depois de completamente obtido. A renovação automática da credencial de acesso é feita pelo servidor, com registro de alerta caso a expiração se aproxime.

### 6.3. Ferramental de conteúdo para a equipe do projeto

Scripts que preenchem semiautomaticamente as fichas de espécies e as descrições de praias a partir de fontes públicas (consulta por nome científico, download da imagem principal e preenchimento de descrição e crédito fotográfico), reduzindo o trabalho manual de curadoria. Preenchem apenas campos vazios por padrão, preservando qualquer conteúdo já revisado pela equipe.

### 6.4. Importação do acervo histórico anterior ao aplicativo

Scripts de importação dos avistamentos e dos pontos de telemetria pré-existentes (planilhas CSV e arquivos de mapa KML) para o banco de dados do projeto. O resultado é que o painel administrativo **não nasceu vazio**: opera sobre o acervo histórico e as submissões novas no mesmo lugar, com filtros uniformes entre as duas origens.

---

## 7. Como a Contratante pode validar esta etapa

**No aplicativo, em aparelho Android:**

| # | Verificação | Resultado esperado |
|---|---|---|
| 1 | Abrir uma ficha de espécie e arrastar / pinçar sobre o modelo | Modelo gira e aproxima; abre bem enquadrado |
| 2 | Abrir a página de uma praia | Condições do mar e gráfico de maré preenchidos |
| 3 | Conferir os horários de preamar / baixamar contra a tábua impressa da Marinha | Horários e alturas coincidentes |
| 4 | Registrar um avistamento com foto da galeria | Confirmação imediata e retorno à tela inicial |
| 5 | Ativar o modo avião e registrar outro avistamento | Registro aceito e listado como aguardando conexão |
| 6 | Fechar o aplicativo, reabrir e restaurar a conexão | Envio retomado automaticamente |

**No painel administrativo web:**

| # | Verificação | Resultado esperado |
|---|---|---|
| 7 | Abrir a listagem de avistamentos | Registros do acervo e os enviados nos testes acima |
| 8 | Aplicar filtros de praia, espécie e data | Lista filtrada corretamente |
| 9 | Abrir um registro enviado pelo aplicativo | Fotografia exibida |
| 10 | Editar e salvar um registro | Alteração persistida |
| 11 | Abrir a listagem de telemetria | Pontos listados |

A Contratada permanece à disposição para acompanhar a homologação e fornecer acesso ao painel e o pacote de instalação de teste.

---

## 8. Próximos passos — Etapa 3

Conforme o item 4 da Proposta, a Etapa 3 compreende **Segurança, Produção e Entrega Final**:

- Infraestrutura de produção provisionada (VM em nuvem + serviço gerenciado);
- Exposição segura via Cloudflare Tunnel (HTTPS) com domínio próprio;
- Firebase App Check ativo nas rotas do aplicativo (Play Integrity / App Attest);
- Cloudflare Access protegendo o painel administrativo;
- Aplicativo apontando para o backend de produção (build de release);
- Implementação da interface conforme o design de UX/UI fornecido pela Contratante;
- Ajustes finais de desempenho, estabilidade e usabilidade;
- Pacote de entrega: código-fonte, documentação técnica de operação/deploy e instruções de manutenção.

Esses itens serão objeto do **Relatório de Entrega — Etapa 3**.

Permanece pendente de definição pela Contratante, conforme registrado no item 5.1 deste relatório, **o ponto de entrada do Catálogo de Animais na navegação do aplicativo**, decorrente do layout de UX/UI entregue.

---

## 9. Aceite e marco de pagamento

Com base nos entregáveis descritos nos itens 4 e 5 deste relatório, a Contratada considera **cumprido integralmente o escopo da Etapa 2** e submete os entregáveis à validação da Contratante.

| | |
|---|---|
| **Etapa** | Etapa 2 — Conteúdo, Backend e Registro de Avistamentos |
| **Percentual** | 40% do valor total |
| **Valor** | **R$ 10.000,00** |

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
