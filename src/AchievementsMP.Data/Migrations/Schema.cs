namespace AchievementsMP.Data.Migrations;

public static class Schema
{
    /// <summary>
    /// Regras do desenho:
    /// - Tabelas da Steam (games, achievements, player_achievements, achievement_global_stats) são reescritas pela sincronização.
    /// - Tabelas pessoais (user_lists, goals, plan_tasks, achievement_notes, achievement_plans, guides) não têm chave estrangeira com cascata
    ///   para as tabelas da Steam, então nada que a sincronização faça pode apagá-las.
    /// - Jogos e conquistas nunca são apagados pela sincronização; são marcados (is_owned = 0, is_removed = 1).
    /// </summary>
    public static readonly IReadOnlyList<Migration> All =
    [
        new(1, "initial", """
            CREATE TABLE games (
                app_id              INTEGER PRIMARY KEY,
                name                TEXT    NOT NULL,
                icon_hash           TEXT,
                playtime_minutes    INTEGER NOT NULL DEFAULT 0,
                last_played_utc     INTEGER,
                has_community_stats INTEGER,
                is_owned            INTEGER NOT NULL DEFAULT 1,
                availability        INTEGER NOT NULL DEFAULT 0,
                first_seen_utc      INTEGER NOT NULL,
                library_synced_utc  INTEGER NOT NULL,
                schema_synced_utc   INTEGER,
                player_synced_utc   INTEGER
            );

            CREATE TABLE achievements (
                app_id        INTEGER NOT NULL,
                api_name      TEXT    NOT NULL,
                display_name  TEXT    NOT NULL,
                description   TEXT,
                hidden        INTEGER NOT NULL DEFAULT 0,
                icon_url      TEXT,
                icon_gray_url TEXT,
                is_removed    INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (app_id, api_name)
            ) WITHOUT ROWID;

            CREATE TABLE player_achievements (
                app_id       INTEGER NOT NULL,
                api_name     TEXT    NOT NULL,
                state        INTEGER NOT NULL,
                unlocked_utc INTEGER,
                synced_utc   INTEGER NOT NULL,
                PRIMARY KEY (app_id, api_name)
            ) WITHOUT ROWID;

            CREATE TABLE achievement_global_stats (
                app_id      INTEGER NOT NULL,
                api_name    TEXT    NOT NULL,
                percent     REAL    NOT NULL,
                updated_utc INTEGER NOT NULL,
                PRIMARY KEY (app_id, api_name)
            ) WITHOUT ROWID;

            CREATE TABLE user_lists (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                name         TEXT    NOT NULL,
                description  TEXT,
                is_favorites INTEGER NOT NULL DEFAULT 0,
                created_utc  INTEGER NOT NULL
            );

            CREATE TABLE user_list_games (
                list_id   INTEGER NOT NULL REFERENCES user_lists(id) ON DELETE CASCADE,
                app_id    INTEGER NOT NULL,
                added_utc INTEGER NOT NULL,
                PRIMARY KEY (list_id, app_id)
            ) WITHOUT ROWID;

            CREATE TABLE goals (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                title        TEXT    NOT NULL,
                app_id       INTEGER,
                deadline_utc INTEGER,
                priority     INTEGER NOT NULL DEFAULT 1,
                status       INTEGER NOT NULL DEFAULT 0,
                created_utc  INTEGER NOT NULL
            );

            CREATE TABLE plan_tasks (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                app_id     INTEGER NOT NULL,
                api_name   TEXT,
                title      TEXT    NOT NULL,
                notes      TEXT,
                sort_order INTEGER NOT NULL DEFAULT 0,
                done_utc   INTEGER
            );
            CREATE INDEX ix_plan_tasks_app ON plan_tasks(app_id, sort_order);

            CREATE TABLE achievement_notes (
                app_id      INTEGER NOT NULL,
                api_name    TEXT    NOT NULL,
                note        TEXT    NOT NULL,
                updated_utc INTEGER NOT NULL,
                PRIMARY KEY (app_id, api_name)
            ) WITHOUT ROWID;

            CREATE TABLE guides (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                app_id      INTEGER NOT NULL,
                api_name    TEXT,
                url         TEXT    NOT NULL,
                language    TEXT,
                source      TEXT,
                created_utc INTEGER NOT NULL
            );
            CREATE INDEX ix_guides_app ON guides(app_id);

            CREATE TABLE sync_runs (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                scope         TEXT    NOT NULL,
                app_id        INTEGER,
                started_utc   INTEGER NOT NULL,
                finished_utc  INTEGER,
                status        INTEGER NOT NULL DEFAULT 0,
                error_kind    TEXT,
                error_message TEXT
            );
            CREATE INDEX ix_sync_runs_scope ON sync_runs(scope, app_id, started_utc);

            CREATE TABLE settings (
                key   TEXT PRIMARY KEY,
                value BLOB
            ) WITHOUT ROWID;
            """),

        new(2, "incremental_sync", """
            -- Tempo de jogo no momento da última leitura de conquistas: se mudou, o jogador jogou e vale reconsultar.
            ALTER TABLE games ADD COLUMN player_synced_playtime INTEGER;
            -- Idioma em que o esquema foi baixado: trocar o idioma nas configurações força nova leitura.
            ALTER TABLE games ADD COLUMN schema_language TEXT;
            -- Última falha por jogo, para mostrar o motivo na interface.
            ALTER TABLE games ADD COLUMN last_error TEXT;
            """),

        new(3, "planner", """
            -- Classificação pessoal de cada conquista para o planejador. A Steam não fornece nada disso.
            -- effort: 0 não sei, 1 rápida, 2 demorada · missable: 0 não sei, 1 perdível, 2 não perdível · priority: 0 baixa, 1 normal, 2 alta
            CREATE TABLE achievement_plans (
                app_id      INTEGER NOT NULL,
                api_name    TEXT    NOT NULL,
                effort      INTEGER NOT NULL DEFAULT 0,
                missable    INTEGER NOT NULL DEFAULT 0,
                priority    INTEGER NOT NULL DEFAULT 1,
                requirement TEXT,
                source      TEXT,
                updated_utc INTEGER NOT NULL,
                PRIMARY KEY (app_id, api_name)
            ) WITHOUT ROWID;

            ALTER TABLE guides ADD COLUMN title TEXT;

            -- kind: 0 livre, 1 100% no jogo, 2 conquista específica, 3 quantidade · period: 0 nenhum, 1 diário, 2 semanal
            ALTER TABLE goals ADD COLUMN kind INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE goals ADD COLUMN api_name TEXT;
            ALTER TABLE goals ADD COLUMN target INTEGER;
            ALTER TABLE goals ADD COLUMN period INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE goals ADD COLUMN done_utc INTEGER;

            ALTER TABLE plan_tasks ADD COLUMN created_utc INTEGER;

            -- Contagem de desbloqueios por período (metas semanais/diárias).
            CREATE INDEX ix_player_achievements_unlocked ON player_achievements(unlocked_utc) WHERE state = 2;
            """),
    ];

    public static int LatestVersion => All[^1].Version;
}
