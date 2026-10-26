# Achievements MP

App Windows para acompanhar conquistas da Steam: biblioteca, progresso por jogo, metas e um planejador de 100%.
Tudo fica num banco SQLite no seu computador; a Steam é consultada só para leitura. Não é afiliado à Valve nem à Steam.

## Instalar

1. Baixe o **`AchievementsMP.App-win-Setup.exe`** da [última versão](https://github.com/PABLOzxk/achievements-mp/releases/latest) e abra.
2. O instalador não é assinado digitalmente, então o Windows mostra **"O Windows protegeu o computador"**.
   Clique em **Mais informações → Executar assim mesmo**. Isso só acontece na instalação: as atualizações o app baixa sozinho.
3. Na primeira abertura, o guia **Primeiros passos** pede o link do seu perfil Steam e uma chave pessoal da Steam Web API,
   e confere se o perfil está público. Nenhuma senha é pedida.
4. Em **Configurações → Backup**, escolha uma pasta do OneDrive, Google Drive ou um pendrive. O que você anota e planeja
   no app não existe na Steam; o backup é a única cópia fora do PC.

Sem instalar: o `AchievementsMP.App-win-Portable.zip` roda de qualquer pasta, mas não se atualiza sozinho.

- **Atualizações:** o app procura versões novas no GitHub ao abrir, baixa em segundo plano e avisa no menu lateral.
  A troca acontece quando você clica em "Reiniciar e atualizar" ou na próxima vez que abrir o app.
- **Desinstalar** (Configurações do Windows → Aplicativos) remove o programa, mas mantém seus dados em
  `%LocalAppData%\AchievementsMP`. Apague essa pasta se quiser remover tudo.
- **Privacidade:** veja [PRIVACIDADE.md](PRIVACIDADE.md). Não há servidor do app nem telemetria.

## Estado atual: Fase 5 (validação e distribuição)

| Pronto | Ainda não |
| --- | --- |
| Solução .NET 10 (LTS) em camadas, SQLite com migrações e backup | Assinatura digital do instalador (paga; fora do escopo por ora) |
| Sincronização incremental com a Steam (biblioteca, conquistas, raridade) | |
| Biblioteca: busca, filtros (situação, viabilidade, lista) e 6 ordenações | |
| Detalhes do jogo: conquistas com estado, data e raridade global | |
| Listas personalizadas e favoritos (menu do botão direito) | |
| Viabilidade de 100% pela raridade da conquista mais rara que falta | |
| Planejador de 100%: classificação por conquista, anotações, guias, checklist | |
| Metas com progresso automático (100%, conquista, N por dia/semana) | |
| Guia de primeira configuração que confere perfil, chave e privacidade | |
| Backup diário numa pasta escolhida e restauração com conferência | |
| Instalador e atualização automática pelas Releases do GitHub (Velopack) | |
| Testes xUnit + teste de fumaça das telas no Windows, CI no Windows | |

## Planejador de 100%

A Steam não diz se uma conquista é perdível, rápida ou demorada. No planejador, **você** classifica cada uma
(e anota a fonte); o que não for marcado fica como "não sei", nunca como um palpite do programa.

- **Ordem do plano** (configurável): perdíveis → rápidas → sem classificação → demoradas; ou rápidas primeiro; ou só
  pela raridade. Dentro de cada grupo: prioridade alta primeiro, depois da mais comum para a mais rara.
- **Por conquista:** perdível (sim / não / não sei), esforço, prioridade, requisito especial, fonte, anotação e links de guia.
- **Por jogo:** checklist de tarefas próprias (com data de conclusão e ordem) e links de guias.
- **Metas:** 100% num jogo, uma conquista, N conquistas (no total até um prazo, por dia ou por semana) ou meta livre.
  O progresso vem das datas de desbloqueio da Steam. Sem dados da Steam a meta fica "sem dados", nunca "falhou".
  Metas de 100% e de conquista se concluem sozinhas e continuam concluídas mesmo se um DLC trouxer conquistas novas.
- **Links:** só `http`/`https` são aceitos e abertos, conferido ao salvar e de novo ao abrir.

Ponto cego: o plano só é tão bom quanto a sua classificação. Sem ela, vira "mais comuns primeiro", que é raridade,
não tempo. Tempo até 100% não vem da Steam, e o HowLongToBeat não tem API pública.

## Viabilidade (o que é e o que não é)

A Steam não informa se uma conquista é difícil, rápida ou perdível. O app usa o único dado objetivo disponível: a
**porcentagem global** de jogadores que desbloquearam cada conquista. A viabilidade de um jogo é a faixa da conquista
**mais rara que ainda falta**: Viável (≥ 20%), Moderado (5–20%), Difícil (1–5%), Muito difícil (< 1%).

Limite conhecido: a porcentagem é sobre todos os donos do jogo, inclusive quem nunca jogou. Conquistas do fim da
campanha parecem raras só porque muita gente abandona antes. É um indicador para priorizar, não uma medida de dificuldade.

## Como a sincronização funciona

1. Lê a biblioteca (`GetOwnedGames`). Resposta vazia = perfil ou "Detalhes do jogo" privados: para e avisa, sem tocar nos dados.
2. Decide jogo a jogo o que reler (`SyncPlanner`): a primeira vez lê tudo; depois, só jogos com tempo de jogo novo, jogados desde a última leitura, que falharam antes, com idioma trocado ou com dados mais velhos que 7 dias (progresso) / 30 dias (lista de conquistas). Jogos sem conquistas são reconferidos a cada 30 dias.
3. Para cada jogo: esquema + raridade global (quando preciso) e progresso do jogador. Cada jogo é gravado sozinho: se cair a internet no meio, o que já foi salvo fica.
4. Até 4 jogos em paralelo, no máximo ~6 chamadas/s. Erros de rede/5xx são repetidos (2s, 4s, 8s); 429 pausa todas as chamadas pelo tempo que a Steam pedir.
5. Chave inválida ou "Detalhes do jogo" privados interrompem tudo na hora, em vez de gastar centenas de chamadas.
6. Trocar de conta descarta o progresso da conta anterior (dado da Steam), mas nunca listas, metas ou anotações.

## Backup e restauração

- Um backup por dia, ao abrir ou ao fechar o app, na pasta escolhida. Ficam os 10 mais recentes; a limpeza só apaga
  arquivos com o nome exato `AchievementsMP-backup-AAAA-MM-DD_HHMMSS.db`, nunca outros arquivos da pasta.
- Cada backup é um arquivo único (sem `-wal` ao lado), gravado como `.partial` e renomeado no fim: um backup
  interrompido nunca parece válido.
- **Restaurar** confere o arquivo (cabeçalho SQLite, integridade, versão e tabelas), mostra o que tem dentro e reinicia
  o app. A troca acontece na abertura, antes de qualquer conexão com o banco, depois de guardar uma cópia dos dados
  atuais em `backups\antes-de-restaurar-*.db`. Arquivo inválido é descartado sem tocar nos dados; backup de versão
  antiga é migrado.
- A chave da Steam vai no backup, cifrada pelo Windows: num PC ou usuário diferente ela não abre e o app pede de novo.

## Publicar uma versão

1. Suba a `<Version>` em `src/AchievementsMP.App/AchievementsMP.App.csproj` e, se quiser notas, crie `docs/notas/vX.Y.Z.md`.
2. Rode o workflow **Release** manualmente (Actions → Release → Run workflow). Ele roda os testes, gera o instalador
   com o Velopack e, num Windows limpo, instala, atualiza para uma versão de teste e desinstala, conferindo que a
   pasta de dados não foi tocada. O resultado é um **rascunho** nas Releases, que só quem tem acesso ao repositório vê.
3. Baixe o `Setup.exe` do rascunho e teste. Se estiver bom, clique em **Publish release** no rascunho. A partir daí
   o app instalado de quem já usa encontra a versão nova sozinho.

Rodar o workflow de novo para a mesma versão substitui o rascunho. Uma versão publicada nunca é substituída: suba a
`<Version>`. Também dá para publicar direto, sem rascunho, enviando a tag (`git tag vX.Y.Z && git push origin vX.Y.Z`).
O `PACK_ID` (`AchievementsMP.App`) nunca deve mudar, porque é ele que liga as instalações às atualizações.

## Rodar no Windows (desenvolvimento)

1. Instale o [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Na pasta do projeto:

```powershell
dotnet test                                  # roda os testes (os de interface só no Windows)
dotnet run --project src/AchievementsMP.App  # abre o app
```

Ou abra `AchievementsMP.slnx` no Visual Studio 2026 / Rider.

Os dados ficam em `%LocalAppData%\AchievementsMP` (`achievements.db`, `backups\`, `logs\`).

## Estrutura

```
src/
  AchievementsMP.Core    Domínio puro: jogos, conquistas, progresso, configurações. Sem dependências.
  AchievementsMP.Data    SQLite (Microsoft.Data.Sqlite + Dapper), migrações e repositórios.
  AchievementsMP.Steam   Steam Web API atrás de ISteamWebApi. Parser separado do HTTP.
  AchievementsMP.Sync    Sincronização incremental e a verificação do guia de configuração.
  AchievementsMP.App     WPF (MVVM com CommunityToolkit.Mvvm). Só esta camada depende do Windows.
tests/
  AchievementsMP.Tests   xUnit. Banco real em arquivo temporário; Steam com respostas gravadas.
  AchievementsMP.UiTests Só Windows: abre cada tela com dados e falha em erro de XAML ou de binding.
```

## Regras que o código garante (e os testes cobrem)

- **A sincronização nunca apaga dados pessoais.** Listas, metas, tarefas, notas e guias ficam em tabelas sem cascata para as tabelas da Steam. Jogo que sai da biblioteca vira `is_owned = 0`; conquista que sai do esquema vira `is_removed = 1`. Nada é apagado.
- **Falha não zera progresso.** Uma falha só muda `availability`; o progresso salvo continua lá, com a data da última sincronização boa.
- **Sem duplicatas.** Chaves naturais (`app_id`, `app_id + api_name`) e upserts idempotentes.
- **Dado ausente não vira pendente.** Conquista sem estado conhecido é `Unknown`. O percentual é um piso e `IsExact` diz se pode ser exibido como definitivo.
- **Resposta vazia da biblioteca = perfil privado**, não "biblioteca vazia". Ela nunca marca seus jogos como não possuídos.
- **Migrações seguras.** Backup antes de alterar um banco existente, cada migração numa transação, recusa abrir banco de versão mais nova.
- **A chave da API não vaza.** Cifrada com DPAPI (usuário atual), nunca incluída em mensagens de erro ou logs, e o cliente HTTP não segue redirecionamentos (a chave vai na URL e não pode ser levada para outro endereço).
- **Banco danificado tem saída.** Se o arquivo não abre, o app oferece restaurar um backup ou começar do zero, e guarda o arquivo danificado em `backups\` em vez de apagar.
- **Dados do banco não viram comandos.** Links de guia só abrem se forem http/https; ícones só carregam por https (um caminho de rede num backup adulterado não é acessado).

Veja também [SEGURANCA.md](SEGURANCA.md).

## Steam: o que é preciso saber

- Cada usuário usa **a própria chave** da Steam Web API (gratuita, em <https://steamcommunity.com/dev/apikey>). Por isso não há servidor nem chave embutida no executável.
- O perfil **e** a opção "Detalhes do jogo" precisam estar **públicos**, senão a Steam devolve a biblioteca vazia e recusa as conquistas.
- A API oficial **não informa** se uma conquista é perdível, difícil ou rápida. Esses dados são marcados por você no planejador (com a fonte), e o padrão é "desconhecido".
- `GetGlobalAchievementPercentagesForApp` não exige chave; os demais exigem.
