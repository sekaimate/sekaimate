package kube

import (
	"context"
	"fmt"
	"log"

	metav1 "k8s.io/apimachinery/pkg/apis/meta/v1"
)

// localMeetingID is the id bootstrapLocalMeeting (cmd/server/main.go) uses
// for the Compose single-room deployment. That meeting is never routed
// through Manager.Create — it is registered directly against
// controlplane.Store from BASIS_MEETING_PUBLIC_HOST/SetPort/Password — so
// Reconcile must not expect a matching GameServer for it (design.md §12
// decision 1 keeps this bootstrap path independent of Kubernetes).
const localMeetingID = "local"

// Reconcile implements design.md §12 decision 2: at startup, Kubernetes is
// treated as the source of truth for concierge-managed meetings. It lists
// every GameServer/Secret labeled app=basis-server in the namespace and
// cross-checks them against m.meetings:
//
//   - A MeetingRecord (other than the "local" bootstrap meeting) with no
//     matching GameServer is marked "failed" — its backing compute is gone,
//     so the stale record must not keep advertising itself as usable.
//   - A GameServer or Secret with no matching MeetingRecord is logged as
//     orphaned. It is intentionally NOT deleted: Reconcile only detects and
//     reports inconsistency, it does not attempt destructive cleanup of
//     resources it doesn't fully understand the history of.
//
// Reconcile requires meetings (unlike Create/Delete, where a nil Store just
// disables the background Ready-watch) — it has no synchronous fallback
// behavior to degrade to.
func (m *Manager) Reconcile(ctx context.Context) error {
	if m.meetings == nil {
		return fmt.Errorf("kube: Reconcile requires a non-nil meetings store")
	}

	selector := metav1.ListOptions{LabelSelector: appLabelKey + "=" + appLabelValue}

	gsList, err := m.agones.AgonesV1().GameServers(m.cfg.Namespace).List(ctx, selector)
	if err != nil {
		return fmt.Errorf("kube: reconcile: list gameservers: %w", err)
	}
	secretList, err := m.core.CoreV1().Secrets(m.cfg.Namespace).List(ctx, selector)
	if err != nil {
		return fmt.Errorf("kube: reconcile: list secrets: %w", err)
	}

	gameServerIDs := make(map[string]bool, len(gsList.Items))
	for _, gs := range gsList.Items {
		gameServerIDs[gs.Labels[instanceLabelKey]] = true
	}
	secretIDs := make(map[string]bool, len(secretList.Items))
	for _, s := range secretList.Items {
		secretIDs[s.Labels[instanceLabelKey]] = true
	}

	recordIDs := make(map[string]bool)
	for _, rec := range m.meetings.List() {
		recordIDs[rec.Id] = true
		if rec.Id == localMeetingID {
			continue
		}
		if !gameServerIDs[rec.Id] {
			log.Printf("kube: reconcile: meeting %s has no matching GameServer %s; marking failed", rec.Id, gameServerName(rec.Id))
			m.meetings.UpdateStatus(rec.Id, "failed", "No matching Kubernetes GameServer was found at startup reconciliation.", "", 0)
		}
	}

	for id := range gameServerIDs {
		if !recordIDs[id] {
			log.Printf("kube: reconcile: orphaned GameServer %s has no matching MeetingRecord (not deleted)", gameServerName(id))
		}
	}
	for id := range secretIDs {
		if !recordIDs[id] {
			log.Printf("kube: reconcile: orphaned Secret %s has no matching MeetingRecord (not deleted)", secretName(id))
		}
	}
	return nil
}
