#!/usr/bin/env bash
set -euo pipefail

scm_host=$(az webapp show --resource-group test --name ClaimsModule --query "enabledHostNames[?contains(@, '.scm.')]|[0]" --output tsv)
if [[ ! "$scm_host" =~ ^[a-zA-Z0-9.-]+\.scm\.[a-zA-Z0-9.-]+$ ]]; then
    echo "::error::Could not determine the App Service SCM hostname."
    exit 1
fi
token=$(az account get-access-token --resource https://management.azure.com/ --query accessToken --output tsv)
if [[ -z "$token" ]]; then
    echo "::error::Could not obtain an Azure access token."
    exit 1
fi
echo "::add-mask::$token"
base="https://$scm_host"
job="$base/api/triggeredwebjobs/claims-migrations"
workdir=$(mktemp -d)
trap 'rm -rf -- "$workdir"' EXIT

get_status() {
    local url=$1
    curl --silent --show-error --max-time 20 --output "$workdir/body" --write-out '%{http_code}' \
        --header "Authorization: Bearer $token" "$url"
}

for attempt in {1..24}; do
    status=$(get_status "$job") || status=000
    if [[ "$status" == 200 ]]; then break; fi
    if [[ "$status" == 401 || "$status" == 403 ]]; then
        echo "::error::SCM authorization failed ($status). Check App Service publishing permissions."
        exit 1
    fi
    if [[ "$status" != 404 && "$status" != 000 && "$status" != 5?? ]]; then
        echo "::error::SCM WebJob lookup returned HTTP $status."
        exit 1
    fi
    sleep 10
done
if [[ "$status" != 200 ]]; then
    echo "::error::The migrations WebJob did not become available."
    exit 1
fi

status=$(curl --silent --show-error --max-time 20 --request POST --dump-header "$workdir/headers" \
    --output /dev/null --write-out '%{http_code}' --header "Authorization: Bearer $token" "$job/run") || {
    echo "::error::WebJob trigger outcome is unknown. Check its history before retrying."
    exit 1
}
if [[ "$status" != 202 ]]; then
    echo "::error::WebJob trigger returned HTTP $status. Check its history before retrying."
    exit 1
fi
location=$(awk 'BEGIN { IGNORECASE=1 } /^Location:/ { sub(/^[^:]*:[[:space:]]*/, ""); sub(/\r$/, ""); print; exit }' "$workdir/headers")
if [[ "$location" == /api/triggeredwebjobs/claims-migrations/history/* ]]; then location="$base$location"; fi
prefix="$job/history/"
run_id=${location#"$prefix"}
if [[ "$run_id" == "$location" || ! "$run_id" =~ ^[a-zA-Z0-9_-]+$ ]]; then
    echo "::error::WebJob returned an invalid run location."
    exit 1
fi

for attempt in {1..30}; do
    status=$(get_status "$location") || status=000
    if [[ "$status" == 200 ]]; then
        state=$(jq -r '.status // empty' "$workdir/body")
        case "$state" in
            Success) echo "EF Core migrations succeeded."; exit 0 ;;
            Failed|Error|Aborted) echo "::error::Migrations ended with status $state. See WebJob logs."; exit 1 ;;
        esac
    elif [[ "$status" == 401 || "$status" == 403 ]]; then
        echo "::error::SCM authorization failed while checking the run ($status)."
        exit 1
    elif [[ "$status" != 404 && "$status" != 000 && "$status" != 5?? ]]; then
        echo "::error::WebJob status returned HTTP $status."
        exit 1
    fi
    sleep 10
done
echo "::error::Timed out waiting for EF Core migrations."
exit 1
