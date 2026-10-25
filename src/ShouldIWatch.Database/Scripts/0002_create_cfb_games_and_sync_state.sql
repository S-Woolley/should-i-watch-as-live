-- College football: cached games and the timestamps that throttle calls to the data provider.

CREATE TABLE dbo.cfb_season_sync (
    season              int       NOT NULL CONSTRAINT pk_cfb_season_sync PRIMARY KEY,
    schedule_fetched_at datetime2 NULL
);

-- Postseason is stored as season_type 'postseason', week 0.
CREATE TABLE dbo.cfb_week_sync (
    season               int         NOT NULL,
    season_type          varchar(16) NOT NULL,
    week                 int         NOT NULL,
    results_fetched_at   datetime2   NULL,
    reference_fetched_at datetime2   NULL,
    CONSTRAINT pk_cfb_week_sync PRIMARY KEY (season, season_type, week)
);

-- Keyed by provider so a different data source starts with a fresh cache. Times are UTC.
-- Only the internal id is ever exposed by the API. Line scores are never stored.
CREATE TABLE dbo.cfb_game (
    id                int IDENTITY(1, 1) NOT NULL CONSTRAINT pk_cfb_game PRIMARY KEY,
    provider          varchar(32)        NOT NULL,
    provider_game_id  nvarchar(64)       NOT NULL,
    season            int                NOT NULL,
    season_type       varchar(16)        NOT NULL,
    week              int                NOT NULL,
    start_date        datetime2          NOT NULL,
    start_time_tbd    bit                NOT NULL,
    home_team         nvarchar(100)      NOT NULL,
    away_team         nvarchar(100)      NOT NULL,
    completed         bit                NOT NULL CONSTRAINT df_cfb_game_completed DEFAULT 0,
    home_points       int                NULL,
    away_points       int                NULL,
    went_to_overtime  bit                NOT NULL CONSTRAINT df_cfb_game_went_to_overtime DEFAULT 0,
    home_pregame_elo  int                NULL,
    away_pregame_elo  int                NULL,
    excitement_index  float              NULL,
    home_spread       float              NULL,
    home_rank         int                NULL,
    away_rank         int                NULL,
    updated_at        datetime2          NOT NULL,
    CONSTRAINT uq_cfb_game_provider_game UNIQUE (provider, provider_game_id)
);

CREATE INDEX ix_cfb_game_week ON dbo.cfb_game (provider, season, season_type, week, start_date);
