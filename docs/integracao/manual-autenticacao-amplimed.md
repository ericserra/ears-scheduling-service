# Manual de autenticação — API de Dados da Amplimed

Documento recebido do cliente em 23/10/2026. Reproduzido aqui como referência
da integração, com as credenciais substituídas por marcadores.

> **Escopo.** Este manual cobre **apenas a API de Dados** (`hr.amplimed.com.br`),
> que é somente leitura. As APIs de **Agendamento** e de **Cadastro de Pacientes**,
> que o motor EARS usa para escrever, vivem em outro gateway
> (`integracoes.amplimed.com.br`), têm credenciais próprias e não estão cobertas
> aqui. Ver `docs/adr/0002-leitura-de-agenda.md` e a coleção em `docs/postman/`.

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

As credenciais da EARS são de **produção**.

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

A resposta traz `expires_at`, não `expires_in`. O token dura cerca de uma hora.
Guardar e reutilizar, renovando uns cinco minutos antes de expirar. Um `401` numa
chamada de dados pede um novo login e uma repetição da chamada.

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
| `401` no login | Credenciais erradas, ou campos em snake_case | Conferir valores e usar camelCase |
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
