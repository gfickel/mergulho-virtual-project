# Conteúdo das praias — o que falta preencher

Este documento é a lista de tarefas para terminar o conteúdo das telas **Praias** e
**Praia (detalhe)** do app. Ele é para quem escreve o conteúdo — a equipe do projeto
e os biólogos — e não exige saber programar.

O arquivo a editar é:

```
src/app/MergulhoVirtual/Assets/Resources/beaches_content.json
```

---

## Antes de começar — três regras

1. **Campo vazio é melhor do que campo inventado.** Se ninguém confirmou o horário
   do salva-vidas daquela praia, deixe `""`. A tela foi feita para esconder a
   informação que falta — ela nunca inventa um valor. Um horário de salva-vidas
   errado ou um nível de risco chutado são piores do que um espaço em branco.
2. **Não mude o campo `name`.** Ele é a chave que liga este arquivo ao mapa, aos
   tubarões em realidade aumentada e ao banco de dados de avistamentos. Mudar o
   `name` quebra as três coisas de uma vez. O nome que o usuário lê é o
   `displayName`, e ele fica no outro arquivo (`places.json`).
3. **Ao preencher um campo, apague o nome dele da lista `_todo`** daquela praia.
   A lista `_todo` é só um lembrete para humanos; o app a ignora.

### Como editar sem quebrar o arquivo

- Abra em um editor de texto simples (VS Code, Bloco de Notas, `gedit`) — **não** no Word.
- Texto sempre entre aspas duplas: `"riskLevel": "baixo"`.
- Acentos e `ç` podem ser digitados normalmente.
- Se o texto tiver aspas, escreva `\"` no lugar de `"`.
- Listas usam colchetes e vírgulas:
  `"tips": ["Primeira dica.", "Segunda dica.", "Terceira dica."]`
- Depois de editar, peça para alguém rodar `python3 -m json.tool` no arquivo — se
  ele reclamar, ficou uma vírgula ou uma aspa fora do lugar.

---

## Nomes a confirmar

O nome que aparece na tela vem do campo `displayName` de `places.json`
(`src/app/MergulhoVirtual/Assets/Resources/places.json`). Quatro praias tinham nome
em inglês no arquivo e ganharam agora um nome em português. **Confirmem estes três**,
que têm mais de uma forma em uso na ilha:

| Chave interna (não mudar) | Nome exibido hoje | Confirmar |
|---|---|---|
| `Atalaia Beach` | Praia do Atalaia | "do Atalaia" ou "da Atalaia"? |
| `Praia do Porto de Santo Antônio Noronha` | Praia do Porto de Santo Antônio | ou só "Praia do Porto"? |
| `Sharks Cove` | Enseada dos Tubarões | confere? |

Os outros ficaram: `Boldró Beach` → **Praia do Boldró**, `Sueste Beach` → **Baía do
Sueste**. As demais praias já estavam em português e o nome exibido é igual à chave.

---

## O que cada campo faz na tela

| Campo | O que aparece na tela | Formato | Exemplo do protótipo |
|---|---|---|---|
| `riskLevel` | Pílula colorida "Risco: Baixo" ao lado do nome da praia | uma palavra: `baixo`, `medio` ou `alto` | `"baixo"` |
| `environmentTags` | Pílulas escuras sobre a foto de capa | lista de textos curtos (2 a 3) | `["Ambiente recifal", "Área de berçário"]` |
| `bestSeason` | Valor de "Melhor Época" no card de números | texto curto | `"Set - Fev"` |
| `idealTide` | Valor de "Maré Ideal" | uma palavra: `baixa`, `alta` ou `qualquer` | `"baixa"` — o app completa sozinho com o horário |
| `sightingPeak` | Card "Pico de avistamento" | texto curto | `"Jan-Mar manhã"` |
| `lifeguardHours` | Faixa amarela de aviso: "Salva-vidas: …" | texto curto | `"Das 08h às 17h"` |
| `advisories` | Linhas extras na faixa amarela — regras e restrições | lista de frases | `["É proibido pisar nos recifes."]` |
| `species` | Chips "Espécies comuns" + card da espécie | lista de espécies (ver abaixo) | — |
| `species[].behaviour` | "Comportamento nessa praia: …" dentro do card | 1 a 3 frases | `"Utilizam as águas rasas como berçário…"` |
| `species[].tag` | Pílula sobre a foto da espécie (opcional) | texto muito curto | `"Área de berçário"` |
| `tips` | Seção "Dicas de convivência" | **exatamente 3** frases | `["Mantenha distância…", "…", "…"]` |

> **Pistas do protótipo (a confirmar, não preenchidas).** O designer deixou
> exemplos nas telas que podem ser um bom ponto de partida — mas são só exemplos,
> então **ninguém preencheu nada a partir deles**. Na tela de detalhe da **Baía do
> Sueste** ele escreveu as pílulas "Mar de fora" e "Área do parque"; na tela de
> lista, "Ambiente recifal" e "Área de berçário". Se a equipe confirmar que
> valem para aquela praia, é só escrever em `environmentTags`.

> **Sobre `tips`:** no protótipo são sempre três dicas, numeradas 1-2-3 pelo próprio
> app (não escreva o número na frase). São dicas de **convivência com a fauna**
> naquela praia. Se houver só duas dicas boas, é melhor deixar a lista vazia do que
> completar com uma frase genérica.

---

## Espécies que podem ser usadas hoje

O campo `species` usa uma **chave**, não o nome escrito. Só estas cinco existem no
app (um arquivo `.asset` em `Assets/Resources/Animals/` para cada):

| Chave a escrever | Espécie |
|---|---|
| `hammerhead` | Tubarão Martelo |
| `lemon_shark` | Tubarão Limão |
| `nurse_shark` | Tubarão Lixa |
| `reef_shark` | Tubarão Bico-Fino |
| `tiger_shark` | Tubarão Tigre |

Para acrescentar uma espécie (raias, barracudas, tartaruga-verde…) é preciso primeiro
cadastrá-la no app — peça ao desenvolvedor. **Não invente uma chave nova aqui**: o
app não vai encontrá-la e o chip fica em branco.

Formato de cada espécie dentro de `species`:

```json
{ "key": "lemon_shark", "tag": "Área de berçário", "behaviour": "Comportamento nessa praia: …" }
```

A ordem da lista é a ordem dos chips na tela, e o primeiro já vem selecionado.

---

## Praia por praia

Para achar uma praia no arquivo, procure pela linha `"name": "<chave>"`. O caminho
entre colchetes (ex.: `beaches[0].riskLevel`) é a referência exata para o desenvolvedor.

### 1. Praia do Sancho

- Chave no arquivo: `"name": "Praia do Sancho"`  ·  caminho: `beaches[0]`

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[0].advisories` = “O acesso é feito por uma escada vertical instalada nas rochas.”
  - origem: places.json > description
- `beaches[0].species` = `hammerhead`, `reef_shark` — origem: MainScene.unity > Beach Shark Spawner > Beaches (são os tubarões que já aparecem em realidade aumentada nesta praia).

**Falta preencher:**

- [ ] `beaches[0].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[0].environmentTags` — Pílulas sobre a foto de capa (Tela 1 e Tela 4), ex.: "Ambiente recifal", "Área de berçário". Vazio = a faixa de pilulas some.
- [ ] `beaches[0].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[0].idealTide` — Valor da coluna "Maré Ideal" (Tela 4). O app acrescenta sozinho o horário da próxima maré (ex.: "Baixa (até 14h)"), então escreva só a maré.
- [ ] `beaches[0].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[0].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[0].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.
- [ ] `beaches[0].species[].behaviour` — Texto "Comportamento nessa praia: …" dentro do card da espécie (Tela 4).

---

### 2. Baía dos Porcos

- Chave no arquivo: `"name": "Baía dos Porcos"`  ·  caminho: `beaches[1]`

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[1].environmentTags` = “Área do parque”; “Piscina natural”
  - origem: places.json > description — “Faz parte do Parque Nacional Marinho e tem visitação controlada. / Na maré baixa surgem piscinas naturais de águas transparentes.”
- `beaches[1].idealTide` = “baixa”
  - origem: places.json > description — “Na maré baixa surgem piscinas naturais de águas transparentes, ideais para snorkeling.”
- `beaches[1].advisories` = “Faz parte do Parque Nacional Marinho e tem visitação controlada.”
  - origem: places.json > description
- `beaches[1].species` = `tiger_shark` — origem: MainScene.unity > Beach Shark Spawner > Beaches (são os tubarões que já aparecem em realidade aumentada nesta praia).

**Falta preencher:**

- [ ] `beaches[1].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[1].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[1].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[1].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[1].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.
- [ ] `beaches[1].species[].behaviour` — Texto "Comportamento nessa praia: …" dentro do card da espécie (Tela 4).

---

### 3. Praia da Cacimba do Padre

- Chave no arquivo: `"name": "Praia da Cacimba do Padre"`  ·  caminho: `beaches[2]`

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[2].species` = `lemon_shark`, `nurse_shark` — origem: MainScene.unity > Beach Shark Spawner > Beaches (são os tubarões que já aparecem em realidade aumentada nesta praia).

**Falta preencher:**

- [ ] `beaches[2].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[2].environmentTags` — Pílulas sobre a foto de capa (Tela 1 e Tela 4), ex.: "Ambiente recifal", "Área de berçário". Vazio = a faixa de pilulas some.
- [ ] `beaches[2].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[2].idealTide` — Valor da coluna "Maré Ideal" (Tela 4). O app acrescenta sozinho o horário da próxima maré (ex.: "Baixa (até 14h)"), então escreva só a maré.
- [ ] `beaches[2].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[2].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[2].advisories` — Linhas extras na faixa amarela de aviso — regras e restrições da praia.
- [ ] `beaches[2].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.
- [ ] `beaches[2].species[].behaviour` — Texto "Comportamento nessa praia: …" dentro do card da espécie (Tela 4).

---

### 4. Praia da Quixaba

- Chave no arquivo: `"name": "Praia da Quixaba"`  ·  caminho: `beaches[3]`

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[3].environmentTags` = “Mar de dentro”
  - origem: places.json > description — “um dos cenários mais reservados do Mar de Dentro.”

**Falta preencher:**

- [ ] `beaches[3].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[3].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[3].idealTide` — Valor da coluna "Maré Ideal" (Tela 4). O app acrescenta sozinho o horário da próxima maré (ex.: "Baixa (até 14h)"), então escreva só a maré.
- [ ] `beaches[3].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[3].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[3].advisories` — Linhas extras na faixa amarela de aviso — regras e restrições da praia.
- [ ] `beaches[3].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[3].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

### 5. Praia do Bode

- Chave no arquivo: `"name": "Praia do Bode"`  ·  caminho: `beaches[4]`

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[4].environmentTags` = “Mar de dentro”; “Piscina natural”
  - origem: places.json > description — “Praia tranquila do Mar de Dentro, com piscinas naturais que se formam entre as pedras na maré baixa.”
- `beaches[4].idealTide` = “baixa”
  - origem: places.json > description — “piscinas naturais que se formam entre as pedras na maré baixa.”

**Falta preencher:**

- [ ] `beaches[4].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[4].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[4].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[4].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[4].advisories` — Linhas extras na faixa amarela de aviso — regras e restrições da praia.
- [ ] `beaches[4].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[4].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

### 6. Praia do Americano

- Chave no arquivo: `"name": "Praia do Americano"`  ·  caminho: `beaches[5]`

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[5].idealTide` = “baixa”
  - origem: places.json > description — “na maré baixa, é possível chegar a pé a partir da Praia do Bode.”

**Falta preencher:**

- [ ] `beaches[5].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[5].environmentTags` — Pílulas sobre a foto de capa (Tela 1 e Tela 4), ex.: "Ambiente recifal", "Área de berçário". Vazio = a faixa de pilulas some.
- [ ] `beaches[5].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[5].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[5].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[5].advisories` — Linhas extras na faixa amarela de aviso — regras e restrições da praia.
- [ ] `beaches[5].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[5].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

### 7. Praia do Boldró

- Chave no arquivo: `"name": "Boldró Beach"`  ·  caminho: `beaches[6]`
- Nome exibido na tela: **Praia do Boldró** (vem de `places.json` → `displayName`)

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[6].environmentTags` = “Ambiente recifal”
  - origem: places.json > description — “Na maré baixa, recifes de coral ficam expostos no canto esquerdo.”

**Falta preencher:**

- [ ] `beaches[6].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[6].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[6].idealTide` — Valor da coluna "Maré Ideal" (Tela 4). O app acrescenta sozinho o horário da próxima maré (ex.: "Baixa (até 14h)"), então escreva só a maré.
- [ ] `beaches[6].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[6].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[6].advisories` — Linhas extras na faixa amarela de aviso — regras e restrições da praia.
- [ ] `beaches[6].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[6].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

### 8. Praia da Conceição

- Chave no arquivo: `"name": "Praia da Conceição"`  ·  caminho: `beaches[7]`

**Falta preencher:**

- [ ] `beaches[7].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[7].environmentTags` — Pílulas sobre a foto de capa (Tela 1 e Tela 4), ex.: "Ambiente recifal", "Área de berçário". Vazio = a faixa de pilulas some.
- [ ] `beaches[7].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[7].idealTide` — Valor da coluna "Maré Ideal" (Tela 4). O app acrescenta sozinho o horário da próxima maré (ex.: "Baixa (até 14h)"), então escreva só a maré.
- [ ] `beaches[7].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[7].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[7].advisories` — Linhas extras na faixa amarela de aviso — regras e restrições da praia.
- [ ] `beaches[7].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[7].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

### 9. Praia do Meio

- Chave no arquivo: `"name": "Praia do Meio"`  ·  caminho: `beaches[8]`

**Falta preencher:**

- [ ] `beaches[8].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[8].environmentTags` — Pílulas sobre a foto de capa (Tela 1 e Tela 4), ex.: "Ambiente recifal", "Área de berçário". Vazio = a faixa de pilulas some.
- [ ] `beaches[8].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[8].idealTide` — Valor da coluna "Maré Ideal" (Tela 4). O app acrescenta sozinho o horário da próxima maré (ex.: "Baixa (até 14h)"), então escreva só a maré.
- [ ] `beaches[8].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[8].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[8].advisories` — Linhas extras na faixa amarela de aviso — regras e restrições da praia.
- [ ] `beaches[8].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[8].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

### 10. Praia do Cachorro

- Chave no arquivo: `"name": "Praia do Cachorro"`  ·  caminho: `beaches[9]`

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[9].environmentTags` = “Piscina natural”
  - origem: places.json > description — “Na maré baixa surge o Buraco do Galego, piscina natural circular.”
- `beaches[9].idealTide` = “baixa”
  - origem: places.json > description — “Na maré baixa surge o Buraco do Galego, piscina natural circular formada pelas rochas no canto direito.”

**Falta preencher:**

- [ ] `beaches[9].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[9].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[9].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[9].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[9].advisories` — Linhas extras na faixa amarela de aviso — regras e restrições da praia.
- [ ] `beaches[9].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[9].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

### 11. Praia do Porto de Santo Antônio

- Chave no arquivo: `"name": "Praia do Porto de Santo Antônio Noronha"`  ·  caminho: `beaches[10]`
- Nome exibido na tela: **Praia do Porto de Santo Antônio** (vem de `places.json` → `displayName`)

**Falta preencher:**

- [ ] `beaches[10].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[10].environmentTags` — Pílulas sobre a foto de capa (Tela 1 e Tela 4), ex.: "Ambiente recifal", "Área de berçário". Vazio = a faixa de pilulas some.
- [ ] `beaches[10].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[10].idealTide` — Valor da coluna "Maré Ideal" (Tela 4). O app acrescenta sozinho o horário da próxima maré (ex.: "Baixa (até 14h)"), então escreva só a maré.
- [ ] `beaches[10].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[10].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[10].advisories` — Linhas extras na faixa amarela de aviso — regras e restrições da praia.
- [ ] `beaches[10].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[10].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

### 12. Enseada dos Tubarões

- Chave no arquivo: `"name": "Sharks Cove"`  ·  caminho: `beaches[11]`
- Nome exibido na tela: **Enseada dos Tubarões** (vem de `places.json` → `displayName`)

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[11].environmentTags` = “Mar de fora”
  - origem: places.json > description — “Pequena enseada do Mar de Fora.”

**Falta preencher:**

- [ ] `beaches[11].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[11].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[11].idealTide` — Valor da coluna "Maré Ideal" (Tela 4). O app acrescenta sozinho o horário da próxima maré (ex.: "Baixa (até 14h)"), então escreva só a maré.
- [ ] `beaches[11].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[11].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[11].advisories` — Linhas extras na faixa amarela de aviso — regras e restrições da praia.
- [ ] `beaches[11].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[11].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

### 13. Buraco da Raquel

- Chave no arquivo: `"name": "Buraco da Raquel"`  ·  caminho: `beaches[12]`

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[12].environmentTags` = “Piscina natural”
  - origem: places.json > description — “Em torno, piscinas rasas se formam nas pedras.”
- `beaches[12].advisories` = “A descida é proibida — apenas a observação do alto é permitida.”
  - origem: places.json > description

**Falta preencher:**

- [ ] `beaches[12].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[12].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[12].idealTide` — Valor da coluna "Maré Ideal" (Tela 4). O app acrescenta sozinho o horário da próxima maré (ex.: "Baixa (até 14h)"), então escreva só a maré.
- [ ] `beaches[12].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[12].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[12].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[12].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

### 14. Enseada da Caieira

- Chave no arquivo: `"name": "Enseada da Caieira"`  ·  caminho: `beaches[13]`

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[13].environmentTags` = “Piscina natural”
  - origem: places.json > description — “duas piscinas naturais entre as rochas premiam o esforço.”
- `beaches[13].advisories` = “O acesso é por trilha de alta dificuldade, com guia credenciado obrigatório (cerca de 3,5 horas só de ida).”; “O uso de protetor solar é proibido antes do banho.”
  - origem: places.json > description

**Falta preencher:**

- [ ] `beaches[13].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[13].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[13].idealTide` — Valor da coluna "Maré Ideal" (Tela 4). O app acrescenta sozinho o horário da próxima maré (ex.: "Baixa (até 14h)"), então escreva só a maré.
- [ ] `beaches[13].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[13].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[13].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[13].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

### 15. Praia do Atalaia

- Chave no arquivo: `"name": "Atalaia Beach"`  ·  caminho: `beaches[14]`
- Nome exibido na tela: **Praia do Atalaia** (vem de `places.json` → `displayName`)

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[14].environmentTags` = “Área do parque”; “Ambiente recifal”; “Piscina natural”
  - origem: places.json > description — “Um dos atrativos mais procurados do Parque Nacional, é uma piscina natural rasa abrigada por recifes.”
- `beaches[14].advisories` = “O acesso é por trilha guiada com horário marcado e o banho dura 30 minutos.”; “O uso de dermocosméticos como protetor solar é proibido.”
  - origem: places.json > description

**Falta preencher:**

- [ ] `beaches[14].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[14].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[14].idealTide` — Valor da coluna "Maré Ideal" (Tela 4). O app acrescenta sozinho o horário da próxima maré (ex.: "Baixa (até 14h)"), então escreva só a maré.
- [ ] `beaches[14].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[14].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[14].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[14].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

### 16. Baía do Sueste

- Chave no arquivo: `"name": "Sueste Beach"`  ·  caminho: `beaches[15]`
- Nome exibido na tela: **Baía do Sueste** (vem de `places.json` → `displayName`)

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[15].environmentTags` = “Ambiente recifal”; “Manguezal”
  - origem: places.json > description — “Baía cercada por recifes e área de manguezal.”
- `beaches[15].advisories` = “O snorkeling é permitido apenas nas raias delimitadas.”; “É proibido pisar nos recifes ou tocar a fauna.”
  - origem: places.json > description

**Falta preencher:**

- [ ] `beaches[15].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[15].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[15].idealTide` — Valor da coluna "Maré Ideal" (Tela 4). O app acrescenta sozinho o horário da próxima maré (ex.: "Baixa (até 14h)"), então escreva só a maré.
- [ ] `beaches[15].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[15].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[15].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[15].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

### 17. Praia do Leão

- Chave no arquivo: `"name": "Praia do Leão"`  ·  caminho: `beaches[16]`

**Já preenchido** (confira se está correto e complete se faltar algo):

- `beaches[16].environmentTags` = “Ambiente recifal”
  - origem: places.json > description — “Tem fortes correntes e formações de recife.”
- `beaches[16].advisories` = “Não é permitido caminhar sobre os recifes nem nadar fora das áreas indicadas.”
  - origem: places.json > description

**Falta preencher:**

- [ ] `beaches[16].riskLevel` — Pílula colorida "Risco: …" no card de localização (Tela 1) e ao lado do nome da praia (Tela 4). Vazio = nenhuma pilula é desenhada.
- [ ] `beaches[16].bestSeason` — Valor da coluna "Melhor Época" no card de estatísticas (Tela 4). Vazio = mostra "—".
- [ ] `beaches[16].idealTide` — Valor da coluna "Maré Ideal" (Tela 4). O app acrescenta sozinho o horário da próxima maré (ex.: "Baixa (até 14h)"), então escreva só a maré.
- [ ] `beaches[16].sightingPeak` — Card "Pico de avistamento" na tela de Praias (Tela 1). Vazio = "—".
- [ ] `beaches[16].lifeguardHours` — Faixa amarela de aviso (Tela 4): "Salva-vidas: <valor>". Vazio = a faixa não aparece (ou mostra o primeiro item de 'advisories').
- [ ] `beaches[16].species` — Chips "Espécies comuns" + o card de espécie abaixo deles (Tela 4). A ordem da lista é a ordem dos chips; o primeiro já vem selecionado.
- [ ] `beaches[16].tips` — Seção "Dicas de convivência" (Tela 4): exatamente 3 frases, numeradas 1-2-3 pelo app. Vazio = a seção inteira some.

---

## O que NÃO se preenche aqui

Dois blocos da tela de detalhe vêm do servidor, não deste arquivo, e por enquanto
aparecem vazios ou com `—`:

- **"120 avistamentos registrados"** — o app hoje só sabe o total da ilha inteira,
  não o de cada praia. Precisa de uma mudança no servidor.
- **"Galeria de avistamentos"** — as fotos enviadas pelos usuários são privadas e
  ainda não têm um endereço público. Precisa de uma decisão sobre privacidade e
  autorização de uso de imagem antes de existir.

A **maré do momento** ("Maré agora") também não é conteúdo: o app calcula sozinho a
partir da tábua de marés da Marinha. O campo `idealTide` é outra coisa — é a maré
*recomendada* para visitar a praia.

---

## Notas para quem for programar a tela

- O arquivo é lido com `JsonUtility`, que **não aceita um array no nível de cima** —
  por isso tudo está dentro do objeto `beaches`, no mesmo padrão que
  `ReverseGeocoding` usa para `places.json`. Monte um `Dictionary<string, …>` por
  `name` uma vez no carregamento.
- **Todo campo pode faltar.** `JsonUtility` devolve `null` para texto e lista
  ausentes; trate `null` e `""` do mesmo jeito e esconda a seção.
- `_comment`, `_todo` e `_sources` não têm campo correspondente em C# e são ignorados
  pelo `JsonUtility` — são documentação para quem edita o arquivo à mão.
- `riskLevel` e `idealTide` guardam um **token** (`baixo` / `baixa`), não o texto da
  tela. Quem monta a tela escreve "Risco: Baixo" e "Baixa (até 14h)" — o horário
  sai do `TideService`, não daqui.
- O card de espécie do protótipo mostra o **nome científico** ("Negaprion
  brevirostris") e o `AnimalDef` ainda não tem esse campo. O nome científico é
  igual em qualquer praia, então o lugar certo dele é um campo novo em
  `AnimalDef`, não aqui.
- Os chips do protótipo dizem "Tubarão-limão" (com hífen) e o `AnimalDef` diz
  "Tubarão Limão". Vale uniformizar nos `AnimalDef` antes de a tela ir ao ar.
