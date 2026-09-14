# EARS.Integration

Motor de automação que conecta a agenda da **Amplimed** ao WhatsApp da Clínica EARS,
operado via **SleekFlow**.

Dois fluxos:

1. **Agendamento pela conversa** — o paciente marca consulta pelo WhatsApp e o horário
   é gravado direto na agenda da Amplimed.
2. **Confirmação de véspera (D-1)** — rotina diária envia template com botões
   Confirmar / Remarcar / Cancelar e atualiza o agendamento conforme a resposta.

---

## Stack

| Camada | Tecnologia |
|---|---|
| Runtime | .NET 8 · ASP.NET Core Minimal API |
| Agendamento | Quartz.NET (hosted service) |
| Persistência | PostgreSQL 16 · EF Core 8 |
| Resiliência | HttpClientFactory + Polly (retry, timeout, circuit breaker) |
| Observabilidade | Serilog · health checks |
| Empacotamento | Docker · docker compose |

## Estrutura

```
src/
├── EARS.Integration.Domain/          entidades, enums e value objects — sem dependências
├── EARS.Integration.Application/     casos de uso, contratos e DTOs
├── EARS.Integration.Infrastructure/  EF Core, clients HTTP, jobs do Quartz
└── EARS.Integration.Api/             webhook, endpoints administrativos e composição
tests/
└── EARS.Integration.UnitTests/       domínio e regras de negócio
```

A dependência aponta sempre para dentro: `Api → Infrastructure → Application → Domain`.

## Como rodar

### Local

```bash
cp .env.example .env     # preencha as credenciais
docker compose up -d postgres
dotnet run --project src/EARS.Integration.Api
```

A API sobe em `http://localhost:5080`. O Swagger fica em `/swagger` (só em Development).

### Container

```bash
cp .env.example .env
docker compose up -d --build
curl -fsS http://localhost:8080/health
```

### Migrações

As migrações são aplicadas automaticamente na subida. Para criar uma nova:

```bash
dotnet ef migrations add NomeDaMigracao \
  --project src/EARS.Integration.Infrastructure \
  --startup-project src/EARS.Integration.Api
```

> A primeira migração ainda não foi gerada — rode o comando acima com o banco no ar
> antes do primeiro deploy.

## Configuração

Tudo por variável de ambiente, no padrão `Secao__Chave` (ver `.env.example`).
Nenhum segredo vai para o repositório.

Parâmetros de automação ajustáveis sem recompilar:

| Chave | Padrão | O que faz |
|---|---|---|
| `Automacao__CronConfirmacao` | `0 0 17 * * ?` | horário do disparo D-1 |
| `Automacao__CronLembrete` | `0 30 7 * * ?` | horário do lembrete único |
| `Automacao__MaxHorariosOfertados` | `5` | opções mostradas ao paciente |
| `Automacao__ExpiracaoConversa` | `06:00:00` | inatividade que encerra a conversa |
| `Automacao__LeituraAgendaViaApi` | `false` | cenário A ou B da Cláusula 3ª — ver ADR 0002 |

## Endpoints

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/health` | health check (API + Postgres) |
| `POST` | `/webhooks/sleekflow` | recebe mensagens e respostas de botão |
| `GET` | `/admin/agendamentos?dia=` | agendamentos do dia com status |
| `GET` | `/admin/logs?pagina=` | trilha de auditoria de mensagens |
| `POST` | `/admin/confirmacao/disparar` | dispara a varredura D-1 manualmente |
| `POST` | `/admin/opt-out?telefone=` | registra descadastro |
| `GET` | `/admin/config` | parâmetros em vigor |

Os endpoints `/admin` exigem o cabeçalho `X-Api-Key`.

## Decisões de projeto

- **[ADR 0001](docs/adr/0001-host-unico.md)** — API e worker no mesmo host.
  Consequência importante: **rodar em instância única** enquanto o Quartz usar store
  em memória, ou os jobs disparam em duplicidade.
- **[ADR 0002](docs/adr/0002-leitura-de-agenda.md)** — os dois cenários de leitura de
  agenda previstos na Cláusula 3ª, alternados por configuração.

## Regras que não são óbvias no código

- **Identificação de paciente usa CPF e telefone em conjunto.** Telefone isolado não
  desambigua pacientes que compartilham o mesmo número — problema já enfrentado no
  serviço GMC. A criação de cadastro só ocorre quando não há correspondência alguma.
- **Fora da janela de 24h da Meta só se envia template aprovado.** O disparo D-1 é
  sempre template; texto livre só dentro da janela.
- **O template não menciona especialidade nem motivo da consulta** (Cláusula 13ª, § 2º):
  a notificação pode aparecer na tela de bloqueio e ser vista por terceiros.
- **Resposta ambígua nunca vira confirmação.** O botão é a fonte confiável; texto livre
  só é aceito quando inequívoco. Na dúvida, escala para atendimento humano.
- **Não há terceira mensagem.** Após o lembrete único, o agendamento é marcado como não
  confirmado e vira tarefa da recepção.

## Operação

| Rotina | Script | Cadência sugerida |
|---|---|---|
| Backup do banco | `scripts/backup.sh` | diária, 3h |
| Expurgo LGPD (12 meses) | `scripts/expurgo-lgpd.sql` | mensal |

## Testes

```bash
dotnet test
```

---

Strenos · Eric de Souza Serra
