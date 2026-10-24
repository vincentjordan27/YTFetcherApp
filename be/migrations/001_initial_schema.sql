-- Prerequisite: run migrations/000_provision_database.sql once as the PostgreSQL cluster administrator.
-- Run this baseline while connected as the application role to the application database created by that script.
-- Do not add \connect or credentials here; the invoking psql command supplies them.

BEGIN;

CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    firebase_uid VARCHAR(128) NOT NULL,
    email VARCHAR(320) NOT NULL,
    display_name VARCHAR(200) NULL,
    role VARCHAR(20) NOT NULL DEFAULT 'ADMIN',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    last_login_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by UUID NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by UUID NULL,
    deleted_at TIMESTAMPTZ NULL,
    deleted_by UUID NULL,

    CONSTRAINT uq_users_firebase_uid UNIQUE (firebase_uid),
    CONSTRAINT fk_users_created_by
        FOREIGN KEY (created_by)
        REFERENCES users(id),
    CONSTRAINT fk_users_modified_by
        FOREIGN KEY (modified_by)
        REFERENCES users(id),
    CONSTRAINT fk_users_deleted_by
        FOREIGN KEY (deleted_by)
        REFERENCES users(id)
);

CREATE UNIQUE INDEX uq_users_email_lower ON users (lower(email));

INSERT INTO users (
    id,
    firebase_uid,
    email,
    display_name,
    role,
    is_active,
    is_deleted,
    created_at,
    modified_at)
VALUES (
    '00000000-0000-0000-0000-000000000001',
    'SYSTEM',
    'system@ytfetcher.invalid',
    'SYSTEM',
    'ADMIN',
    TRUE,
    FALSE,
    now(),
    now());

CREATE TABLE auth_login_events (
    id BIGSERIAL PRIMARY KEY,
    user_id UUID NULL,
    logged_in_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    user_agent TEXT NULL,
    success BOOLEAN NOT NULL,
    failure_reason TEXT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by UUID NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by UUID NULL,
    deleted_at TIMESTAMPTZ NULL,
    deleted_by UUID NULL,

    CONSTRAINT fk_auth_login_events_user
        FOREIGN KEY (user_id)
        REFERENCES users(id),
    CONSTRAINT fk_auth_login_events_created_by
        FOREIGN KEY (created_by)
        REFERENCES users(id),
    CONSTRAINT fk_auth_login_events_modified_by
        FOREIGN KEY (modified_by)
        REFERENCES users(id),
    CONSTRAINT fk_auth_login_events_deleted_by
        FOREIGN KEY (deleted_by)
        REFERENCES users(id)
);

CREATE TABLE discovery_keywords (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    keyword VARCHAR(200) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by UUID NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by UUID NULL,
    deleted_at TIMESTAMPTZ NULL,
    deleted_by UUID NULL,

    CONSTRAINT fk_discovery_keywords_created_by
        FOREIGN KEY (created_by)
        REFERENCES users(id),
    CONSTRAINT fk_discovery_keywords_modified_by
        FOREIGN KEY (modified_by)
        REFERENCES users(id),
    CONSTRAINT fk_discovery_keywords_deleted_by
        FOREIGN KEY (deleted_by)
        REFERENCES users(id)
);

CREATE UNIQUE INDEX uq_discovery_keywords_keyword_lower
    ON discovery_keywords (lower(keyword));

CREATE INDEX ix_discovery_keywords_active
    ON discovery_keywords (is_active, is_deleted, created_at);

CREATE TABLE discovery_states (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    keyword_id UUID NOT NULL,
    last_recent_completed_at TIMESTAMPTZ NULL,
    recent_window_start TIMESTAMPTZ NULL,
    recent_window_end TIMESTAMPTZ NULL,
    recent_page_token TEXT NULL,
    recent_completed_quota_date DATE NULL,
    history_window_start TIMESTAMPTZ NULL,
    history_window_end TIMESTAMPTZ NULL,
    history_page_token TEXT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by UUID NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by UUID NULL,
    deleted_at TIMESTAMPTZ NULL,
    deleted_by UUID NULL,

    CONSTRAINT uq_discovery_states_keyword UNIQUE (keyword_id),
    CONSTRAINT fk_discovery_states_keyword
        FOREIGN KEY (keyword_id)
        REFERENCES discovery_keywords(id),
    CONSTRAINT fk_discovery_states_created_by
        FOREIGN KEY (created_by)
        REFERENCES users(id),
    CONSTRAINT fk_discovery_states_modified_by
        FOREIGN KEY (modified_by)
        REFERENCES users(id),
    CONSTRAINT fk_discovery_states_deleted_by
        FOREIGN KEY (deleted_by)
        REFERENCES users(id)
);

CREATE TABLE channels (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    youtube_channel_id VARCHAR(64) NOT NULL,
    name VARCHAR(500) NOT NULL,
    thumbnail_url TEXT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    uploads_playlist_id VARCHAR(64) NULL,
    backfill_status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    backfill_cutoff_at TIMESTAMPTZ NULL,
    backfill_page_token TEXT NULL,
    backfill_claim_token UUID NULL,
    backfill_claimed_at TIMESTAMPTZ NULL,
    backfill_failure_count INTEGER NOT NULL DEFAULT 0,
    backfill_failure_reason VARCHAR(500) NULL,
    backfill_last_failed_at TIMESTAMPTZ NULL,
    backfill_scheduling_attempted_at TIMESTAMPTZ NULL,
    backfill_scheduling_failure_reason VARCHAR(500) NULL,
    backfill_ready_at TIMESTAMPTZ NULL,
    first_page_quota_date DATE NULL,
    backfill_pages_completed INTEGER NOT NULL DEFAULT 0,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    discovered_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    approved_at TIMESTAMPTZ NULL,
    approved_by_user_id UUID NULL,
    rejected_at TIMESTAMPTZ NULL,
    rejected_by_user_id UUID NULL,
    rejection_reason VARCHAR(500) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by UUID NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by UUID NULL,
    deleted_at TIMESTAMPTZ NULL,
    deleted_by UUID NULL,
    last_upload_check_at TIMESTAMPTZ NULL,

    CONSTRAINT uq_channels_youtube_channel_id UNIQUE (youtube_channel_id),
    CONSTRAINT ck_channels_backfill_failure_count_range
        CHECK (backfill_failure_count BETWEEN 0 AND 3),
    CONSTRAINT ck_channels_backfill_pages_completed_nonnegative
        CHECK (backfill_pages_completed >= 0),
    CONSTRAINT fk_channels_approved_by_user
        FOREIGN KEY (approved_by_user_id)
        REFERENCES users(id),
    CONSTRAINT fk_channels_rejected_by_user
        FOREIGN KEY (rejected_by_user_id)
        REFERENCES users(id),
    CONSTRAINT fk_channels_created_by
        FOREIGN KEY (created_by)
        REFERENCES users(id),
    CONSTRAINT fk_channels_modified_by
        FOREIGN KEY (modified_by)
        REFERENCES users(id),
    CONSTRAINT fk_channels_deleted_by
        FOREIGN KEY (deleted_by)
        REFERENCES users(id)
);

CREATE INDEX ix_channels_pending
    ON channels (status, is_deleted, discovered_at DESC);

CREATE INDEX ix_channels_backfill_candidates
    ON channels (approved_at, id)
    WHERE status = 'APPROVED' AND is_deleted = FALSE AND backfill_status <> 'COMPLETED';

CREATE INDEX ix_channels_backfill_continuation_dispatch
    ON channels (backfill_ready_at, id)
    WHERE status = 'APPROVED'
      AND is_deleted = FALSE
      AND backfill_status = 'PENDING';

CREATE INDEX ix_channels_backfill_dispatch_recovery
    ON channels (backfill_scheduling_attempted_at, id)
    WHERE status = 'APPROVED'
      AND is_deleted = FALSE
      AND backfill_status IN ('SCHEDULING', 'SCHEDULING_FAILED');

CREATE INDEX ix_channels_upload_monitor_candidates
    ON channels (last_upload_check_at, id)
    WHERE status = 'APPROVED'
      AND is_deleted = FALSE
      AND backfill_status = 'COMPLETED';

CREATE TABLE channel_discoveries (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    channel_id UUID NOT NULL,
    keyword_id UUID NOT NULL,
    youtube_video_id VARCHAR(32) NOT NULL,
    video_title VARCHAR(1000) NOT NULL,
    video_thumbnail_url TEXT NULL,
    video_published_at TIMESTAMPTZ NOT NULL,
    discovered_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by UUID NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by UUID NULL,
    deleted_at TIMESTAMPTZ NULL,
    deleted_by UUID NULL,

    CONSTRAINT uq_channel_discoveries_youtube_video_id
        UNIQUE (youtube_video_id),
    CONSTRAINT fk_channel_discoveries_channel
        FOREIGN KEY (channel_id)
        REFERENCES channels(id),
    CONSTRAINT fk_channel_discoveries_keyword
        FOREIGN KEY (keyword_id)
        REFERENCES discovery_keywords(id),
    CONSTRAINT fk_channel_discoveries_created_by
        FOREIGN KEY (created_by)
        REFERENCES users(id),
    CONSTRAINT fk_channel_discoveries_modified_by
        FOREIGN KEY (modified_by)
        REFERENCES users(id),
    CONSTRAINT fk_channel_discoveries_deleted_by
        FOREIGN KEY (deleted_by)
        REFERENCES users(id)
);

CREATE INDEX ix_channel_discoveries_keyword ON channel_discoveries (keyword_id);

CREATE TABLE videos (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    youtube_video_id VARCHAR(32) NOT NULL,
    channel_id UUID NOT NULL,
    title VARCHAR(1000) NOT NULL,
    thumbnail_url TEXT NULL,
    published_at TIMESTAMPTZ NOT NULL,
    duration_millis BIGINT NOT NULL,
    current_views BIGINT NOT NULL DEFAULT 0,
    current_likes BIGINT NOT NULL DEFAULT 0,
    is_reference BOOLEAN NOT NULL DEFAULT FALSE,
    reference_set_by_user_id UUID NULL,
    reference_set_at TIMESTAMPTZ NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by UUID NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by UUID NULL,
    deleted_at TIMESTAMPTZ NULL,
    deleted_by UUID NULL,
    tracking_status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE',
    consecutive_failures INT NOT NULL DEFAULT 0,
    last_stats_fetched_at TIMESTAMPTZ NULL,
    availability_status VARCHAR(20) NOT NULL DEFAULT 'AVAILABLE',
    stats_claim_token UUID NULL,
    stats_claimed_at TIMESTAMPTZ NULL,

    CONSTRAINT uq_videos_youtube_video_id UNIQUE (youtube_video_id),
    CONSTRAINT ck_videos_availability_status
        CHECK (availability_status IN ('AVAILABLE', 'UNAVAILABLE')),
    CONSTRAINT ck_videos_stats_claim_coherent
        CHECK ((stats_claim_token IS NULL) = (stats_claimed_at IS NULL)),
    CONSTRAINT fk_videos_channel
        FOREIGN KEY (channel_id)
        REFERENCES channels(id),
    CONSTRAINT fk_videos_reference_set_by_user
        FOREIGN KEY (reference_set_by_user_id)
        REFERENCES users(id),
    CONSTRAINT fk_videos_created_by
        FOREIGN KEY (created_by)
        REFERENCES users(id),
    CONSTRAINT fk_videos_modified_by
        FOREIGN KEY (modified_by)
        REFERENCES users(id),
    CONSTRAINT fk_videos_deleted_by
        FOREIGN KEY (deleted_by)
        REFERENCES users(id)
);

CREATE INDEX ix_videos_stats_due
    ON videos (last_stats_fetched_at, id)
    WHERE is_deleted = FALSE
      AND is_reference = FALSE
      AND tracking_status = 'ACTIVE';

CREATE TABLE video_snapshots (
    id BIGSERIAL PRIMARY KEY,
    video_id UUID NOT NULL,
    captured_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    view_count BIGINT NOT NULL,
    like_count BIGINT NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by UUID NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by UUID NULL,
    deleted_at TIMESTAMPTZ NULL,
    deleted_by UUID NULL,

    CONSTRAINT fk_video_snapshots_video
        FOREIGN KEY (video_id)
        REFERENCES videos(id),
    CONSTRAINT fk_video_snapshots_created_by
        FOREIGN KEY (created_by)
        REFERENCES users(id),
    CONSTRAINT fk_video_snapshots_modified_by
        FOREIGN KEY (modified_by)
        REFERENCES users(id),
    CONSTRAINT fk_video_snapshots_deleted_by
        FOREIGN KEY (deleted_by)
        REFERENCES users(id)
);

CREATE TABLE youtube_quota_usage (
    id BIGSERIAL PRIMARY KEY,
    endpoint VARCHAR(100) NOT NULL,
    called_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    units INT NOT NULL,
    success BOOLEAN NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by UUID NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by UUID NULL,
    deleted_at TIMESTAMPTZ NULL,
    deleted_by UUID NULL,

    CONSTRAINT fk_youtube_quota_usage_created_by
        FOREIGN KEY (created_by)
        REFERENCES users(id),
    CONSTRAINT fk_youtube_quota_usage_modified_by
        FOREIGN KEY (modified_by)
        REFERENCES users(id),
    CONSTRAINT fk_youtube_quota_usage_deleted_by
        FOREIGN KEY (deleted_by)
        REFERENCES users(id)
);

CREATE TABLE youtube_daily_quota_usage (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    quota_date DATE NOT NULL,
    queries_units_used INTEGER NOT NULL DEFAULT 0,
    video_batch_stats_units_used INTEGER NOT NULL DEFAULT 0,
    search_query_units_used INTEGER NOT NULL DEFAULT 0,
    first_page_attempt_limit INTEGER NOT NULL DEFAULT 0,
    first_page_attempt_used INTEGER NOT NULL DEFAULT 0,
    backfill_dispatch_opened_at TIMESTAMPTZ NULL,
    daily_processing_claim_token UUID NULL,
    daily_processing_claimed_at TIMESTAMPTZ NULL,
    daily_processing_completed_at TIMESTAMPTZ NULL,
    search_quota_exhausted_at TIMESTAMPTZ NULL,
    queries_quota_exhausted_at TIMESTAMPTZ NULL,
    video_batch_stats_quota_exhausted_at TIMESTAMPTZ NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by UUID NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by UUID NULL,
    deleted_at TIMESTAMPTZ NULL,
    deleted_by UUID NULL,

    CONSTRAINT fk_youtube_daily_quota_usage_created_by
        FOREIGN KEY (created_by)
        REFERENCES users(id),
    CONSTRAINT fk_youtube_daily_quota_usage_modified_by
        FOREIGN KEY (modified_by)
        REFERENCES users(id),
    CONSTRAINT fk_youtube_daily_quota_usage_deleted_by
        FOREIGN KEY (deleted_by)
        REFERENCES users(id),
    CONSTRAINT ck_youtube_daily_quota_usage_counters_nonnegative CHECK (
        queries_units_used >= 0
        AND video_batch_stats_units_used >= 0
        AND search_query_units_used >= 0
        AND first_page_attempt_limit >= 0
        AND first_page_attempt_used >= 0),
    CONSTRAINT ck_youtube_daily_quota_usage_first_page_within_limit
        CHECK (first_page_attempt_used <= first_page_attempt_limit),
    CONSTRAINT ck_youtube_daily_quota_usage_daily_processing_claim_coherent
        CHECK ((daily_processing_claim_token IS NULL) = (daily_processing_claimed_at IS NULL))
);

CREATE UNIQUE INDEX uq_youtube_daily_quota_usage_quota_date
    ON youtube_daily_quota_usage (quota_date)
    WHERE is_deleted = FALSE;

CREATE TABLE channel_backfill_runs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    channel_id UUID NOT NULL,
    claim_token UUID NOT NULL,
    trigger_source VARCHAR(20) NOT NULL,
    page_number INTEGER NOT NULL,
    status VARCHAR(20) NOT NULL,
    started_at TIMESTAMPTZ NOT NULL,
    completed_at TIMESTAMPTZ NULL,
    failure_reason VARCHAR(500) NULL,
    failure_count_at_time INTEGER NULL,
    quota_limit_bucket VARCHAR(30) NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by UUID NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by UUID NULL,
    deleted_at TIMESTAMPTZ NULL,
    deleted_by UUID NULL,

    CONSTRAINT fk_channel_backfill_runs_channel
        FOREIGN KEY (channel_id)
        REFERENCES channels(id),
    CONSTRAINT fk_channel_backfill_runs_created_by
        FOREIGN KEY (created_by)
        REFERENCES users(id),
    CONSTRAINT fk_channel_backfill_runs_modified_by
        FOREIGN KEY (modified_by)
        REFERENCES users(id),
    CONSTRAINT fk_channel_backfill_runs_deleted_by
        FOREIGN KEY (deleted_by)
        REFERENCES users(id),
    CONSTRAINT ck_channel_backfill_runs_page_number_positive
        CHECK (page_number >= 1),
    CONSTRAINT ck_channel_backfill_runs_failure_count_nonnegative
        CHECK (failure_count_at_time IS NULL OR failure_count_at_time >= 0),
    CONSTRAINT ck_channel_backfill_runs_trigger_source
        CHECK (trigger_source IN ('APPROVAL', 'DAILY_PUMP', 'RECOVERY')),
    CONSTRAINT ck_channel_backfill_runs_status
        CHECK (status IN ('RUNNING', 'SUCCEEDED', 'FAILED', 'ABANDONED', 'QUOTA_LIMIT_REACHED')),
    CONSTRAINT ck_channel_backfill_runs_terminal_completed_at
        CHECK ((status = 'RUNNING' AND completed_at IS NULL)
            OR (status <> 'RUNNING' AND completed_at IS NOT NULL)),
    CONSTRAINT ck_channel_backfill_runs_failure_fields
        CHECK ((status = 'FAILED' AND failure_reason IS NOT NULL AND failure_count_at_time IS NOT NULL)
            OR (status <> 'FAILED' AND failure_reason IS NULL AND failure_count_at_time IS NULL)),
    CONSTRAINT ck_channel_backfill_runs_quota_limit_bucket
        CHECK ((status = 'QUOTA_LIMIT_REACHED' AND quota_limit_bucket IS NOT NULL
            AND quota_limit_bucket IN ('QUERIES', 'VIDEO_BATCH_STATS'))
            OR (status <> 'QUOTA_LIMIT_REACHED' AND quota_limit_bucket IS NULL))
);

CREATE UNIQUE INDEX uq_channel_backfill_runs_claim_token
    ON channel_backfill_runs (claim_token);

CREATE INDEX ix_channel_backfill_runs_channel_started
    ON channel_backfill_runs (channel_id, started_at DESC, id DESC);

CREATE INDEX ix_channel_backfill_runs_retention
    ON channel_backfill_runs (started_at, id)
    WHERE status <> 'RUNNING';

CREATE INDEX ix_channel_backfill_runs_list
    ON channel_backfill_runs (started_at DESC, id DESC)
    WHERE is_deleted = FALSE;

INSERT INTO discovery_keywords (keyword)
VALUES
    ('valo');

INSERT INTO discovery_states (keyword_id)
SELECT id
FROM discovery_keywords;

COMMIT;
