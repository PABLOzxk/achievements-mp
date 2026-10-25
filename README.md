# Achievements MP

App Windows para acompanhar conquistas da Steam: biblioteca, progresso por jogo, metas e um planejador de 100%.
Tudo fica num banco SQLite local; a Steam é consultada só para leitura.

## Estado atual: Fase 3 (organização)

| Pronto | Ainda não |
| --- | --- |
| Solução .NET 10 (LTS) em camadas, SQLite com migrações e backup | Planejador, metas e guias (Fase 4) |
| Sincronização incremental com a Steam (biblioteca, conquistas, raridade) | Anotações por conquista na interface (Fase 4) |
| Biblioteca: busca, filtros (situação, viabilidade, lista) e 6 ordenações | Instalador (Fase 5) |
| Detalhes do jogo: conquistas com estado, data e raridade global | |
| Listas personalizadas e favoritos (menu do botão direito) | |
| Viabilidade de 100% pela raridade da conquista mais rara que falta | |
| 107 testes xUnit, CI no Windows | |

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

## Rodar no Windows

1. Instale o [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Na pasta do projeto:

```powershell
dotnet test                                  # roda os testes
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
  AchievementsMP.App     WPF (MVVM com CommunityToolkit.Mvvm). Só esta camada depende do Windows.
tests/
  AchievementsMP.Tests   xUnit. Banco real em arquivo temporário; Steam com respostas gravadas.
```

## Regras que o código garante (e os testes cobrem)

- **A sincronização nunca apaga dados pessoais.** Listas, metas, tarefas, notas e guias ficam em tabelas sem cascata para as tabelas da Steam. Jogo que sai da biblioteca vira `is_owned = 0`; conquista que sai do esquema vira `is_removed = 1`. Nada é apagado.
- **Falha não zera progresso.** Uma falha só muda `availability`; o progresso salvo continua lá, com a data da última sincronização boa.
- **Sem duplicatas.** Chaves naturais (`app_id`, `app_id + api_name`) e upserts idempotentes.
- **Dado ausente não vira pendente.** Conquista sem estado conhecido é `Unknown`. O percentual é um piso e `IsExact` diz se pode ser exibido como definitivo.
- **Resposta vazia da biblioteca = perfil privado**, não "biblioteca vazia". Ela nunca marca seus jogos como não possuídos.
- **Migrações seguras.** Backup antes de alterar um banco existente, cada migração numa transação, recusa abrir banco de versão mais nova.
- **A chave da API não vaza.** Cifrada com DPAPI (usuário atual) e nunca incluída em mensagens de erro.

## Steam: o que é preciso saber

- Cada usuário usa **a própria chave** da Steam Web API (gratuita, em <https://steamcommunity.com/dev/apikey>). Por isso não há servidor nem chave embutida no executável.
- O perfil **e** a opção "Detalhes do jogo" precisam estar **públicos**, senão a Steam devolve a biblioteca vazia e recusa as conquistas.
- A API oficial **não informa** se uma conquista é perdível, difícil ou rápida. Esses dados vão ser marcados por você (ou vir de fontes externas com link), e o padrão é "desconhecido".
- `GetGlobalAchievementPercentagesForApp` não exige chave; os demais exigem.
