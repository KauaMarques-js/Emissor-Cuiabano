# Changelog

Todas as mudanças relevantes do projeto. Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/).

## [0.1.0] - 2026-10-24

### Adicionado
- Primeiro acesso numa tela só: conta por e-mail, cadastro da empresa preenchido pelo CNPJ (BrasilAPI) e pelo CEP (ViaCEP), tipos de nota, modo de emissão e certificado A1.
- Emissão de **NFS-e pelo Sistema Nacional** (Sefin Nacional): DPS no leiaute v1.01 validada contra o XSD oficial, assinatura XMLDSig e envio com mTLS. Também consulta (inclusive recuperação após timeout), cancelamento e substituição.
- NF-e modelo 55 em modo simulação.
- Tela única de emissão, com o resultado na hora (autorizada, rejeitada, pendente ou sem resposta) e PDF/XML para baixar.
- Cadastros de clientes, serviços e produtos; emissão em lote; central de rejeições; relatórios; auditoria; backup.
- Certificado A1 guardado criptografado (Data Protection/DPAPI).
- CI com build e testes a cada push e pull request.
