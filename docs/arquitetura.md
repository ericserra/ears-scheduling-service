# Arquitetura

## Visão geral

```
Paciente (WhatsApp)
        │
        ▼
   SleekFlow  ──webhook──►  EARS.Integration.Api
        ▲                          │
        └────Platform API──────────┤
                                   ├──► Amplimed — Cadastro de Pacientes
                                   ├──► Amplimed — Agendamento
                                   └──► Amplimed — Dados
                                   │
                              PostgreSQL
```

## Camadas

**Domain** — entidades, enums e value objects. Sem dependência de framework.
As invariantes ficam aqui: `Agendamento.ElegivelParaConfirmacao()`,
`OutboxMessage.RegistrarFalha()`, validação de `Cpf` e `Telefone`.

**Application** — casos de uso e contratos (`IAmplimed*Client`, `ISleekFlowClient`,
repositórios). Não conhece EF Core nem HttpClient.

**Infrastructure** — implementações: EF Core, clients HTTP com Polly, jobs do Quartz.

**Api** — composição, webhook e endpoints administrativos.

## As três APIs da Amplimed

Cada uma tem esquema de autenticação próprio — por isso três clients, não um:

| API | Autenticação | Uso |
|---|---|---|
| Cadastro de Pacientes | `x-Authorization: Basic {token}` | busca e criação de paciente |
| Agendamento | `X-authorization` + identificador | horários livres, marcação, cancelamento |
| Dados | `client_id`/`client_secret` → Bearer | profissionais, base de pacientes |

O client de Dados mantém o token em cache até expirar, com `SemaphoreSlim` para evitar
renovação concorrente.

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

```
Inicio
  └─► AguardandoIdentificacao   (nome completo)
        └─► AguardandoCpf        (valida e resolve o cadastro)
              └─► AguardandoProfissional
                    └─► AguardandoEscolhaHorario
                          └─► Concluida
```

De qualquer estado: `TransferidaParaHumano` (pedido explícito, resposta fora do
previsto ou falha inesperada) e `Expirada` (inatividade além da janela configurada).

O estado é persistido a cada turno: reiniciar o serviço não interrompe um agendamento
em andamento.

## Corrida de horário

Entre a oferta e a escolha do paciente, o horário pode ser ocupado por outro canal.
Antes de gravar, o caso de uso revalida a disponibilidade; havendo conflito, reoferta
sem quebrar a conversa.

## Dados e LGPD

Agendamento de consulta é dado pessoal sensível. O banco guarda apenas identificação do
paciente, identificação do agendamento, data, hora, profissional e status. Nada de
diagnóstico, prontuário ou queixa.

| Tabela | Conteúdo |
|---|---|
| `pacientes` | espelho mínimo: id Amplimed, nome, CPF, telefone |
| `agendamentos` | espelho local, status e marcos de envio |
| `conversas` | estado da máquina, dados coletados no fluxo |
| `mensagens_log` | trilha de auditoria de envios e recebimentos |
| `opt_out` | descadastro, verificado antes de todo envio |
| `outbox` | eventos pendentes de reprocessamento |

Retenção de 12 meses, com expurgo em `scripts/expurgo-lgpd.sql`.
