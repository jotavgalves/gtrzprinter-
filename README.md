# GTRZ Printer

GTRZ Printer é o bridge de impressão local/rede da GTRZ.

## Objetivos do projeto

- Aplicativo Windows único, leve e responsivo.
- Interface preta e vermelha seguindo a identidade GTRZ.
- Servidor IPP para a POS-80 aparecer como impressora nativa no Windows.
- API local para impressão direta pelo ecossistema GTRZ.
- Descoberta automática de servidor e clientes na rede local.
- Lista de PCs conectados em tempo real.
- Configuração completa do bridge e acesso às propriedades nativas do driver.
- Inicialização automática e bandeja do Windows.
- Sem PowerShell visível no uso normal.
- Build self-contained: o usuário não precisa instalar .NET.
- Instalador independente de versões anteriores.
- Recuperação automática de portas ocupadas por servidores GTRZ antigos.

## Arquitetura

```text
PC cliente
  Windows / GTRZ POS-80
           |
           | IPP :631
           v
PC servidor (ex.: DANIELA)
  GTRZ Printer
    |-- IPP :631
    |-- API :9101
    |-- Discovery UDP :9102
           |
           v
       POS-80 local
           |
          USB
```

## Build

O GitHub Actions gera automaticamente:

- `GTRZ Printer.exe` self-contained x64
- instalador `GTRZ-Printer-Setup.exe`
- pacote portátil ZIP

A implementação está sendo criada do zero para substituir os protótipos anteriores.
