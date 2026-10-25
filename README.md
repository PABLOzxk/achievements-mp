# Achievements MP

App Windows para acompanhar conquistas da Steam: biblioteca, progresso por jogo, metas e um planejador de 100%.
Tudo fica num banco SQLite local; a Steam é consultada só para leitura.

## Estado atual: Fase 1 (fundação)

| Pronto | Ainda não |
| --- | --- |
| Solução .NET 10 (LTS) em camadas | Sincronização com a Steam (Fase 2) |
| Banco SQLite com migrações, backup automático e rollback | Biblioteca, filtros, listas na UI (Fase 3) |
| Configurações persistidas; chave da API cifrada com DPAPI | Planejador e guias (Fase 4) |
| Cliente da Steam Web API (6 endpoints) com erros classificados | Instalador (Fase 5) |
| Tema escuro estilo Steam, navegação lateral, 4 telas + Configurações | |
| 53 testes xUnit cobrindo as regras obrigatórias do plano | |

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
