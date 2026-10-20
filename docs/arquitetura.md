# Arquitetura

## Quem é dono de quê

O motor **não** é a porta de entrada do WhatsApp. O menu é do fluxo
"EARS - Menu de entrada" no Advanced Flow Builder; o motor só assume nos ramos
1 (agendar consulta) e 3 (remarcar ou cancelar consulta), chamado pelo nó HTTP
do fluxo. O acoplamento entre os dois é a etiqueta `agendamento-em-curso`, que
suspende o menu enquanto o agendamento acontece. Detalhes e limitações da API
que levaram a esse desenho estão no [ADR 0004](adr/0004-integracao-flow-builder.md).

```
Paciente ──► Flow Builder (menu)
                 │
                 ├── ramos 2,4,5,6,7,8 ──► IA / Recepção
                 │
                 └── ramos 1 e 3 ──HTTP──► POST /webhooks/sleekflow/atendimento
                                                │
                            etiqueta aplicada ──┤
                                                ▼
                                          motor conduz
                                                │
                            etiqueta removida ──┘  (conclusão, transferência
                                                    ou expiração)
```

## Visão geral

```
Paciente (WhatsApp)
        │
        ▼
   SleekFlow  ──webhook──►  EARS.Integration.Api
        ▲                          │
        └────Platform API──────────┤
                                   ├──► Amplimed — Agendamento
                                   └──► Amplimed — Cadastro de Pacientes
                                   │
                              PostgreSQL
                                   ▲
                                   │ polling (15 min)
                        SincronizacaoAgendaJob
```

A seta de polling não é um detalhe de implementação: a Amplimed **não oferece
webhook** nesta integração. Sem consulta periódica, agendamento marcado no
sistema da clínica nunca chegaria ao espelho local.

## Camadas

**Domain** — entidades, enums e value objects. Sem dependência de framework.
As invariantes ficam aqui: `Agendamento.ElegivelParaConfirmacao()`,
`OutboxMessage.RegistrarFalha()`, validação de `Cpf` e `Telefone`.

**Application** — casos de uso e contratos (`IAmplimed*Client`, `ISleekFlowClient`,
repositórios). Não conhece EF Core nem HttpClient.

**Infrastructure** — implementações: EF Core, clients HTTP com Polly, jobs do Quartz.

**Api** — composição, webhook e endpoints administrativos.

## As duas APIs da Amplimed

| API | Base | Autenticação | Uso |
|---|---|---|---|
| Agendamento | `{gateway}/integracoes/referencia/{identificador}` | `x-authorization: {token}` | profissionais, horários livres, agenda do dia, marcação, status, cancelamento |
| Cadastro de Pacientes | `{gateway}/pacientes/integration` | `x-Authorization: Basic {token}` | busca por CPF, cadastro, vínculo de codReferencia |

O **identificador vai no caminho da rota**, depois de `/referencia`, e não em
cabeçalho. Só o token é header.

A listagem de profissionais vem da própria API de Agendamento
(`listar-profissionais`), então não há dependência da API de Dados.

### Esquisitices que o código isola

Tudo abaixo fica contido em `Clients/Amplimed`; o resto do sistema conversa pelos
DTOs normalizados da camada de aplicação.

- **Datas sem fuso, em dois formatos.** `"2023-11-14 08:00:00"` em
  `listar-horarios`, `"2025-04-11T15:00:00"` em `listar-agendamentos`. Em ambos o
  relógio é o da clínica: ler como UTC deslocaria tudo em três horas e o disparo
  D-1 cairia no dia errado. Toda leitura passa por `DataHoraAmplimed`.
- **Formatos de escrita divergentes entre as duas APIs.** Data de nascimento é
  `yyyy-MM-dd` em `cadastrar-agendamento` e `dd/MM/yyyy` em `POST /pattient`.
  Celular, nesta última, vai mascarado: `(48) 99999-9999`.
- **Status em texto na leitura, numérico na escrita.** A listagem devolve
  `"Agendado"`; `alterar-status-agendamento` recebe `statusCode`. `MapaStatus` faz
  as duas pontas, e texto desconhecido não vira status inventado.
- **Resposta da agenda aninhada por profissional**, achatada na leitura.
- **Listagem de pacientes com duas formas**: array puro e `{ "data": [...] }`.
- **A rota é `pattient`**, com dois t.

### Identificação de paciente

A API não expõe id próprio de paciente: o identificador é o `codReferencia`
(`idUnico` na API de Agendamento, `codRef` na listagem da agenda), e quem o cunha
é o integrador. Aqui ele é derivado do Guid local — `ears-{guid}` — por duas
razões: reaproveitar um valor mescla cadastros silenciosamente, e embutir CPF no
campo seria dado pessoal onde não precisa estar.

Também não existe busca por telefone, só por CPF, nome ou codReferencia. Por isso
o CPF é pedido na conversa, e o telefone serve apenas para casar com o espelho
local.

### Os dois identificadores de agendamento

Ver [ADR 0003](adr/0003-identificadores-de-agendamento.md). Resumo: UUID da
integração serve a cancelamento e exclusão; o par (usuclin, idAgendamento) da
listagem serve à alteração de status. Não há tradução entre eles, e o agendamento
criado pelo WhatsApp só ganha o par no primeiro polling que o encontrar.

## Resiliência

Toda chamada externa passa por três políticas, aplicadas por client:

1. **Retry** — 3 tentativas, backoff exponencial (2s, 4s, 8s), também em HTTP 429;
2. **Circuit breaker** — abre após 5 falhas, permanece 30s aberto;
3. **Timeout** — 15s por requisição.

Falha que sobrevive a isso vai para a tabela `outbox` e é reprocessada pelo
`OutboxDispatcherJob` a cada 5 minutos, com backoff próprio e teto de tentativas.

> O despacho por tipo na outbox está marcado com `TODO` — os handlers de
> reprocessamento entram junto com a integração real dos clients.

## Máquina de estados da conversa

A ordem é a que a clínica definiu, não a que seria conveniente para a Amplimed.

```
Inicio
  └─► AguardandoParaQuem        (para você ou para outra pessoa)
        └─► AguardandoIdade      (decide quais profissionais podem atender)
              └─► AguardandoConvenio
                    └─► AguardandoDiaTurno
                          └─► AguardandoNomePaciente
                                └─► AguardandoDataNascimento
                                      └─► [AguardandoCpf]   só se houver homônimo
                                            └─► AguardandoEscolhaHorario
                                                  └─► Concluida
```

Ramo 3 (remarcar ou cancelar) entra por outro caminho:

```
Inicio
  └─► AguardandoTipoRemarcacao
        ├─ exame    ──► TransferidaParaHumano
        ├─ cancelar ──► AguardandoConfirmacaoCancelamento ──► Concluida
        └─ remarcar ──► libera o horário ──► AguardandoEscolhaHorario
```

Profissional não é perguntado: a preferência é opcional e os horários já saem
com o nome de quem atende. O CPF só aparece quando nome e data de nascimento
não desempatam.

De qualquer estado: `TransferidaParaHumano` (urgência, pedido de atendente,
convênio de triagem humana ou falha inesperada) e `Expirada`.

O estado é persistido a cada turno: reiniciar o serviço não interrompe um
agendamento em andamento.

### Expiração não é higiene de banco

`ExpiracaoConversaJob` roda a cada dez minutos e fecha conversas paradas. O que
importa nele não é limpar linhas: é **tirar a etiqueta** de quem desistiu no
meio. Sem isso o paciente fica permanentemente sem menu, escreve no WhatsApp e
nada acontece — sem erro, sem log, sem sintoma que aponte para a causa.

Antes, a expiração só era avaliada quando chegava mensagem nova, que é
exatamente o que não acontece no caso que importa.

## Corrida de horário

Entre a oferta e a escolha do paciente, o horário pode ser ocupado por outro canal.
Antes de gravar, o caso de uso revalida a disponibilidade; havendo conflito, reoferta
sem quebrar a conversa.

## Regras da clínica

Vivem na seção `Clinica` da configuração, não no código: grade etária por
profissional, convênios de triagem humana, convênios só-consulta, termos de
urgência e de pedido de atendente, frase de transferência. São decisões da
clínica e mudam sem aviso; compilar cada uma significaria um deploy por
mudança de grade.

Duas têm precedência sobre qualquer estado da conversa: **urgência** (perda
súbita de audição) e **pedido de atendente** transferem na hora, em qualquer
ponto. **IPE e Unimed Unifácil** são checados antes de qualquer oferta, porque
a regra proíbe citar médicas e mostrar horários para esses convênios.

## Dados e LGPD

Agendamento de consulta é dado pessoal sensível. O banco guarda apenas identificação do
paciente, identificação do agendamento, data, hora, profissional e status. Nada de
diagnóstico, prontuário ou queixa.

| Tabela | Conteúdo |
|---|---|
| `pacientes` | espelho mínimo: codReferencia, nome, CPF, telefone, data de nascimento |
| `agendamentos` | espelho local, os dois identificadores Amplimed, status e marcos de envio |
| `conversas` | estado da máquina, dados coletados no fluxo (idade, convênio, preferência, nome e nascimento), e se a etiqueta está aplicada |
| `mensagens_log` | trilha de auditoria de envios e recebimentos |
| `opt_out` | descadastro, verificado antes de todo envio |
| `outbox` | eventos pendentes de reprocessamento |

A data de nascimento entrou no espelho por necessidade, não por conveniência: o
upsert do `cadastrar-agendamento` casa por CPF + data de nascimento quando o
`idUnico` ainda não existe na base, e sem ela o cadastro sairia duplicado.

A especialidade do profissional **não** é guardada, mesmo vindo na listagem da
agenda: o template de véspera não pode mencioná-la (Cláusula 13ª, § 2º), e o que
não tem finalidade não entra.

Retenção de 12 meses, com expurgo em `scripts/expurgo-lgpd.sql`.
