namespace EARS.Integration.UnitTests;

/// <summary>
/// Respostas copiadas dos exemplos da documentação da Amplimed (coleções Postman
/// "API Agendamento Amplimed Referência" e "API Cadastro de Pacientes Amplimed").
/// Servem para travar o parsing contra o formato real, não contra o que
/// imaginamos que ele seja.
/// </summary>
public static class PayloadsDaDocumentacao
{
    public const string ListarProfissionais = """
    [
        {
            "nomeProfissional": "Médico Teste Integração",
            "codigoProfissional": "654e42a2fb7e795963e1eb14",
            "conselho": "CRM",
            "numeroConselho": "131059",
            "ufConselho": "SC",
            "especialidades": "Neurologia",
            "telemedicina": false,
            "nomeConsultorio": "Consultorio Integracao",
            "codigoIbge": 4200404,
            "datasDisponiveis": ["2023-11-14", "2023-11-21", "2023-11-28"],
            "procedimentos": [
                { "id": "62e98815e40b7a5d67b11189", "nome": "Consulta", "active": true, "external_id": 1 },
                { "id": "62ea6651e98b3b49b942b1c4", "nome": "Retorno", "active": true, "external_id": 2 }
            ],
            "convenios": [
                { "id": "62e98815e40b7a5d67b11187", "nome": "Particular", "active": true, "external_id": 1 }
            ]
        }
    ]
    """;

    public const string ListarHorarios = """
    [
        {
            "idHorario": "654e42a2fb7e795963e1eb14_2023-11-14_08:00:00_08:15:00_false_2_8",
            "dataHora": "2023-11-14 08:00:00",
            "codigoProfissional": "654e42a2fb7e795963e1eb14",
            "teleconsulta": false,
            "procedimento": 8,
            "procedimentoDesc": "Consulta 1",
            "convenio": 2,
            "convenioDesc": "Descricao_Convenio",
            "valor": 150.2
        },
        {
            "idHorario": "654e42a2fb7e795963e1eb14_2023-11-14_08:15:00_08:30:00_false_2_8",
            "dataHora": "2023-11-14 08:15:00",
            "codigoProfissional": "654e42a2fb7e795963e1eb14",
            "teleconsulta": false,
            "procedimento": 8,
            "procedimentoDesc": "Consulta 1",
            "convenio": 2,
            "convenioDesc": "UNIMED TESTE INTEGRAÇÃO",
            "valor": 90
        }
    ]
    """;

    public const string ListarAgendamentos = """
    {
        "profissionais": [
            {
                "nome": "Dr João",
                "especialidade": "Cardiologista",
                "conselho": "CRM",
                "codConselho": "1234",
                "ufConselho": "SC",
                "agendamentos": [
                    {
                        "usuclin": "1",
                        "idAgendamento": 1,
                        "data_hora": "2025-04-11T15:00:00",
                        "duracao": 30,
                        "status": "Agendado",
                        "procedimento": "Consulta",
                        "convenio": "",
                        "unidade": "Clínica X",
                        "enderecoUnidade": "Rua y - n123",
                        "beneficiario": {
                            "codp": 1,
                            "codRef": "xyz",
                            "cpf": "111.111.111-11",
                            "nr_carteira": "",
                            "nome": "Cliente da Silva"
                        }
                    }
                ]
            },
            {
                "nome": "Dra Maria",
                "especialidade": "Acupuntura",
                "conselho": "CRFA",
                "codConselho": "4321",
                "ufConselho": "SC",
                "agendamentos": [
                    {
                        "usuclin": "1",
                        "idAgendamento": 2,
                        "data_hora": "2025-04-11T09:30:00",
                        "duracao": 30,
                        "status": "Cancelado",
                        "procedimento": "Retorno",
                        "convenio": "",
                        "unidade": "",
                        "enderecoUnidade": "",
                        "beneficiario": {
                            "codp": 2,
                            "codRef": null,
                            "cpf": "222.222.222-22",
                            "nr_carteira": "12345688",
                            "nome": "Paciente Integração"
                        }
                    }
                ]
            }
        ],
        "currentPage": 1,
        "totalPages": 1
    }
    """;

    public const string CadastrarAgendamento = """
    {
        "message": "Appointment created successfully",
        "id": "195fb405-40bb-40d6-9f52-a22d3d6369b9"
    }
    """;

    public const string AlterarStatus = """
    { "message": "Status alterado com sucesso" }
    """;

    public const string BuscarPacienteArray = """
    [
        {
            "codReferencia": "123",
            "nomeSocial": "Hiury teste23",
            "nomeRegistro": "Hiury teste23",
            "dataNascimento": "01/01/1998",
            "genero": "M",
            "cpf": "16485703055",
            "rg": "4444444",
            "celular": "(48) 99999-9999",
            "email": "paciente@example.com",
            "cidade": "Florianópolis",
            "uf": "SC"
        }
    ]
    """;

    public const string BuscarPacienteEnvelope = """
    {
        "data": [
            {
                "codReferencia": "",
                "nomeSocial": "John Connor",
                "nomeRegistro": "John Connor",
                "dataNascimento": "28/02/1985",
                "genero": "M",
                "cpf": "13037981903",
                "celular": "",
                "endereco": "water mills 16"
            }
        ]
    }
    """;

    public const string CadastrarPaciente = """
    { "success": true, "message": "Patient saved successfully" }
    """;
}
