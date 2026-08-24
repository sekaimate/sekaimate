#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
profile="${MINIKUBE_PROFILE:-minikube}"
namespace="${K8S_NAMESPACE:-basis}"
pid_dir="${K8S_PID_DIR:-${repository_root}/.tmp/k8s}"
mkdir -p "$pid_dir"

cleanup() {
  for pid_file in "$pid_dir"/gameserver-*.pid; do
    [[ -f "$pid_file" ]] || continue
    kill "$(<"$pid_file")" 2>/dev/null || true
  done
  rm -f "$pid_dir"/gameserver-*.pid "$pid_dir"/gameserver-*.log
}
trap cleanup EXIT INT TERM

while true; do
  while IFS= read -r name; do
    [[ -n "$name" ]] || continue
    state="$(kubectl --context "$profile" -n "$namespace" get gameserver "$name" \
      -o jsonpath='{.status.state}' 2>/dev/null || true)"
    [[ "$state" == "Ready" ]] || continue
    port="$(kubectl --context "$profile" -n "$namespace" get gameserver "$name" \
      -o jsonpath='{range .status.ports[*]}{.name}{"="}{.port}{"\n"}{end}' 2>/dev/null \
      | awk -F= '$1 == "websocket" { print $2; exit }')"
    [[ "$port" =~ ^[0-9]+$ ]] || continue

    pid_file="$pid_dir/gameserver-${name}.pid"
    if [[ -f "$pid_file" ]] && kill -0 "$(<"$pid_file")" 2>/dev/null; then
      continue
    fi

    log_file="$pid_dir/gameserver-${name}.log"
    kubectl --context "$profile" -n "$namespace" port-forward "pod/$name" "$port:4297" >"$log_file" 2>&1 &
    echo "$!" > "$pid_file"
    echo "Forwarded $name: 127.0.0.1:$port -> 4297"
  done < <(
    kubectl --context "$profile" -n "$namespace" get gameservers \
      -o jsonpath='{range .items[*]}{.metadata.name}{"\n"}{end}' 2>/dev/null || true
  )

  sleep 2
done
