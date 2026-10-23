# Infraestrutura — EARS.Integration

Terraform que roda **dentro da VPS** e governa a plataforma: Traefik com TLS
automático, a API e o Postgres. A VPS em si é criada no painel da Hostinger; não
há provider de nuvem aqui.

```
internet ──443──► traefik ──┐
                            ├── rede edge ──► api ──┐
                            │                       ├── rede dados ──► postgres
                            └── (80 redireciona)    │
```

Duas redes: o Traefik não alcança o banco, e a rede de dados é `internal`, sem
rota para fora.

## Quando a VPS já tem um proxy

`usar_traefik = false` desliga o Traefik desta stack, para VPS que já serve 80 e
443 por Easypanel, nginx ou outro Traefik. Sem isso o container não sobe: as
portas já têm dono, e o `apply` falha.

```
internet ──443──► proxy existente ──┬── rede do proxy ──► api ──┐
                                    │                           ├─► postgres
                                    └── ou 127.0.0.1:8080       │
```

A API passa a ser alcançável de dois jeitos, e você usa o que o seu proxy
entender:

- **pela rede docker**, quando o proxy lê labels do container. Informe
  `rede_proxy_externa` com o nome da rede (veja em `docker network ls`) e as
  labels do Traefik já vão no container.
- **pela porta do host**, publicada em `127.0.0.1:8080` e nunca na internet. É
  o caminho do Easypanel, que roteia pela própria interface em vez de ler
  labels: crie lá o serviço apontando para essa porta.

Com proxy externo, TLS é responsabilidade dele: `acme_email` e `acme_staging`
não têm efeito, e as fases de certificado do roteiro não se aplicam.

---

## Antes de rodar

1. **VPS criada** na Hostinger, Ubuntu 22.04 ou 24.04, com acesso root por SSH.
2. **Registro A** do domínio apontando para o IP da VPS, **já propagado**.
   Confirme antes de prosseguir, porque é a causa número um de falha de emissão:

   ```bash
   dig +short api.exemplo.com.br
   ```

3. **Portas 80 e 443 abertas** no firewall. A Let's Encrypt valida pela 443
   (desafio TLS-ALPN); a 80 serve ao redirecionamento.
4. **Docker e Terraform** instalados na VPS:

   ```bash
   curl -fsSL https://get.docker.com | sh

   # Terraform (HashiCorp apt repo)
   apt-get update && apt-get install -y gnupg software-properties-common curl
   curl -fsSL https://apt.releases.hashicorp.com/gpg \
     | gpg --dearmor -o /usr/share/keyrings/hashicorp-archive-keyring.gpg
   echo "deb [signed-by=/usr/share/keyrings/hashicorp-archive-keyring.gpg] \
   https://apt.releases.hashicorp.com $(lsb_release -cs) main" \
     > /etc/apt/sources.list.d/hashicorp.list
   apt-get update && apt-get install -y terraform
   ```

5. **Repositório na VPS**, se for construir a imagem ali (o padrão):

   ```bash
   mkdir -p /opt/ears && git clone <repo> /opt/ears/src
   ```

---

## Subir

```bash
cd /opt/ears/src/infra/terraform

cp terraform.tfvars.example terraform.tfvars
chmod 600 terraform.tfvars
$EDITOR terraform.tfvars

terraform init
terraform plan
terraform apply
```

Com `acme_staging = true` (o padrão) o navegador vai acusar certificado
inválido — é esperado. Confirme que o roteamento funciona:

```bash
curl -k https://api.exemplo.com.br/health
```

Respondendo, vire `acme_staging = false` no tfvars e rode `terraform apply` de
novo. Aí o certificado passa a ser confiável.

> **Por que começar em staging.** O limite de produção da Let's Encrypt é de 5
> falhas por conta por hora. Errar o DNS com staging desligado trava a emissão
> pelo resto da hora, e você fica sem como testar.

### Depois do apply

```bash
terraform output url_webhook              # cadastre no SleekFlow
terraform output cenario_clausula_terceira
terraform output -raw postgres_password
```

---

## Atualizar a aplicação

```bash
cd /opt/ears/src && git pull
cd infra/terraform
terraform apply -var="app_versao=$(git -C /opt/ears/src rev-parse --short HEAD)"
```

**Use sempre o SHA do commit.** A versão é o gatilho do rebuild: o conteúdo do
diretório **não** é observado, porque hashear a árvore a cada `plan` seria lento
e mudaria a cada arquivo gerado pelo build. Repetir o apply com a mesma versão
não reconstrói nada.

O modo de errar é específico e silencioso: você dá `git pull`, roda
`terraform apply`, o Terraform recria o container porque alguma variável de
ambiente mudou, e você fica com **código novo no disco e imagem velha rodando**.
O sintoma é um endpoint recém-criado respondendo 404 pelo Kestrel. Amarrar a
versão ao commit elimina a classe inteira do problema.

Para mudar só configuração — cron, janela de envio, template — use `env_extra` e
dê apply: o container é recriado com o novo ambiente, sem rebuild.

---

## O que este Terraform **não** faz

- **Backup e expurgo LGPD.** `scripts/backup.sh` (diário) e
  `scripts/expurgo-lgpd.sql` (mensal) continuam fora daqui. Até entrarem, agende
  no cron do host. Sem backup, o volume `ears-postgres-dados` é o único lugar
  onde os dados existem.
- **Observabilidade.** Nada vigia `/health`. Uma queda noturna só aparece quando
  a recepção reclamar que ninguém recebeu a confirmação.
- **DNS.** O registro A é manual no painel da Hostinger. O provider Terraform da
  Hostinger existe e cobre DNS, mas rodaria da sua máquina, não daqui — é outra
  camada, com outro state. Dá para acrescentar depois.

---

## Traefik precisa ser v3.6.1 ou mais novo

Até a v3.6.0 o provider do Docker pedia a API na versão 1.24, fixa, sem
negociar. O Docker 29 subiu o mínimo aceito para 1.40, e o provider morre com:

```
ERR Provider error, retrying in 1.5s
    error="client version 1.24 is too old. Minimum supported API version is 1.40"
```

O sintoma engana: os três containers sobem e ficam `healthy`, o TLS responde,
e **tudo devolve 404** — porque sem o provider o Traefik não conhece rota
nenhuma. Olhar só o `docker ps` não mostra o problema; o log do Traefik sim.

Se por algum motivo você precisar manter um Traefik antigo, o paliativo é do
lado do daemon, em `/etc/docker/daemon.json`:

```json
{ "min-api-version": "1.24" }
```

Valide com `dockerd --validate` e reinicie o Docker. É um downgrade que vale
para todos os clientes da máquina, então sirva-se dele só até atualizar.

## Duas coisas para saber antes de confiar

**O state guarda os segredos em texto puro.** Tokens da Amplimed, chave do
SleekFlow, chave de admin e senha do banco ficam legíveis no
`terraform.tfstate`. `sensitive = true` esconde do output do terminal, não do
arquivo. Então:

```bash
chmod 600 terraform.tfstate terraform.tfvars
```

O `.gitignore` já barra os dois. O state vive na VPS, o que também significa que
**perdê-lo custa**: sem ele o Terraform não sabe o que existe e tenta recriar
tudo. Inclua `/opt/ears/src/infra/terraform/terraform.tfstate` na rotina de
backup. Se isso incomodar, o caminho é mover os segredos para SOPS ou ler de
arquivo fora do state.

**Uma instância, e só uma.** O Quartz usa store em memória (ADR 0001): duas
instâncias disparam a mesma confirmação de véspera duas vezes, e o paciente
recebe a mensagem duplicada. Não replique o container `ears-api` sem antes mover
o Quartz para store no banco.

---

## Conflito com o docker-compose.yml

O `docker-compose.yml` da raiz sobe a mesma coisa. Rodar os dois contra o mesmo
host gera containers e volumes concorrentes com nomes diferentes, e metade do
tráfego vai para o lugar errado — o tipo de problema que consome uma tarde.

Escolha um por ambiente: o compose para desenvolvimento na sua máquina, este
Terraform na VPS. Vale deixar isso dito no README da raiz.

---

## Operação do dia a dia

```bash
docker logs -f ears-api
docker logs -f ears-traefik          # falhas de ACME aparecem aqui
docker exec -it ears-postgres psql -U ears -d ears

terraform state list
terraform apply -replace=docker_container.api   # recriar só a API
```

Rotação de logs já está configurada: 3 arquivos de 10 MB por container.

O `docker ps` mostra o estado de saúde da API: o healthcheck vem do Dockerfile e
consulta `/health`, que por sua vez checa a API e o Postgres. `terraform apply`
espera esse estado virar `healthy` antes de terminar, então um deploy quebrado
falha no apply em vez de ficar reiniciando calado.
