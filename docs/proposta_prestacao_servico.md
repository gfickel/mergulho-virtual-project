# Proposta de Prestação de Serviço

**Desenvolvimento do Aplicativo "Mergulho Virtual" (App Android AR + Sistema de Backend)**

---

## 1. Identificação da Contratada

| | |
|---|---|
| **Razão Social** | GUILHERME PINTO FICKEL DESENVOLVIMENTO DE SOFTWARE LTDA |
| **CNPJ** | 58.187.905/0001-00 |

| | |
|---|---|
| **Data da proposta** | 03 de junho de 2026 |
| **Validade da proposta** | 30 dias a contar da data acima |
| **Prazo total estimado** | 3 (três) meses |

---

## 2. Objeto

Constitui objeto desta proposta o desenvolvimento, a configuração e a entrega de uma solução completa de turismo digital para o **Arquipélago de Fernando de Noronha**, denominada **"Mergulho Virtual"**, composta por:

1. **Aplicativo móvel** com Realidade Aumentada (AR) para Android (com caminho de captura de avistamentos compatível com iOS); e
2. **Sistema de backend** (API, painel administrativo, armazenamento de imagens e infraestrutura de produção em nuvem).

O aplicativo identifica a vida marinha por meio de classificação de imagem executada no próprio dispositivo (on-device), sobrepõe uma interface sensível à localização (GPS + identificação da praia), posiciona modelos 3D de animais marinhos em AR, exibe um contador de avistamentos e permite que os usuários registrem seus próprios avistamentos (foto + metadados).

---

## 3. Escopo dos Serviços

### 3.1. Aplicativo Móvel (Android / AR)

- Motor de AR sobre **Unity 6 + URP + AR Foundation / ARCore**.
- **Classificação de imagem on-device** do feed da câmera via modelo ONNX (sem dependência de servidor para o reconhecimento).
- **Localização e geocodificação reversa**: leitura de GPS e identificação automática da praia de Fernando de Noronha a partir de polígonos georreferenciados.
- **Modelos 3D em AR**: posicionamento de animais marinhos (ex.: Tubarão Martelo, Tubarão Tigre, Tubarão Limão) por praia, com interação por toque (tap-to-info).
- **Catálogo de Animais**: tela de listagem + visualizador 3D interativo (rotação por arraste, zoom por pinça, enquadramento automático da câmera).
- **Telas de conteúdo**: praias (com cartão de condições e tábua de marés oficial da Marinha/DHN), sobre, e navegação por barra inferior.
- **Implementação da interface (UI)** no aplicativo a partir do design de UX/UI fornecido pela Contratante (ver itens 3.3 e 7): construção das telas, componentes e fluxos conforme o layout entregue por profissional designer.
- **Registro de avistamentos pelo usuário**: seleção de foto da galeria, marcação de praia/data/espécie/observações e envio em segundo plano.
- **Sistema de fila de tarefas em background** com persistência em disco, retentativa com backoff exponencial e tolerância a falhas de conectividade (uploads sobrevivem ao fechamento do app).
- **Preservação de EXIF** das fotos enviadas (GPS, data original, modelo de câmera).
- Otimizações de desempenho e bateria (pausa de AR e taxa de quadros adaptativa fora das telas de AR).

### 3.2. Sistema de Backend

- **API HTTP** (FastAPI) voltada ao aplicativo: upload de avistamentos (multipart) e contador de avistamentos.
- **Painel administrativo web** (operador): listagem paginada com filtros, visualização, edição e exclusão de registros, além de telemetria.
- **Armazenamento de imagens** em nuvem (Google Cloud Storage): original preservado + variante redimensionada para exibição, com EXIF mantido.
- **Banco de dados** Firestore com **índices compostos** para filtros server-side.
- **Segurança**:
  - Atestação de integridade do app via **Firebase App Check** (Play Integrity no Android / App Attest no iOS) para as rotas do aplicativo;
  - **Cloudflare Access** (SSO) para o painel administrativo.
- **Infraestrutura de produção**: VM em nuvem (GCE), serviço gerenciado via systemd, exposição segura por **Cloudflare Tunnel** (HTTPS), domínio próprio e processo de deploy automatizado.

### 3.3. Itens Não Inclusos (Exclusões)

Salvo acordo aditivo por escrito, **não** estão inclusos: **criação do design de UX/UI** (concepção visual, protótipos e identidade), que ficará a cargo de profissional designer contratado pela Contratante — a Contratada se responsabiliza pela **implementação** desse design no aplicativo; publicação nas lojas (taxas de Google Play / Apple Developer e processo de revisão); custos recorrentes de infraestrutura (nuvem, domínio, Cloudflare); produção de novos modelos 3D além dos já previstos; criação de conteúdo textual/fotográfico de terceiros; versão nativa iOS completa de AR (o iOS contempla apenas o caminho de captura de avistamentos); e suporte/manutenção após o aceite final (ver item 8).

---

## 4. Etapas, Entregáveis e Marcos de Pagamento

O projeto é dividido em **3 (três) etapas**, cada uma com entregáveis claros e um **marco de pagamento** vinculado ao aceite da Contratante. O pagamento de cada etapa é devido após a validação dos respectivos entregáveis.

### Etapa 1 — Núcleo do Aplicativo AR e Reconhecimento (Mês 1)

**Entregáveis:**
- Projeto Unity configurado (AR Foundation / ARCore) rodando em dispositivo Android.
- Classificação de imagem on-device (ONNX) operando sobre o feed da câmera.
- Localização por GPS + geocodificação reversa das praias de Noronha.
- Posicionamento de pelo menos **um** modelo 3D em AR com interação por toque.
- Navegação base entre telas (Splash, AR/HUD, barra inferior).

**Marco de pagamento:** **30%** do valor total, mediante aceite dos entregáveis acima.

---

### Etapa 2 — Conteúdo, Backend e Registro de Avistamentos (Mês 2)

**Entregáveis:**
- Catálogo de Animais com visualizador 3D interativo (rotação/zoom/enquadramento).
- Telas de Praias com cartão de condições e tábua de marés oficial (DHN/Marinha).
- Backend (FastAPI) com API de upload e contador de avistamentos.
- Painel administrativo web (listagem, filtros, visualização, edição/exclusão, telemetria).
- Armazenamento de imagens em nuvem (original + variante de exibição, EXIF preservado).
- Fluxo de **registro de avistamentos** no app (seleção de foto, metadados, envio) integrado ao backend, com fila de upload em background e retentativa.

**Marco de pagamento:** **40%** do valor total, mediante aceite dos entregáveis acima.

---

### Etapa 3 — Segurança, Produção e Entrega Final (Mês 3)

**Entregáveis:**
- Infraestrutura de produção provisionada (VM em nuvem + serviço gerenciado).
- Exposição segura via Cloudflare Tunnel (HTTPS) com domínio próprio.
- Firebase App Check ativo nas rotas do app (Play Integrity / App Attest).
- Cloudflare Access protegendo o painel administrativo.
- Aplicativo apontando para o backend de produção (build de release).
- Ajustes finais de desempenho, estabilidade e usabilidade.
- Pacote de entrega: código-fonte, documentação técnica de operação/deploy e instruções de manutenção.

**Marco de pagamento:** **30%** do valor total, mediante aceite final do projeto.

---

## 5. Cronograma Resumido

| Etapa | Período | % do valor |
|---|---|---|
| Etapa 1 — Núcleo do App AR e Reconhecimento | Mês 1 | 30% |
| Etapa 2 — Conteúdo, Backend e Avistamentos | Mês 2 | 40% |
| Etapa 3 — Segurança, Produção e Entrega Final | Mês 3 | 30% |
| **Total** | **3 meses** | **100%** |

> Atrasos da Contratante na validação de cada etapa ou na entrega de insumos deslocam o cronograma proporcionalmente.

---

## 6. Valores e Condições de Pagamento

| Descrição | Valor |
|---|---|
| **Valor total do projeto** | **R$ 25.000,00** |
| Etapa 1 (30%) | R$ 7.500,00 |
| Etapa 2 (40%) | R$ 10.000,00 |
| Etapa 3 (30%) | R$ 7.500,00 |

**Condições:**
- Cada parcela será faturada e cobrada após o **aceite** dos entregáveis da respectiva etapa, conforme item 4.
- Prazo de pagamento: até **5 (cinco) dias úteis** após a apresentação dos entregáveis e emissão da nota fiscal.
- A Contratante terá **5 (cinco) dias úteis** para validar cada etapa; decorrido o prazo sem manifestação formal, os entregáveis serão considerados aceitos.

**Dados bancários para pagamento:**

| | |
|---|---|
| **Favorecido** | GUILHERME PINTO FICKEL DESENVOLVIMENTO DE SOFTWARE LTDA |
| **CNPJ** | 58.187.905/0001-00 |
| **Banco** | 301 |
| **Agência** | 0001 |
| **Conta / Código** | 31153256 |

---

## 7. Premissas e Responsabilidades da Contratante

Para o cumprimento dos prazos, a Contratante deverá providenciar tempestivamente:
- O **design de UX/UI** das telas (layouts, protótipos e *assets* visuais), elaborado por profissional designer, entregue em formato adequado à implementação (ex.: Figma) e em tempo hábil para cada etapa; a Contratada implementará a interface conforme esse design;
- Contas e acessos de nuvem (Google Cloud / Firebase) e o domínio de produção, bem como o custeio dos serviços recorrentes;
- Conta de desenvolvedor nas lojas (Google Play / Apple), quando aplicável à publicação;
- Conteúdo de terceiros necessário (textos, fotos, créditos, modelos 3D adicionais);
- Disponibilidade para validação de cada etapa dentro do prazo previsto;
- Um dispositivo Android compatível com ARCore para testes de homologação, quando solicitado.

---

## 8. Garantia e Suporte

- **Garantia de correção de defeitos** por **30 (trinta) dias** após o aceite final, limitada a falhas no escopo entregue (não cobre novas funcionalidades, mudanças de requisito ou problemas decorrentes de alterações feitas por terceiros).
- Manutenção evolutiva e suporte contínuo após o período de garantia poderão ser contratados à parte, mediante proposta específica.

---

## 9. Propriedade Intelectual

Após a quitação integral, o código-fonte e os artefatos desenvolvidos sob este escopo serão de titularidade da Contratante. Bibliotecas, pacotes e ativos de terceiros permanecem sob suas respectivas licenças.
