# Clínica EARS – Resumo do projeto SleekFlow para integração (Strenos)

> Documento recebido de Emerson (SleekFlow) em 20/10/2026. Guardado aqui porque
> é o contrato de integração entre o Flow Builder e este motor. O que o código
> faz com ele está em [ADR 0004](../adr/0004-integracao-flow-builder.md).

Data: 20/10/2026. Autor: Emerson (SleekFlow).
Para: dev da Strenos, para integrar o motor de agendamento ao fluxo de entrada do WhatsApp.

---

## 1. Visão geral

A Clínica EARS (otorrino e fonoaudiologia, Porto Alegre) atende pelo WhatsApp no SleekFlow em três camadas:

1. **Fluxo de entrada (Flow Builder):** primeira coisa que o paciente vê. Menu em lista do WhatsApp. Em rascunho, não publicado.
2. **Agente de IA "Assistente Ears" (AgentFlow, agentId `ezzTQPyv24Q0zAdX`):** responde dúvidas, coleta dados, transfere. **Não agenda.**
3. **Equipe "Recepção":** recebe todas as transferências.

A Strenos entra no fluxo de entrada, substituindo os ramos provisórios de agendamento.

## 2. Fluxo de entrada

- **Flow ID:** `aQPUdMLeg1D8oEE0` (Advanced Flow Builder, conta EARS). Rascunho, 40+ blocos.
- **Fluxo de teste antigo:** `VpdUM6m5LBWoRrDm`, ainda ativo, será desligado na publicação.

### Gatilho
- Evento: cliente envia mensagem no WhatsApp.
- Condição: a etiqueta **NÃO contém** `agendamento-em-curso`.
  - Enquanto o paciente estiver em agendamento conduzido pelo motor, as mensagens dele não devem reabrir o menu.
  - A integração precisa **adicionar** a etiqueta ao começar e **remover** ao terminar, concluindo ou desistindo. Senão o paciente nunca mais vê o menu.

### Ramos do menu

| # | Opção | Hoje | Integração Strenos |
|---|---|---|---|
| 1 | Agendar consulta | PROVISÓRIO: coleta + Recepção | **SIM** — o motor substitui o ramo inteiro |
| 2 | Agendar exame | coleta + Recepção | Não |
| 3 | Remarcar ou cancelar | PROVISÓRIO: coleta + Recepção | **SIM, para consulta.** Exame fica com a Recepção |
| 4 | Documentos | coleta + Recepção | Não |
| 5 | Convênios aceitos | texto fixo + IA | Não |
| 6 | Endereço e horário | texto fixo + IA | Não |
| 7 | Outras dúvidas | IA | Não |
| 8 | Falar com a equipe | Recepção | Não |
| – | Outra resposta (texto livre) | IA | Não |
| – | Nenhuma resposta (30 min) | encerra | Não |

Frase de transferência padrão:
> "Vou encaminhar seu atendimento para a nossa equipe, que continua com você por aqui."

### Timeout
- No Advanced Flow Builder o nó de espera que estoura vira "Falhou" no log e o fluxo **segue sem a resposta**.
- Por isso cada "Aguardar evento" tem depois uma ramificação: `Error Code` não existe → segue; senão → encerra.
- **Para a Strenos:** se o motor usar espera, aplicar o mesmo padrão.
- **Janela de 24h:** depois de 24h sem mensagem do paciente, só template aprovado.

## 3. Pontos de integração

1. **Ramo "Agendar consulta"** — remover blocos provisórios; chamar o motor (webhook/HTTP); adicionar e remover a etiqueta; erro/desistência/fora da regra → Recepção com a frase padrão.
2. **Ramo "Remarcar ou cancelar"** — consulta via motor; exame continua com a Recepção; precisa de pergunta "consulta ou exame?" no início.
3. **A confirmar com a clínica:** lembretes/confirmações disparados pela agenda. Automação separada, com template. Atenção para não duplicar o que a clínica já envia.

### Regras de negócio que o motor deve respeitar
- **Ordem da coleta:** o que precisa e para quem (idade); convênio; dia e turno; nome e data de nascimento. Médica de preferência é opcional.
- **Crianças:** Dra. Leticia Rosito e Dra. Caroline Elias. Dra. Renata Tramontin só a partir de 12 anos.
- **IPE e Unimed Unifácil:** a equipe faz a triagem. Não oferecer horários nem citar médicas.
- **Saúde PAS:** só consultas.
- **Convênio fora da lista:** oferecer particular (dinheiro ou Pix).
- **Teleconsulta:** não confirmar nem negar; a equipe avalia.
- **Cancelamento:** a clínica não cobra taxa. Nunca mencionar multa.
- **Urgência** (perda súbita de audição): transferir na hora. Nunca mandar ao pronto-atendimento pelo bot.
- **ATENDENTE**, em qualquer ponto: transferir na hora.

## 4. Contexto adicional

- Agente de IA v4 (15/10/2026): playbook com personalidade, comportamento, 11 guardrails e 4 cenários. A clínica mantém uma v5 própria.
- Base de conhecimento: 16 arquivos .md (FIXO_01–04, DIN_01–02, PROF_01–09, tabela de valores).
- Regras de saída do agente: só disparam com a frase de transferência, pedido explícito de pessoa ou ATENDENTE.
- LGPD: nenhuma conversa real de paciente vai para a base de conhecimento.

## 5. Pendências (do documento original)

- [ ] Strenos: integrar o motor nos ramos 1 e 3, com controle da etiqueta.
- [ ] Clínica: definir se haverá lembretes/confirmações automáticos e por qual sistema.
- [ ] Testar as condições de timeout (2 min) e voltar para 24h.
- [ ] Confirmar qual agente fica no fluxo.
- [ ] Avisos pós-transferência (fora do expediente; urgência por horário).
- [ ] Publicar o fluxo novo fora do horário e desligar o antigo.
- [ ] Playbook v6 da clínica.
