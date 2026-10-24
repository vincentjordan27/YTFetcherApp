-- Run once with psql as the pre-existing PostgreSQL cluster administrator.
-- The invoking command supplies that administrator as "-U username_global" and
-- connects it to the existing "postgres" database. Replace every dummy value
-- below before execution.
--
-- This is a provisioning script, not a schema migration. It creates the database
-- and login role that will own the tables created by migrations/001_initial_schema.sql.
-- It is intentionally not idempotent: a second run should be investigated rather
-- than silently changing an existing role or database.

CREATE ROLE username_app_fetcher
    LOGIN
    PASSWORD 'replace_with_a_unique_long_password';

CREATE DATABASE database_name
    OWNER username_app_fetcher;

\connect database_name

CREATE EXTENSION IF NOT EXISTS pgcrypto;
