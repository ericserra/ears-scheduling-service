# ---------------------------------------------------------------------------
# Identidade e acesso ao Docker
# ---------------------------------------------------------------------------

variable "projeto" {
  description = "Prefixo de todos os recursos criados (containers, redes, volumes)."
  type        = string
  default     = "ears"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,20}$", var.projeto))
    error_message = "Use minúsculas, dígitos e hífen, começando por letra."
  }
}

variable "docker_host" {
  description = "Socket do Docker. Rodando na própria VPS, o padrão serve."
  type        = string
  default     = "unix:///var/run/docker.sock"
}

variable "fuso_horario" {
  description = "Fuso dos containers. Precisa casar com Automacao__FusoHorario."
  type        = string
  default     = "America/Sao_Paulo"
}

# ---------------------------------------------------------------------------
# Domínio e TLS
# ---------------------------------------------------------------------------

variable "dominio" {
  description = <<-TXT
    Domínio público da API, onde o webhook do SleekFlow vai bater.
    PRÉ-REQUISITO: um registro A apontando para o IP da VPS já propagado.
    Sem isso o desafio TLS-ALPN da Let's Encrypt falha e o Traefik serve
    certificado inválido.
  TXT
  type = string

  validation {
    condition     = can(regex("^[a-z0-9.-]+\\.[a-z]{2,}$", var.dominio))
    error_message = "Informe um domínio, sem esquema e sem barra final."
  }
}

variable "usar_traefik" {
  description = <<-TXT
    true cria o Traefik desta stack, com TLS automático, ocupando 80 e 443.

    false para VPS que já tem proxy nessas portas — Easypanel, nginx, outro
    Traefik. Nesse caso nada de proxy é criado aqui: a API entra na rede do
    proxy existente (rede_proxy_externa) e também é publicada em
    127.0.0.1:porta_host_api, para servir de alvo tanto a proxy que lê labels
    do docker quanto a proxy que roteia por configuração própria.

    Com false, acme_email e acme_staging não têm efeito: quem emite e renova
    certificado é o proxy que já está lá.
  TXT
  type    = bool
  default = true
}

variable "traefik_image" {
  description = <<-TXT
    Imagem do Traefik.

    Não desça abaixo da v3.6.1. Até a v3.6.0 o provider pedia a API do Docker
    na versão 1.24, fixa, sem negociar; o Docker 29 subiu o mínimo para 1.40 e
    o provider morre com "client version 1.24 is too old". O sintoma é cruel:
    os containers sobem saudáveis, mas nenhuma rota existe e tudo responde 404.
  TXT
  type    = string
  default = "traefik:v3.6.1"
}

variable "rede_proxy_externa" {
  description = <<-TXT
    Nome da rede docker do proxy existente, quando usar_traefik = false.
    A rede precisa existir; este Terraform só entra nela.

    Descubra com: docker network ls
    No Easypanel costuma ser "easypanel".

    Vazio faz a API ser alcançada apenas pela porta publicada em 127.0.0.1.
  TXT
  type    = string
  default = ""
}

variable "porta_host_api" {
  description = <<-TXT
    Porta no host onde a API fica disponível quando o proxy é externo.
    Publicada só em 127.0.0.1: quem atende a internet é o proxy.
  TXT
  type    = number
  default = 8080
}

variable "acme_email" {
  description = "E-mail de contato da Let's Encrypt. Ignorado quando usar_traefik = false."
  type        = string
  default     = ""
}

variable "acme_staging" {
  description = <<-TXT
    true usa o ambiente de staging da Let's Encrypt: certificado não confiável
    pelo navegador, mas sem limite de emissão. Suba a primeira vez com true,
    confirme que o roteamento funciona, depois vire para false.

    O limite de produção é de 5 falhas por conta, por hora: errar o DNS com
    staging desligado trava a emissão pelo resto da hora.
  TXT
  type    = bool
  default = true
}

# ---------------------------------------------------------------------------
# Imagem da aplicação
# ---------------------------------------------------------------------------

variable "app_image" {
  description = <<-TXT
    Imagem pronta a usar (ex.: "ghcr.io/strenos/ears-integration:1.0.3").
    Deixe null para o Terraform construir a partir do Dockerfile do repositório
    em app_contexto — que é o caminho natural numa VPS única, sem registry.
  TXT
  type    = string
  default = null
}

variable "app_contexto" {
  description = "Raiz do repositório na VPS, usada no build quando app_image é null."
  type        = string
  default     = "/opt/ears/src"
}

variable "app_versao" {
  description = <<-TXT
    Tag e gatilho de rebuild. Mudar este valor é o que faz o Terraform
    reconstruir a imagem e recriar o container; o conteúdo do diretório não é
    observado (varrer a árvore a cada plan seria lento e instável).
  TXT
  type    = string
  default = "1.0.0"
}

variable "app_porta" {
  description = "Porta onde a API escuta dentro do container. Casa com ASPNETCORE_URLS."
  type        = number
  default     = 8080
}

variable "ambiente" {
  description = "ASPNETCORE_ENVIRONMENT. Em Development o Swagger fica exposto."
  type        = string
  default     = "Production"

  validation {
    condition     = contains(["Production", "Staging", "Development"], var.ambiente)
    error_message = "Use Production, Staging ou Development."
  }
}

# ---------------------------------------------------------------------------
# Banco
# ---------------------------------------------------------------------------

variable "postgres_image" {
  description = "Imagem do Postgres. O serviço foi escrito para a 16."
  type        = string
  default     = "postgres:16-alpine"
}

variable "postgres_db" {
  type    = string
  default = "ears"
}

variable "postgres_user" {
  type    = string
  default = "ears"
}

variable "postgres_password" {
  description = <<-TXT
    Senha do Postgres. Deixe null para o Terraform gerar uma de 32 caracteres
    alfanuméricos — sem símbolos de propósito: ";" e "=" quebram a string de
    conexão do Npgsql, e o erro resultante não diz o que aconteceu.
  TXT
  type      = string
  default   = null
  sensitive = true
}

variable "postgres_porta_local" {
  description = <<-TXT
    Porta no host para acessar o banco, publicada SOMENTE em 127.0.0.1 — você
    chega nela por túnel SSH, não pela internet. null não publica nada, que é o
    padrão correto para produção.
  TXT
  type    = number
  default = null
}

# ---------------------------------------------------------------------------
# Segredos da aplicação
# ---------------------------------------------------------------------------

variable "admin_api_key" {
  description = "Cabeçalho X-Api-Key dos endpoints /admin."
  type        = string
  sensitive   = true

  validation {
    condition     = length(var.admin_api_key) >= 24
    error_message = "Use pelo menos 24 caracteres — esse endpoint lê a trilha de mensagens."
  }
}

variable "admin_ips" {
  description = <<-TXT
    Faixas CIDR autorizadas a alcançar /admin (ex.: ["203.0.113.4/32"]).

    Lista vazia significa que /admin fica acessível de qualquer IP, protegido
    apenas pela chave de API. Funciona, mas um endpoint que expõe a trilha de
    mensagens de pacientes merece as duas camadas. Vale preencher.
  TXT
  type    = list(string)
  default = []
}

variable "amplimed_base_url" {
  description = <<-TXT
    Raiz do gateway da Amplimed, com barra final. Serve às duas APIs em uso,
    a de Agendamento e a de Cadastro de Pacientes.

    Ambientes documentados: https://devintegracoes.amplimed.cloud/,
    https://qaintegracoes.amplimed.cloud/, https://stgintegracoes.amplimed.cloud/
    e https://integracoes.amplimed.com.br/ (produção). Qual deles vale para a
    chave recebida é informação da Amplimed, não dá para deduzir.
  TXT
  type        = string
  default     = "https://integracoes.amplimed.com.br/"
}

variable "amplimed_pacientes_token" {
  # Opcional de propósito: o ambiente sobe antes das credenciais chegarem.
  # Vazio faz o motor recusar atendimento (503) em vez de atender pela metade.
  default     = ""
  description = <<-TXT
    Token da API de Cadastro de Pacientes, do e-mail "Credenciais de
    Integração" da Amplimed. Cole o valor como veio, com ou sem o prefixo
    "Basic ": o motor normaliza e manda um prefixo só.
  TXT
  type        = string
  sensitive   = true
}

variable "amplimed_agendamento_identificador" {
  description = <<-TXT
    Identificador da API de Agendamento, do e-mail da Amplimed. Vai no CAMINHO
    da rota, depois de /integracoes/referencia/, nunca em cabeçalho. Para a
    EARS é "integracao-ears".
  TXT
  type        = string
  sensitive   = true
  default     = ""
}

variable "amplimed_agendamento_token" {
  # Opcional de propósito: o ambiente sobe antes das credenciais chegarem.
  # Vazio faz o motor recusar atendimento (503) em vez de atender pela metade.
  default     = ""
  description = <<-TXT
    Token da API de Agendamento, do mesmo e-mail. Vai no cabeçalho
    x-authorization EXATAMENTE como configurado aqui, sem normalização, porque
    a documentação não diz se essa rota espera o prefixo. Comece colando o
    valor inteiro ("Basic abc123"); se der 401, tente só a parte depois do
    "Basic ".
  TXT
  type        = string
  sensitive   = true
}

variable "amplimed_usuclin" {
  description = <<-TXT
    Número da clínica na Amplimed. Obrigatório em listar-agendamentos e
    alterar-status-agendamento: sem ele o cenário A da Cláusula 3ª não roda e a
    confirmação cobre apenas o que foi marcado pelo WhatsApp.
  TXT
  type    = string
  default = ""
}

variable "sleekflow_base_url" {
  description = <<-TXT
    Raiz da API da SleekFlow, com barra final. O padrão é a Platform API de
    produção, que autentica pelo cabeçalho X-Sleekflow-Api-Key.

    Trocar esta raiz só resolve se o outro ambiente falar o MESMO dialeto:
    mesmas rotas (api/message/send/json, api/contact/addOrUpdate,
    api/whatsapp/reply-window-status) e o mesmo cabeçalho. O produto novo da
    SleekFlow (https://api.onsleek.ai/) NÃO serve aqui: usa Authorization:
    Bearer e outras rotas, então apontar para lá troca o 401 por 404.
  TXT
  type    = string
  default = "https://api.sleekflow.io/"
}

variable "sleekflow_api_key" {
  description = "Chave da Platform API. Vai no cabeçalho X-Sleekflow-Api-Key, não como Bearer."
  type        = string
  sensitive   = true
  default     = ""
}

variable "sleekflow_canal" {
  description = "Canal da conta: whatsappcloudapi, whatsapp360dialog ou whatsapp (Twilio). Decide o formato de template e de botões."
  type        = string
  default     = "whatsappcloudapi"

  validation {
    condition     = contains(["whatsappcloudapi", "whatsapp360dialog", "whatsapp"], var.sleekflow_canal)
    error_message = "Use whatsappcloudapi, whatsapp360dialog ou whatsapp."
  }
}

variable "sleekflow_remetente" {
  description = <<-TXT
    Número remetente da clínica, em formato internacional sem o "+".
    Obrigatório: o endpoint de envio exige o campo "from", e sem ele nenhuma
    mensagem sai.
  TXT
  type    = string
  default = ""

  validation {
    condition     = var.sleekflow_remetente == "" || can(regex("^[0-9]{10,15}$", var.sleekflow_remetente))
    error_message = "Só dígitos, com DDI e sem o sinal de mais. Ex.: 5551999998888. Vazio é aceito enquanto o número não chega."
  }
}

variable "sleekflow_template_namespace" {
  description = "Namespace dos templates. Só o 360dialog exige."
  type        = string
  default     = ""
}

variable "sleekflow_etiqueta_em_curso" {
  description = <<-TXT
    Etiqueta que impede o menu do Flow Builder de reabrir durante um
    agendamento. Precisa bater EXATAMENTE com o valor usado na condição de
    inscrição do fluxo de entrada; divergir aqui faz o paciente cair de volta
    no menu a cada resposta.
  TXT
  type    = string
  default = "agendamento-em-curso"
}

variable "sleekflow_etiqueta_recepcao" {
  description = <<-TXT
    Etiqueta de transferência. A Platform API não atribui conversa a uma
    equipe (o assignee só aceita staffId), então a entrega à Recepção é feita
    por etiqueta, que dispara um fluxo no Flow Builder.
  TXT
  type    = string
  default = "transferir-recepcao"
}

variable "sleekflow_staff_id_recepcao" {
  description = "Atribuição direta a um atendente, quando a clínica preferir. Vazio deixa só a etiqueta."
  type        = string
  default     = ""
}

variable "sleekflow_webhook_secret" {
  description = <<-TXT
    Segredo compartilhado que o serviço confere no webhook.

    Segue obrigatório mesmo numa subida sem as demais credenciais: ele é o que
    impede qualquer um de postar nos endpoints públicos. Vazio desliga a
    verificação.
  TXT
  type        = string
  sensitive   = true
}

# ---------------------------------------------------------------------------
# Automação
# ---------------------------------------------------------------------------

variable "confirmacao_ativa" {
  description = <<-TXT
    Liga o Módulo 2 (confirmação de véspera e lembrete único).

    Padrão false. O handoff da SleekFlow registra que a clínica ainda não
    decidiu se haverá lembrete automático e alerta para não duplicar o que ela
    já envia por outro meio. Com false, os jobs nem são registrados no Quartz;
    o polling da agenda continua, porque é dele que sai o par de
    identificadores da Amplimed.
  TXT
  type    = bool
  default = false
}

variable "cron_expiracao_conversa" {
  description = <<-TXT
    Expiração de conversa abandonada. Não é higiene de banco: é o que devolve
    o menu a quem desistiu no meio, tirando a etiqueta de agendamento em
    curso. Desligar acumula pacientes presos sem menu.
  TXT
  type    = string
  default = "0 0/10 * * * ?"
}

variable "leitura_agenda_via_api" {
  description = "Cenário A da Cláusula 3ª. false degrada para o espelho local."
  type        = bool
  default     = true
}

variable "cron_sincronizacao_agenda" {
  description = "Polling da agenda. A Amplimed não oferece webhook nesta integração."
  type        = string
  default     = "0 0/15 * * * ?"
}

variable "dias_sincronizacao_agenda" {
  type    = number
  default = 2
}

variable "env_extra" {
  description = <<-TXT
    Variáveis adicionais no padrão Secao__Chave, para ajustar cron de
    confirmação, janela de envio ou nomes de template sem mexer no Terraform.
  TXT
  type    = map(string)
  default = {}
}

variable "log_max_size" {
  description = "Tamanho máximo de cada arquivo de log por container."
  type        = string
  default     = "10m"
}

variable "log_max_file" {
  description = "Quantos arquivos de log rotacionados manter."
  type        = string
  default     = "3"
}
