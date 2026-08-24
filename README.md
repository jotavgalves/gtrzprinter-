# GTRZ Printer 2.0

Aplicativo Windows da GTRZ para transformar uma impressora térmica USB local em uma impressora de rede simples de instalar e administrar.

Esta versão foi reescrita do zero. Ela **não depende de nenhuma versão anterior do GTRZ Printer**.

## Princípios

- UI preta e vermelha, baseada na identidade visual oficial da GTRZ.
- Responsiva: rede, WMI, impressão e serviços não bloqueiam a thread da interface.
- Executável self-contained x64: o PC não precisa ter .NET instalado.
- Instalador próprio com atalho no Desktop e Menu Iniciar.
- Servidor IPP para o Windows reconhecer `GTRZ POS-80` como impressora.
- API HTTP dedicada para o ecossistema GTRZ.
- Descoberta automática do servidor na rede local.
- PCs conectados e última atividade em tempo real.
- Configuração de papel, área útil, DPI, largura raster, colunas, corte e avanço.
- Acesso às propriedades nativas do driver físico.
- Inicialização automática e bandeja do Windows.
- Logs persistentes e diagnóstico visual.
- Nenhuma janela de PowerShell no uso normal.

## Portas

| Serviço | Porta padrão |
|---|---:|
| IPP | TCP 631 |
| API GTRZ | TCP 9101 |
| Discovery | UDP 9102 |

Ao iniciar como servidor, o aplicativo verifica essas portas. Se encontrar um **servidor legado reconhecido da GTRZ/PowerShell** ocupando uma delas, encerra o processo antigo antes de subir os novos serviços. Um processo desconhecido não é encerrado silenciosamente: o aplicativo informa o conflito em vez de matar software arbitrário.

## Arquitetura

```text
PC cliente
  Windows / GTRZ POS-80
           |
           | IPP :631
           v
PC servidor
  GTRZ Printer 2.0
    |-- IPP :631
    |-- API :9101
    |-- Discovery UDP :9102
           |
           v
       POS-80 local
           |
          USB
```

## Identidade visual

O SVG oficial vermelho da GTRZ está versionado em:

`src/GTRZPrinter/Assets/GtrzLogo.svg`

A interface usa fundo preto, superfícies grafite e vermelho GTRZ `#c31d23` como cor principal.

## Build

O projeto usa:

- .NET 8 Windows / WinForms
- `win-x64`
- `SelfContained=true`
- single-file publish
- Inno Setup 6

O GitHub Actions compila e valida:

1. restore;
2. publish self-contained;
3. existência de `GTRZ Printer.exe`;
4. compilação do instalador Inno Setup;
5. geração do ZIP portátil;
6. verificação de que os artefatos existem e não estão vazios.

O artefato do workflow contém:

- `GTRZ-Printer-Setup.exe`
- `GTRZ-Printer-Portable.zip`

## Instalação

Use `GTRZ-Printer-Setup.exe` gerado pelo workflow. O instalador:

- solicita elevação de administrador;
- encerra instâncias antigas `GTRZ Printer.exe` / `GTRZPrinter.exe`;
- remove a tarefa de inicialização antiga;
- instala a nova versão em Program Files;
- cria atalho no Desktop;
- cria atalho no Menu Iniciar;
- abre o aplicativo ao terminar.

O próprio executável recria firewall/autostart conforme as configurações atuais.

## Modos

### Auto

Se a fila selecionada for uma impressora local USB/DOT4, o PC se torna servidor. Caso contrário, funciona como cliente.

### Server

Força o PC a hospedar IPP/API/Discovery.

### Client

Força o PC a conectar e instalar a impressora do servidor.

## Estado

Versão atual: **2.0.0**.
