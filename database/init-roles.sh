#!/bin/sh
set -eu

# Only runs when PostgreSQL initializes a NEW volume. Passwords arrive via private env.
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --no-password --set ON_ERROR_STOP=1 <<'SQL'
\getenv migration_password MIGRATION_PASSWORD
\getenv runtime_password RUNTIME_PASSWORD
CREATE ROLE uptime_urpo_migrator LOGIN PASSWORD :'migration_password' NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE ROLE uptime_urpo_runtime LOGIN PASSWORD :'runtime_password' NOSUPERUSER NOCREATEDB NOCREATEROLE;
ALTER DATABASE uptime_urpo OWNER TO uptime_urpo_migrator;
REVOKE ALL ON DATABASE uptime_urpo FROM PUBLIC;
GRANT CONNECT ON DATABASE uptime_urpo TO uptime_urpo_runtime;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO uptime_urpo_migrator;
GRANT USAGE ON SCHEMA public TO uptime_urpo_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE uptime_urpo_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO uptime_urpo_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE uptime_urpo_migrator IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO uptime_urpo_runtime;
SQL
