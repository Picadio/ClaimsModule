#!/usr/bin/env bash
set -euo pipefail

cd -- "$(dirname -- "$0")"

export ASPNETCORE_ENVIRONMENT=Production

for name in ClaimsMigrations Claims; do
    for prefix in ConnectionStrings__ SQLAZURECONNSTR_ SQLCONNSTR_ CUSTOMCONNSTR_; do
        setting="${prefix}${name}"
        if [ -n "${!setting:-}" ]; then
            export ConnectionStrings__Claims="${!setting}"
            chmod +x ./efbundle
            exec ./efbundle
        fi
    done
done

echo "Configure ClaimsMigrations or Claims in App Service connection strings." >&2
exit 1
