# ---------------------------------------------------------------------------
# Duas redes, de propósito.
#
# A API fica nas duas; o Traefik nunca alcança o Postgres e o Postgres nunca
# alcança a internet pela rede do proxy. Com uma rede só, qualquer coisa que o
# Traefik roteasse por engano chegaria no banco.
# ---------------------------------------------------------------------------

resource "docker_network" "edge" {
  name   = "${var.projeto}-edge"
  driver = "bridge"
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
  name = "${var.projeto}-traefik-acme"

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
}
