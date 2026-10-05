# ADR 0003 — Os dois espaços de identificador de agendamento

**Status:** aceito
**Data:** outubro de 2026

## Contexto

A API de Agendamento da Amplimed identifica o mesmo agendamento de duas formas
incompatíveis, e cada endpoint aceita apenas uma delas:

| Identificador | De onde vem | Quem aceita | Existe para |
|---|---|---|---|
| UUID (`id`) | resposta de `cadastrar-agendamento` | `cancelar-agendamento`, `excluir-agendamento`, `historico-agendamentos` | só o que foi marcado por esta integração |
| `idAgendamento` inteiro + `usuclin` | `listar-agendamentos` | `alterar-status-agendamento` | todo agendamento da clínica |

Não há endpoint que traduza um no outro.

O problema prático: um agendamento marcado pelo WhatsApp conhece apenas o próprio
UUID. Quando o paciente responde "Confirmar" ao template de véspera, o serviço
precisa de `alterar-status-agendamento`, que exige o par inteiro — que ele não
tem.

Há também uma assimetria de identificador de paciente: a API não expõe id próprio
de paciente. O identificador é o `codReferencia`, chamado `idUnico` na API de
Agendamento e devolvido como `codRef` na listagem, e **quem o cunha é o
integrador**. Reaproveitar um valor para pacientes diferentes mescla cadastros
silenciosamente.

## Decisão

**O espelho local guarda os dois identificadores, ambos opcionais**, e o domínio
expõe o que cada um habilita em vez de deixar o chamador adivinhar:
`PodeAlterarStatusNaAmplimed()` e `PodeCancelarNaAmplimed()`.

O par inteiro é obtido por **late binding**: o polling da agenda (ADR 0002)
encontra a linha correspondente ao agendamento criado pelo WhatsApp — mesmo
paciente, mesmo instante, com tolerância de um minuto — e chama
`VincularAgenda(usuclin, idAgendamento)`. A partir daí a alteração de status está
disponível.

Enquanto o par não existe, o pedido de status vai para a **outbox** em vez de ser
descartado. Dois caminhos levam ao mesmo lugar e o código trata os dois:
`cancelar-agendamento` é preferido no cancelamento e na remarcação porque devolve
o horário à escala, com `alterar-status-agendamento` como alternativa quando o
agendamento não nasceu aqui.

O `codReferencia` do paciente é **derivado do id local** (`ears-{guid}`): estável
para sempre, único por paciente e sem CPF embutido. Para paciente que a clínica
já tinha, `PUT /pacientes/integration/codreferencia` amarra o nosso valor ao
cadastro existente, achado pelo CPF — o que faz o upsert do
`cadastrar-agendamento` reconhecer o paciente por `idUnico` em vez de arriscar
duplicata.

## Consequências

- Confirmação por WhatsApp de um agendamento recém-criado pode levar até um ciclo
  de polling para refletir na agenda da clínica. Com cron de 15 minutos, é o pior
  caso; na prática o agendamento costuma aparecer no polling seguinte, bem antes
  da véspera.
- A outbox deixa de ser decorativa: ela é o que sustenta a promessa de
  reprocessamento quando o par ainda não existe. O despacho por tipo é
  pré-requisito de produção, não melhoria.
- O casamento por paciente + instante é heurístico. Dois agendamentos do mesmo
  paciente no mesmo minuto, com profissionais diferentes, podem ser casados
  trocados. É raro o bastante para aceitar e barato o bastante para revisar se
  aparecer.
- Nenhum `codReferencia` é reaproveitado, por construção: ele sai do Guid do
  espelho local.
