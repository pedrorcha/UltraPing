# UltraPing

<p>
  <img src="assets/UltraPing.svg" width="96" alt="UltraPing">
  <img src="assets/UltraPingViewer.svg" width="96" alt="UltraPingViewer">
</p>

Monitor de ping para Windows feito para NOC e para equipes de infraestrutura e broadcast. Ele pinga centenas de hosts ao mesmo tempo, mostra na tela só o que importa, puxa para o topo quem acabou de cair e separa queda de verdade de pacote perdido.

O projeto vem com dois programas:

| Programa | Para que serve |
|---|---|
| **UltraPing.exe** | Monitor em tempo real: uma grade de painéis com o ping de cada host. |
| **UltraPingViewer.exe** | Dashboard histórico: lê os logs CSV do UltraPing e mostra disponibilidade, latência, perda, quedas e padrões da rede. |

São dois programas de janela (WinForms), cada um com um único arquivo de código, para o **.NET Framework 4**, que já vem no Windows. Não é preciso instalar nada para compilar nem para rodar.

---

## UltraPing: monitor em tempo real

### Recursos

- **Até 512 hosts**, todos pingados o tempo todo, cada um na sua thread.
- **Páginas**: a tela mostra `WindowCount` painéis por vez (padrão 40, em grade 5 × 8 com painéis largos). Para trocar de página, use **◀ Página X/Y ▶** na barra ou **PageUp/PageDown**.
- **Fila dinâmica**: quando um host cai (fica vermelho), o painel dele vai para o **primeiro lugar** e a tela volta sozinha para a página 1. Se ele voltar a responder, fica onde está. A próxima queda toma o primeiro lugar.
- **Proteção contra falso alerta**: quando um ping falha, o programa faz mais `ConfirmPings` pings (padrão 2) antes de alertar. Se algum responder, o host **oscilou**: o painel fica **amarelo** por alguns minutos, sem som, sem subir e sem e-mail. Só fica **vermelho** quando todos os pings falham.
- **Som**: bipe de 1000 Hz por 150 ms em queda confirmada, no máximo um a cada 2 segundos.
- **Botão "Desativar"** nos painéis vermelhos: grava `Enabled=False` e tira o host da tela.
- **Verificação automática dos desativados**: ao abrir o programa **e** uma vez por dia, no horário configurado, todos os hosts desativados são testados em silêncio, e os que responderem voltam a ser monitorados.
- **Log CSV** de cada ping, por host e por dia.
- **Alertas por e-mail** (SMTP) com limite por minuto e por hora.
- **Perfis**: cada perfil tem sua lista de hosts e seus logs.
- **Opções** com a lista de hosts em grade (Nº, Ativo, IP, Nome). Dá para colar direto do Excel (IP [tab] Nome) com Ctrl+V.
- Duplo clique no título maximiza um painel. O botão direito abre o menu do painel (pausar, copiar, `ping -t` etc.).

### Cores dos painéis

| Cor | Significado |
|---|---|
| Verde | respondendo |
| Amarelo | oscilou: perdeu um ping, mas a confirmação respondeu |
| Vermelho | caiu: todos os pings de confirmação falharam |
| Cinza / marrom | sem dados ainda / host pausado |

---

## UltraPingViewer: dashboard histórico

Lê todos os CSV de `Profiles\<perfil>\Logs` (mesmo com o UltraPing aberto e gravando) e monta:

- **Visão geral**
  - indicadores de disponibilidade da rede (meta de 99,9%), hosts fora agora, quedas, tempo total fora, MTTR, latência média com p95 e p99, jitter e oscilações;
  - **diagnóstico automático**: quedas em massa, hosts que falham juntos (provável switch, porta, link, PoE ou energia em comum), quedas repetidas, hosts instáveis, latência e jitter altos, pior sub-rede ou VLAN, horário crítico e quais poucos hosts concentram a maior parte do tempo fora;
  - gráficos de disponibilidade, latência (média e p95) e perda no tempo, rankings dos piores hosts e uma linha do tempo dos hosts mais problemáticos.
- **Hosts**: tabela com disponibilidade, quedas, tempo fora, maior queda, MTTR, perda, oscilações, latência média, p95, p99, máxima, jitter e situação. Ordena por qualquer coluna e tem busca.
- **Incidentes**: quedas em massa (5 ou mais hosts caindo em até 90 s) e a lista de todas as quedas confirmadas.
- **Sub-redes / VLANs**: disponibilidade e latência por grupo (por `/24`, ou pelos nomes do `vlans.txt`).
- **Padrões**: mapas de falha e de oscilação por dia da semana × hora, distribuição da latência, grupos de hosts correlacionados e hosts que caem repetidamente.
- **Host**: o detalhe de um host, com latência e falhas no tempo, faixa de disponibilidade, falhas por hora, distribuição da latência e lista de quedas.

Também tem períodos prontos (última hora, 6 h, 24 h, hoje, ontem, 7 dias, 30 dias, tudo) ou personalizados, e exportação de **PNG** (a página inteira) e **CSV** (a tabela de hosts).

**Tempo real:** com **"Tempo real (10 s)"** ligado (o padrão), o Viewer relê os logs a cada 10 segundos, no mesmo ritmo do ping do UltraPing. A recarga é silenciosa e mantém a aba, a rolagem, a ordenação e a linha escolhida, então dá para deixar numa TV do NOC. Se uma leitura levar mais de 10 s (um período longo, como "Tudo" com meses de log), a próxima só começa quando ela terminar.

### Argumentos de linha de comando (opcionais)

```
UltraPingViewer.exe [pasta] [/periodo:1h|6h|24h|hoje|ontem|7d|30d|tudo] [/aba:0-5] [/host:IP]
```

- `pasta` pode ser a pasta do UltraPing, uma pasta `Profiles`, a pasta de um perfil ou uma pasta `Logs`. Serve, por exemplo, para abrir os logs de outra máquina pela rede.
- `/aba`: 0 = Visão geral, 1 = Hosts, 2 = Incidentes, 3 = Sub-redes, 4 = Padrões, 5 = Host.

### Glossário

- **Disponibilidade**: % dos ciclos de ping em que o host respondeu (oscilação conta como respondeu).
- **Queda**: sequência de ciclos com falha confirmada, do primeiro até o host voltar.
- **MTTR**: tempo médio até o host voltar depois de uma queda.
- **MTBF**: tempo médio entre quedas.
- **p95 / p99**: 95% / 99% das respostas foram mais rápidas que esse valor.
- **Jitter**: variação média da latência entre pings seguidos. Importa para áudio e vídeo em tempo real.

---

## Estrutura da pasta

```
UltraPing\
├─ UltraPing.exe            monitor (gerado pelo compilar.bat)
├─ UltraPingViewer.exe      dashboard (gerado pelo compilar.bat)
├─ config.ini               modelo copiado para o perfil Default na primeira execução
├─ UltraPing.ini            guarda o último perfil usado
├─ vlans.txt                (opcional) nomes das sub-redes/VLANs para o Viewer
├─ Profiles\<perfil>\
│   ├─ config.ini           configuração do perfil (a que vale de verdade)
│   └─ Logs\AAAA-MM-DD\JanelaNNN.csv
└─ src\
    ├─ UltraPing.cs
    ├─ UltraPingViewer.cs
    └─ compilar.bat
```

## Como compilar

1. Feche o UltraPing e o UltraPingViewer.
2. Dê dois cliques em `src\compilar.bat`.
3. Os dois `.exe` são gerados na pasta principal.

O `compilar.bat` usa o `csc.exe` que vem com o .NET Framework 4 (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\`). Esse compilador só entende **C# 5**: no código não use `?.`, `$"..."`, `=>` em membros, `nameof` nem `out var`.

## Primeira execução

1. Abra o `UltraPing.exe`. De fábrica ele já começa pingando só a própria máquina (`localhost` e `127.0.0.1`).
2. Na primeira vez, o `config.ini` é copiado para `Profiles\Default\config.ini`, e a partir daí vale esse arquivo do perfil. Coloque seus hosts em **Opções → Hosts** (ou no `config.ini`, usando o `config.example.ini` como modelo).
3. Para ter outros perfis, crie `Profiles\<nome>\config.ini` e troque no botão **Perfil**.

## Configuração (`config.ini`)

### `[Hosts]`

```ini
IP-1=localhost
Name-1=localhost
Enabled-1=True
IP-2=127.0.0.1
Name-2=127.0.0.1
Enabled-2=True
```

Vão até 512 hosts. O número no título do painel é a posição do host no arquivo. Hosts com `Enabled=False` não aparecem e não são pingados, mas entram na verificação automática dos desativados.

### Demais chaves

| Seção | Chave | Padrão | Descrição |
|---|---|---|---|
| `[Window]` | `WindowCount` | 40 | Painéis por página |
| | `Columns` | 0 | Colunas da grade (0 = automático, prefere painéis largos) |
| | `PromoteAfter` | 1 | Falhas confirmadas seguidas para o host subir ao topo |
| `[Ping]` | `PingTimeDelta` | 1000 | Intervalo entre pings de cada host, em ms (recomendado: 10000) |
| | `PingTimeout` | 2000 | Timeout de cada ping, em ms |
| | `ConfirmPings` | 2 | Pings extras antes de confirmar uma falha (0 = desligado) |
| | `UnstableMinutes` | 10 | Minutos em amarelo depois de uma oscilação |
| | `PingAutoStart` | True | Começar a pingar ao abrir |
| `[General]` | `PlaySounds` | True | Bipe em queda confirmada |
| | `LogToFiles` | True | Gravar o log CSV |
| | `ShowTime` | True | Hora em cada linha do painel |
| | `AutoScroll` | True | Rolar o painel automaticamente |
| | `PingResultsView` | Compact | `Basic`, `Compact`, `Minimal` ou `Detailed` |
| | `MaxLines` | 500 | Linhas guardadas por painel |
| `[Recheck]` | `Enabled` | True | Testar os desativados ao abrir e uma vez por dia |
| | `Time` | 08:00 | Horário do teste diário (HH:mm) |
| | `LastRun` | (automático) | Data do último teste |
| `[Mail]` | `MailAlerts` | False | Enviar alertas por e-mail |
| | `SendAfter` | 3 | Falhas confirmadas seguidas para enviar |
| | `MaxPerMinute` / `MaxPerHour` | 1 / 10 | Limites de envio por host (0 = sem limite) |
| | `SMTPServer`, `SMTPServerPort`, `SMTPSSL`, `SMTPUser`, `SMTPPass`, `MailFrom`, `MailTo` | | Servidor e contas |
| | `MailSubject`, `MailBody` | | Modelos, com as variáveis `%HOST_ID%` `%HOST_NAME%` `%HOST_IP%` `%HOST_STATUS%` `%DATE%` `%PL_COUNT%` |

> A senha do SMTP fica em texto puro no `config.ini`. Não publique o seu `config.ini`.

## Formato do log CSV

`Profiles\<perfil>\Logs\AAAA-MM-DD\JanelaNNN.csv` (UTF-8, separador `;`):

```
DataHora;Janela;Nome;Host;Status;Tempo_ms;TTL;Detalhe
2026-10-30 16:42:17;2;127.0.0.1;127.0.0.1;OK;0;128;0 ms
2026-10-30 16:42:27;2;127.0.0.1;127.0.0.1;OSCILOU;1;128;OSCILOU, confirmado 1 ms
2026-10-30 16:42:37;2;127.0.0.1;127.0.0.1;FALHA;;;FALHA (timeout)
```

`Status` vale `OK`, `OSCILOU` (perdeu um ping, mas a confirmação respondeu) ou `FALHA` (queda confirmada).

## Sub-redes / VLANs no Viewer (`vlans.txt`)

Opcional, ao lado do `UltraPingViewer.exe`. Veja o `vlans.example.txt`:

```
127.0.0.0/8;Loopback
```

Sem esse arquivo, os hosts são agrupados por `/24`.

## Desempenho

- Com cerca de 300 hosts, o UltraPing usa umas 300 threads e menos de 100 MB de RAM.
- O Viewer lê cerca de 2,5 milhões de linhas de log por segundo. Um dia de 300 hosts a cada 10 s tem cerca de 2,6 milhões de linhas.

## Requisitos

- Windows 7 ou mais novo com .NET Framework 4.x (já vem no Windows 10 e 11).
- Permissão para enviar ICMP (ping) aos hosts.

## Histórico de alterações

### 30/10/2026

- **Primeira versão pública** do UltraPing e do UltraPingViewer.
- **Configuração de fábrica:** o programa vem só com `localhost` e `127.0.0.1` e começa a pingar ao abrir. Perfis novos sem `config.ini` são criados assim.
- **`PingAutoStart`:** quando a chave não existe no `config.ini`, agora vale `True`.
- **Logos:** verde para o UltraPing e azul para o UltraPingViewer, em SVG, PNG e ICO na pasta `assets`. Os ícones ficam nos `.exe` e nas janelas.
- **`compilar.bat`:** agora gera os dois programas, cada um com o seu ícone.
- **`.gitattributes`:** mantém os arquivos `.bat` com quebra de linha do Windows.
