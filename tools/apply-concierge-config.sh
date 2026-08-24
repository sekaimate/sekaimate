#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
config_path="${repository_root}/local/concierge/appsettings.minikube.json"
legacy_config_path="${repository_root}/local/concierge/appsettings.json"
confirm=false
init_mode=false
custom_path=false

usage() {
  cat >&2 <<'EOF'
Usage: tools/apply-concierge-config.sh [--init]
       tools/apply-concierge-config.sh [--yes] [config-path]

Apply a local Concierge appsettings file to the minikube Secret and restart the
Concierge deployment. The default input is local/concierge/appsettings.minikube.json.
--init copies concierge/appsettings.minikube.example.json to the default local
path with mode 600, and never overwrites an existing file.
--yes is required because an emptyDir-backed /data loses meeting records when
the pod restarts and can leave GameServers orphaned.
EOF
}

for argument in "$@"; do
  case "$argument" in
    --init)
      init_mode=true
      ;;
    --yes)
      confirm=true
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    -*|"")
      echo "Unknown option: $argument" >&2
      usage
      exit 2
      ;;
    *)
      if [[ "$custom_path" == true ]]; then
        echo "Only one appsettings path may be specified." >&2
        exit 2
      fi
      custom_path=true
      config_path="$argument"
      ;;
  esac
done

if [[ "$config_path" != /* ]]; then
  config_path="${repository_root}/${config_path}"
fi

if [[ "$init_mode" == true ]]; then
  if [[ "$confirm" == true || "$custom_path" == true ]]; then
    echo "--init cannot be combined with --yes or an appsettings path." >&2
    exit 2
  fi
  init_source="${repository_root}/concierge/appsettings.minikube.example.json"
  if [[ -e "$legacy_config_path" && ! -e "$config_path" ]]; then
    echo "Found legacy config: $legacy_config_path" >&2
    echo "Move it to $config_path, then run this command again." >&2
    exit 1
  fi
  if [[ -e "$config_path" ]]; then
    echo "Refusing to overwrite existing Concierge config: $config_path" >&2
    exit 1
  fi
  mkdir -p "$(dirname "$config_path")"
  cp -- "$init_source" "$config_path"
  chmod 600 "$config_path"
  echo "Created $config_path; edit it locally before applying." >&2
  exit 0
fi

if [[ ! -f "$config_path" ]]; then
  echo "Concierge config does not exist: $config_path" >&2
  if [[ "$custom_path" != true && -e "$legacy_config_path" ]]; then
    echo "Found legacy config: $legacy_config_path" >&2
    echo "Move it to $config_path, then run tools/apply-concierge-config.sh --yes." >&2
  elif [[ "$custom_path" == true ]]; then
    echo "Provide an existing config path or create it before applying." >&2
  else
    echo "Run tools/apply-concierge-config.sh --init, then edit $config_path." >&2
  fi
  exit 1
fi

if ! command -v node >/dev/null 2>&1; then
  echo "node is required to validate appsettings.json." >&2
  exit 1
fi
if ! node -e 'JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"))' "$config_path"; then
  echo "Concierge config is not valid JSON: $config_path" >&2
  exit 1
fi
if ! command -v kubectl >/dev/null 2>&1; then
  echo "kubectl is required to apply concierge-config." >&2
  exit 1
fi

cat >&2 <<'EOF'
WARNING: restarting Concierge with an emptyDir-backed /data removes meeting
records. Existing GameServers may become orphaned. Delete/verify existing
meetings and GameServers before continuing.
EOF
if [[ "$confirm" != true ]]; then
  echo "Refusing to continue without --yes." >&2
  exit 2
fi

kubectl -n basis create secret generic concierge-config \
  --from-file="appsettings.json=${config_path}" \
  --dry-run=client -o yaml | kubectl apply -f -
kubectl -n basis rollout restart deployment/concierge
kubectl -n basis rollout status deployment/concierge --timeout=180s
