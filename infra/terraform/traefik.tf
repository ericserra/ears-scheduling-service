# Todo este arquivo só existe quando usar_traefik = true. Com proxy externo,
# nada aqui é criado: as portas 80 e 443 já têm dono.

resource "docker_image" "traefik" {
  count        = var.usar_traefik ? 1 : 0
  name         = "traefik:v3.1"
  keep_locally = true
}

resource "docker_container" "traefik" {
  count    = var.usar_traefik ? 1 : 0
  name     = "${var.projeto}-traefik"
  image    = docker_image.traefik[0].image_id
  restart  = "unless-stopped"
  must_run = true

  # Configuração estática por argumento de linha de comando. A parte dinâmica
  # (rotas, middlewares, certificado) vem das labels do container da API.
  command = concat(
    [
      "--log.level=INFO",
      "--accesslog=true",

      # Dashboard desligado. Ligá-lo sem autenticação publica a topologia
      # inteira do proxy; se precisar, acesse por túnel SSH em vez de abrir.
      "--api.dashboard=false",

      "--providers.docker=true",
      "--providers.docker.exposedbydefault=false",

      # A API está em duas redes; sem isto o Traefik pode escolher a de dados e
      # tentar rotear por um endereço que não alcança.
      "--providers.docker.network=${local.rede_proxy}",

      "--entrypoints.web.address=:80",
      "--entrypoints.web.http.redirections.entrypoint.to=websecure",
      "--entrypoints.web.http.redirections.entrypoint.scheme=https",
      "--entrypoints.web.http.redirections.entrypoint.permanent=true",

      "--entrypoints.websecure.address=:443",

      "--certificatesresolvers.le.acme.email=${var.acme_email}",
      "--certificatesresolvers.le.acme.storage=/letsencrypt/acme.json",
      "--certificatesresolvers.le.acme.tlschallenge=true",
    ],
    var.acme_staging
      ? ["--certificatesresolvers.le.acme.caserver=https://acme-staging-v02.api.letsencrypt.org/directory"]
      : [],
  )

  ports {
    internal = 80
    external = 80
  }

  ports {
    internal = 443
    external = 443
  }

  # Somente leitura: o Traefik precisa enxergar containers e labels, não criar
  # nem matar nada. Acesso de escrita ao socket equivale a root no host.
  volumes {
    host_path      = "/var/run/docker.sock"
    container_path = "/var/run/docker.sock"
    read_only      = true
  }

  volumes {
    volume_name    = docker_volume.traefik_acme[0].name
    container_path = "/letsencrypt"
  }

  networks_advanced {
    name = docker_network.edge[0].name
  }

  env = ["TZ=${var.fuso_horario}"]

  log_driver = "json-file"
  log_opts   = local.log_opts
}
