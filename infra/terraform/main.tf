# ---------------------------------------------------------------------------
# Rede de borda e rede de dados, separadas de propósito.
#
# A API fica nas duas; o proxy nunca alcança o Postgres e o Postgres nunca
# alcança a internet pela rede de borda. Com uma rede só, qualquer coisa que o
# proxy roteasse por engano chegaria no banco.
#
# A rede de borda só é criada quando o Traefik é nosso. Com proxy externo
# (Easypanel, nginx, outro Traefik já ocupando 80/443), a API entra na rede
# que o proxy já usa — criar outra só a deixaria inalcançável.
# ---------------------------------------------------------------------------

resource "docker_network" "edge" {
  count  = var.usar_traefik ? 1 : 0
  name   = "${var.projeto}-edge"
  driver = "bridge"
}

# Rede pré-existente do proxy. Não é criada aqui: só localizada, para o
# container da API poder entrar nela.
data "docker_network" "proxy_externo" {
  count = !var.usar_traefik && var.rede_proxy_externa != "" ? 1 : 0
  name  = var.rede_proxy_externa
}

resource "docker_network" "dados" {
  name     = "${var.projeto}-dados"
  driver   = "bridge"
  internal = true # sem rota para fora: o banco não precisa de internet
}

resource "docker_volume" "postgres_dados" {
  name = "${var.projeto}-postgres-dados"

  lifecycle {
    prevent_destroy = true # os dados dos pacientes vivem aqui
  }
}

resource "docker_volume" "traefik_acme" {
  count = var.usar_traefik ? 1 : 0
  name  = "${var.projeto}-traefik-acme"

  lifecycle {
    prevent_destroy = true # perder o acme.json queima cota de emissão
  }
}

# Senha gerada quando não informada. Sem símbolos: ";" e "=" quebram a string
# de conexão do Npgsql de um jeito que o erro não explica.
resource "random_password" "postgres" {
  count   = var.postgres_password == null ? 1 : 0
  length  = 32
  special = false
}

locals {
  postgres_password = var.postgres_password != null ? var.postgres_password : random_password.postgres[0].result

  # Host "postgres" é o alias do container na rede de dados.
  conexao_postgres = join(";", [
    "Host=postgres",
    "Port=5432",
    "Database=${var.postgres_db}",
    "Username=${var.postgres_user}",
    "Password=${local.postgres_password}",
  ])

  log_opts = {
    "max-size" = var.log_max_size
    "max-file" = var.log_max_file
  }

  admin_restrito = length(var.admin_ips) > 0

  # Rede pela qual o proxy alcança a API. Nossa quando o Traefik é nosso; a do
  # proxy existente caso contrário. Vazia quando o proxy externo fala com a API
  # pela porta publicada no host em vez de por rede docker.
  rede_proxy = var.usar_traefik ? docker_network.edge[0].name : var.rede_proxy_externa

  # Com proxy externo a API precisa de um alvo: ou a rede compartilhada, ou uma
  # porta no host. A porta fica em 127.0.0.1 — quem fala com a internet é o
  # proxy, nunca a API.
  publicar_no_host = !var.usar_traefik
}
