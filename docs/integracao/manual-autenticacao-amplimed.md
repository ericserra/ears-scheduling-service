# Manual de autenticação — API de Dados da Amplimed

Documento recebido do cliente em 23/10/2026. Reproduzido aqui como referência
da integração, com as credenciais substituídas por marcadores.

> **Escopo.** Este manual cobre **apenas a API de Dados** (`hr.amplimed.com.br`),
> que é somente leitura. O **gateway de Integrações**
> (`integracoes.amplimed.com.br`), que é por onde o motor EARS escreve, usa outra
> credencial. Ver a seção 2.1, a coleção em `docs/postman/` e
> `docs/adr/0002-leitura-de-agenda.md`.

## 1. Visão geral

Login por Client ID + Client Secret, que devolve um token **Bearer**. O token vai
no header `Authorization` das chamadas seguintes.

```
POST /oauth/login  ──►  access_token (Bearer)  ──►  GET /data-api/...
```

## 2. Ambientes

| Ambiente | URL base |
|---|---|
| **Produção** | `https://hr.amplimed.com.br` |
| Homologação | `https://stghr.amplimed.cloud` |
| Desenvolvimento | `https://devhr.amplimed.cloud` |

São hosts diferentes dos do gateway de Agendamento e Pacientes
(`integracoes.amplimed.com.br`, `stgintegracoes.amplimed.cloud`). Não confundir.

As credenciais da EARS são de **produção**. Uma integração nova deve começar lendo
dados (só `GET`) e nunca assumir que um ambiente de testes tem os mesmos dados.

## 2.1 Existem DUAS APIs, com credenciais diferentes

| | **API de Dados** (`hr`) | **Gateway de Integrações** |
|---|---|---|
| Host (produção) | `https://hr.amplimed.com.br` | `https://integracoes.amplimed.com.br` |
| Rotas | `/oauth/login`, `/data-api/...` | `/integracoes/referencia/{identifier}/...` e `/pacientes/integration/pattient` |
| Autenticação | `clientId` + `clientSecret` → `Authorization: Bearer` | `x-authorization` (agendamento) e `x-Authorization: Basic` (pacientes), mais o `identifier` no caminho |
| Credenciais da EARS | **Temos** | **Não temos.** Precisam ser emitidas pela Amplimed |

**O Bearer da API de Dados não funciona no gateway.** Testado em 23/10/2026:

- `GET /pacientes/integration/pattient` com o Bearer do `hr` →
  `401 {"statusCode":401,"message":"Wrong credentials!","error":"Unauthorized"}`
- `GET /integracoes/referencia/{identifier}/{clinica}/listar-agendamentos` com Bearer,
  com header inválido ou sem nada → `401 {"status":"error","message":"Unauthorized"}`

O que falta pedir à Amplimed para a clínica 57622, e que no nosso ambiente
corresponde a:

| Variável do motor | É o quê |
|---|---|
| `Amplimed__Agendamento__Identificador` | o `identifier` do caminho da URL de agendamento |
| `Amplimed__Agendamento__Token` | valor do header `x-authorization` |
| `Amplimed__Pacientes__Token` | valor do header `x-Authorization: Basic ...` |

Sem esses três, o gateway não responde, e o motor recusa atendimento com 503 em
vez de atender pela metade.

**Leitura sem o gateway.** A API de Dados já validada em produção cobre:

| Necessidade | Rota |
|---|---|
| Agendamentos | `GET /data-api/list-appointments/{clinicId}` |
| Pacientes | `GET /data-api/list-patients/{clinicId}` |

Escrita (criar ou alterar agendamento, cadastrar paciente) depende do gateway.

## 3. Credenciais

| Item | Valor | Observação |
|---|---|---|
| `clientId` | `<CLIENT_ID>` | Entregue por canal seguro |
| `clientSecret` | `<CLIENT_SECRET>` | Segredo. Nunca em chat, e-mail, repositório ou front-end |
| `clinicId` / `usuclin` | `57622` | ID da clínica EARS. Vai na URL. Não é segredo |

No nosso ambiente isso corresponde a `Amplimed__Agendamento__Usuclin = 57622`,
configurado via `terraform.tfvars`, nunca versionado.

## 4. Login

```
POST https://hr.amplimed.com.br/oauth/login
Content-Type: application/json

{ "clientId": "<CLIENT_ID>", "clientSecret": "<CLIENT_SECRET>" }
```

Campos em **camelCase**. Em snake_case a API devolve erro de validação.

Resposta de sucesso é **HTTP 201**, não 200:

```json
{
  "access_token": "eyJ...",
  "token_type": "Bearer",
  "expires_at": "2026-10-23 21:19:54"
}
```

A resposta traz `expires_at`, não `expires_in`, **em UTC**. O JWT dura **3 horas**,
confirmado em 23/10/2026. Guardar e reutilizar, renovando uns cinco minutos antes
de expirar. Um `401` numa chamada de dados pede um novo login e uma repetição da
chamada.

O corpo precisa ser JSON. Form-urlencoded ou `snake_case` devolvem
`400 "The client id field is required"`.

O `clientId` da EARS contém `&` e `_`. Em JSON não é problema, mas quebra se for
montado em query string sem escape, ou colado em shell sem aspas simples: o `&`
manda o comando para segundo plano e corta o valor.

## 5. Chamadas de dados

```
Authorization: Bearer <access_token>
Accept: application/json
```

### Testar o acesso

```
GET https://hr.amplimed.com.br/data-api/list-clinics
```

```json
{ "success": true, "message": "Dados recuperados com sucesso.",
  "data": { "usuclins": [57622] },
  "pagination": { "current_page": 1, "last_page": 1, "per_page": 10, "total": 1 } }
```

### Endpoints em uso

| Endpoint | Para quê | Parâmetros úteis |
|---|---|---|
| `/data-api/list-patients/{clinicId}` | Dados do paciente | `codp`, `page`, `pageSize` |
| `/data-api/list-appointments/{clinicId}` | Agendamentos | `id-paciente`, `datahora-inicial` (`YYYY-MM-DD 00:00:00`), `page`, `pageSize` |
| `/data-api/financial/{clinicId}` | Dados financeiros | ver especificação da Amplimed |

### Paginação

Respostas de lista trazem `pagination`. Incrementar `page` até
`current_page == last_page`.

### Limite de requisições

Em `429`, backoff exponencial. No motor isso já está coberto por
`PoliticasHttp.Retry`: três tentativas em 2s, 4s e 8s, com `TooManyRequests`
tratado explicitamente. Evitar varrer a base inteira: consultar por `codp` ou
por período.

## 6. Erros comuns

| Sintoma | Causa provável | Solução |
|---|---|---|
| `404 {"msg":"Client not found or inactive"}` no login | Credenciais não existem ou foram desativadas | Conferir os valores, atenção ao `&` do clientId |
| `400 {"msg":{"clientId":[...]}}` | Corpo não é JSON, ou campos em snake_case | `Content-Type: application/json` e camelCase |
| `500 "Invalid JWT: Wrong number of segments"` | Header `Authorization` com token malformado ou vazio | Conferir `Bearer <token>` completo |
| `401 {"message":"Wrong credentials!"}` | É o **gateway**, rota de pacientes, recusando o Bearer da API de Dados | Seção 2.1 |
| `401 {"status":"error","message":"Unauthorized"}` | Gateway, rota de agendamento, sem `x-authorization` válido ou `identifier` errado | Seção 2.1 |
| `401` numa chamada de dados | Token expirado ou sem o prefixo `Bearer ` | Refazer o login, conferir o header |
| `403` ou lista vazia | `clinicId` errado | Usar o que veio de `list-clinics` |
| `429` | Rate limit | Backoff exponencial, reduzir volume |
| `CERTIFICATE_VERIFY_FAILED` | Antivírus interceptando TLS na máquina de desenvolvimento | Apontar `REQUESTS_CA_BUNDLE` / `SSL_CERT_FILE` para um bundle que inclua o certificado do antivírus. Nunca desligar a verificação |

## 7. Segurança

O `clientSecret` dá acesso a dados de pacientes, sujeitos à LGPD. Tratar como
senha: só em cofre ou variável de ambiente, nunca no código, no front-end ou em
log. Não registrar o `access_token` completo em log. Em caso de vazamento ou
troca de quem opera o projeto, pedir à Amplimed um novo par e atualizar todos os
sistemas que o usam, porque o sync do cliente compartilha essas credenciais.

Usar somente os endpoints de leitura de que a integração precisa.
