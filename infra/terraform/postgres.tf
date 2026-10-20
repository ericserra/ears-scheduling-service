resource "docker_image" "postgres" {
  name         = var.postgres_image
  keep_locally = true
}

resource "docker_container" "postgres" {
  name     = "${var.projeto}-postgres"
  image    = docker_image.postgres.image_id
  restart  = "unless-stopped"
  must_run = true

  # Espera o healthcheck passar antes de seguir. É o que garante que a API só
  # suba com o banco pronto: ela aplica as migrações do EF na inicialização e
  # morreria se o Postgres ainda estivesse aceitando conexões.
  wait         = true
  wait_timeout = 180

  env = [
    "POSTGRES_DB=${var.postgres_db}",
    "POSTGRES_USER=${var.postgres_user}",
    "POSTGRES_PASSWORD=${local.postgres_password}",
    "TZ=${var.fuso_horario}",
    "PGTZ=${var.fuso_horario}",
  ]

  volumes {
    volume_name    = docker_volume.postgres_dados.name
    container_path = "/var/lib/postgresql/data"
  }

  networks_advanced {
    name    = docker_network.dados.name
    aliases = ["postgres"]
  }

  # Publicado somente em 127.0.0.1 quando pedido: acesso por túnel SSH, nunca
  # exposto na internet.
  dynamic "ports" {
    for_each = var.postgres_porta_local == null ? [] : [var.postgres_porta_local]
    content {
      internal = 5432
      external = ports.value
      ip       = "127.0.0.1"
    }
  }

  healthcheck {
    test         = ["CMD-SHELL", "pg_isready -U ${var.postgres_user} -d ${var.postgres_db}"]
    interval     = "10s"
    timeout      = "5s"
    retries      = 6
    start_period = "20s"
  }

  log_driver = "json-file"
  log_opts   = local.log_opts
}
