# Terraform roda DENTRO da VPS, conversando com o Docker local pelo socket.
# Não há provider de nuvem aqui de propósito: a VPS da Hostinger é criada no
# painel, e o que o Terraform governa é a plataforma que roda nela.
#
# Consequência: o state vive na VPS. Guarde-o (ver README) — sem ele o
# Terraform não sabe o que já existe e tenta recriar tudo.

terraform {
  required_version = ">= 1.6.0"

  required_providers {
    docker = {
      source  = "kreuzwerker/docker"
      version = "~> 3.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
}

provider "docker" {
  host = var.docker_host
}
