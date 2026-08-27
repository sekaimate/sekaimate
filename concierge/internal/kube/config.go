package kube

import (
	"os"
	"path/filepath"

	"k8s.io/client-go/rest"
	"k8s.io/client-go/tools/clientcmd"
)

// ResolveRESTConfig resolves a Kubernetes REST config using the same
// order basis-k8s's cmd/server/main.go does (docs/concierge/design.md §8):
// in-cluster config first, then $KUBECONFIG, then $HOME/.kube/config.
//
// ok is false when neither an in-cluster environment nor a kubeconfig file
// is present at all — that is not an error, it is the normal state for a
// concierge deployment that has no Kubernetes integration configured
// (non-k8s deployments keep working exactly as in phase 1, backed by
// NoopProvisioner; see cmd/server/main.go). err is only set when a
// kubeconfig file is present but fails to parse/build, which is a genuine
// misconfiguration the caller should surface.
func ResolveRESTConfig() (cfg *rest.Config, ok bool, err error) {
	if inCluster, icErr := rest.InClusterConfig(); icErr == nil {
		return inCluster, true, nil
	}

	kubeconfig := os.Getenv("KUBECONFIG")
	if kubeconfig == "" {
		home, herr := os.UserHomeDir()
		if herr != nil {
			return nil, false, nil
		}
		kubeconfig = filepath.Join(home, ".kube", "config")
	}
	if _, statErr := os.Stat(kubeconfig); statErr != nil {
		return nil, false, nil
	}

	cfg, err = clientcmd.BuildConfigFromFlags("", kubeconfig)
	if err != nil {
		return nil, false, err
	}
	return cfg, true, nil
}
