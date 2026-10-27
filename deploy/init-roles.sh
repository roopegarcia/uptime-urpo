#!/bin/sh
set -eu
# Runs once on an empty volume. Passwords never enter container environment metadata.
MIGRATION_PASSWORD=$(cat /run/secrets/migration_password)
RUNTIME_PASSWORD=$(cat /run/secrets/runtime_password)
export MIGRATION_PASSWORD RUNTIME_PASSWORD
exec sh /opt/uptime/create-roles.sh
